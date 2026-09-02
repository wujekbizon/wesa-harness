from pathlib import Path
import hashlib

import pytest
import yaml

from wesa_engine.plugins.registry import PluginRegistry
from wesa_engine.plugins.contracts import PluginError
from wesa_engine.sandbox import SandboxExecutionRequest, SandboxJobBuilder, SandboxPolicy
from wesa_engine.sandbox import SandboxExecutionResult
from wesa_engine.sandbox.kubernetes import SandboxError
from wesa_engine.kernel.auth import sign_for_test
from wesa_engine.kernel.contracts import TaskEnvelopeV2
from wesa_engine.runtime.server import HarnessProtocolServer
from test_protocol_server import KEY, initialize, runtime_root, signed_task


ROOT = Path(__file__).resolve().parents[2]


def test_job_uses_immutable_image_and_restricted_pod_controls():
    policy = SandboxPolicy.from_file(ROOT / "config" / "sandbox-policy.json")
    resources = SandboxJobBuilder(policy).build(
        SandboxExecutionRequest("a" * 32, "python-compile", "text/x-python", b"print('safe')\n")
    )
    config_map, job = resources["items"]
    pod = job["spec"]["template"]["spec"]
    container = pod["containers"][0]

    assert config_map["binaryData"]["artifact.py"]
    assert "@sha256:" in container["image"]
    assert container["command"][:3] == ["python", "-I", "-c"]
    assert "subprocess.run" in container["command"][3]
    assert "timeout=30" in container["command"][3]
    assert "/workspace/source/artifact.py" in container["command"][3]
    barrier = pod["initContainers"][0]
    assert barrier["name"] == "network-policy-barrier"
    assert barrier["image"] == container["image"]
    assert barrier["securityContext"]["readOnlyRootFilesystem"] is True
    assert "socket.create_connection" in barrier["command"][3]
    assert pod["automountServiceAccountToken"] is False
    assert pod["enableServiceLinks"] is False
    assert pod["securityContext"]["runAsNonRoot"] is True
    assert pod["securityContext"]["seccompProfile"] == {"type": "RuntimeDefault"}
    assert container["securityContext"]["allowPrivilegeEscalation"] is False
    assert container["securityContext"]["readOnlyRootFilesystem"] is True
    assert container["securityContext"]["capabilities"]["drop"] == ["ALL"]
    assert container["resources"]["limits"] == {"cpu": "250m", "memory": "128Mi"}
    assert {value["name"]: value["value"] for value in container["env"]}["PYTHONPYCACHEPREFIX"] == "/workspace/work/pycache"
    assert next(value for value in container["volumeMounts"] if value["name"] == "source")["readOnly"] is True
    assert job["spec"]["activeDeadlineSeconds"] == 60
    assert job["spec"]["backoffLimit"] == 0
    assert job["spec"]["ttlSecondsAfterFinished"] == 300
    assert policy.profile("python-compile").output_limit_kb == 256


def test_job_rejects_unlisted_profile_media_and_invalid_id():
    builder = SandboxJobBuilder(SandboxPolicy.from_file(ROOT / "config" / "sandbox-policy.json"))
    with pytest.raises(SandboxError, match="profile is not allowlisted"):
        builder.build(SandboxExecutionRequest("a" * 32, "shell", "text/x-python", b"x"))
    with pytest.raises(SandboxError, match="media type"):
        builder.build(SandboxExecutionRequest("a" * 32, "python-run", "text/javascript", b"x"))
    with pytest.raises(SandboxError, match="execution id"):
        builder.build(SandboxExecutionRequest("not-an-id", "python-run", "text/x-python", b"x"))


def test_namespace_is_restricted_and_network_is_default_deny():
    namespace = yaml.safe_load((ROOT / "infra" / "sandbox" / "namespace.yaml").read_text(encoding="utf-8"))
    network = yaml.safe_load((ROOT / "infra" / "sandbox" / "network-policy.yaml").read_text(encoding="utf-8"))
    resource_docs = list(
        yaml.safe_load_all((ROOT / "infra" / "sandbox" / "resource-policy.yaml").read_text(encoding="utf-8"))
    )

    assert namespace["metadata"]["labels"]["pod-security.kubernetes.io/enforce"] == "restricted"
    assert network["spec"] == {"podSelector": {}, "policyTypes": ["Ingress", "Egress"]}
    assert {value["kind"] for value in resource_docs} == {"ResourceQuota", "LimitRange"}


def test_sandbox_plugin_is_loaded_but_execution_defaults_disabled():
    registry = PluginRegistry(ROOT / "plugins")
    registry.load_all()
    plugin = registry.sandbox_plugin("kubernetes-sandbox")
    assert plugin.health().status == "healthy"
    with pytest.raises(PluginError, match="disabled"):
        plugin.execute(SandboxExecutionRequest("a" * 32, "python-run", "text/x-python", b"print(1)"))
    registry.stop_all()


def test_signed_greg_approval_executes_fake_sandbox_and_gates_result(tmp_path):
    class FakeSandbox:
        def __init__(self):
            self.requests = []

        def execute(self, request):
            self.requests.append(request)
            return SandboxExecutionResult(request.execution_id, request.profile, "completed", 0, "OK\n", "", 12)

    root = runtime_root(tmp_path)
    (root / "staging").mkdir()
    source = b"print('OK')\n"
    (root / "staging" / "source.py").write_bytes(source)
    server = HarnessProtocolServer(root)
    fake = FakeSandbox()
    server.sandbox = fake
    initialize(server)
    value = signed_task(root, "sandbox-approved")
    value["task"].update(
        {
            "agentId": "T1",
            "type": "test",
            "prompt": "Greg approved sandbox profile execution",
            "outputFile": "staging/sandbox/result.json",
            "attachments": [
                {
                    "path": "staging/source.py",
                    "mediaType": "text/x-python",
                    "sha256": hashlib.sha256(source).hexdigest(),
                }
            ],
        }
    )
    value["metadata"] = {
        "approvalBy": "greg",
        "artifactSha256": hashlib.sha256(source).hexdigest(),
        "executionId": "a" * 32,
        "sandboxProfile": "python-run",
    }
    value["signature"] = sign_for_test(KEY, TaskEnvelopeV2.from_mapping(value))

    response = server.handle({"id": "2", "method": "sandbox/execute", "params": value})

    assert response["result"]["status"] == "completed"
    assert response["result"]["gateOutcome"] == "accept"
    assert len(fake.requests) == 1
    assert (root / "staging" / "sandbox" / "result.json").is_file()


def test_sandbox_denies_non_greg_approval_before_execution(tmp_path):
    class NeverSandbox:
        def execute(self, request):
            raise AssertionError("sandbox must not execute")

    root = runtime_root(tmp_path)
    (root / "staging").mkdir()
    source = b"print('NO')\n"
    (root / "staging" / "source.py").write_bytes(source)
    server = HarnessProtocolServer(root)
    server.sandbox = NeverSandbox()
    initialize(server)
    value = signed_task(root, "sandbox-denied")
    value["task"].update(
        {
            "agentId": "T1",
            "type": "test",
            "outputFile": "staging/sandbox/result.json",
            "attachments": [
                {"path": "staging/source.py", "mediaType": "text/x-python", "sha256": None}
            ],
        }
    )
    value["metadata"] = {
        "approvalBy": "axel",
        "artifactSha256": hashlib.sha256(source).hexdigest(),
        "executionId": "b" * 32,
        "sandboxProfile": "python-run",
    }
    value["signature"] = sign_for_test(KEY, TaskEnvelopeV2.from_mapping(value))

    response = server.handle({"id": "2", "method": "sandbox/execute", "params": value})

    assert response["error"]["code"] == "SANDBOX_APPROVAL_DENIED"
