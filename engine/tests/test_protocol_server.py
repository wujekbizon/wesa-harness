import base64
import json
import shutil
from datetime import datetime, timezone
from pathlib import Path

from wesa_engine.kernel.auth import sign_for_test
from wesa_engine.kernel.contracts import TaskEnvelopeV2
from wesa_engine.runtime.server import HarnessProtocolServer


SOURCE_ROOT = Path(__file__).resolve().parents[2]
KEY = b"k" * 32


def runtime_root(tmp_path: Path) -> Path:
    shutil.copytree(SOURCE_ROOT / "config", tmp_path / "config")
    shutil.copytree(SOURCE_ROOT / "plugins", tmp_path / "plugins")
    return tmp_path


def initialize(server: HarnessProtocolServer) -> dict:
    return server.handle(
        {
            "id": "1",
            "method": "initialize",
            "params": {
                "protocolVersion": "1.0",
                "client": {"name": "test"},
                "verificationKeys": {"harness-api": base64.b64encode(KEY).decode("ascii")},
            },
        }
    )


def signed_task(workspace_root: Path, nonce: str = "nonce-1") -> dict:
    value = {
        "task": {
            "id": "task-1",
            "agentId": "W1",
            "workspaceId": "workspace-1",
            "workspaceRoot": str(workspace_root),
            "provider": "ollama-provider",
            "model": "test-model:latest",
            "type": "write",
            "prompt": "Report",
            "outputFile": "staging/a.md",
        },
        "issuer": "harness-api",
        "issuedAt": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
        "nonce": nonce,
        "signature": "placeholder",
        "metadata": {},
    }
    value["signature"] = sign_for_test(KEY, TaskEnvelopeV2.from_mapping(value))
    return value


def test_server_requires_initialize(tmp_path):
    response = HarnessProtocolServer(runtime_root(tmp_path)).handle(
        {"id": "1", "method": "health/read", "params": {}}
    )
    assert response["error"]["code"] == "NOT_INITIALIZED"


def test_server_initializes_with_security_checks(tmp_path):
    response = initialize(HarnessProtocolServer(runtime_root(tmp_path)))
    assert response["result"]["executionEnabled"] is True
    assert response["result"]["protocolVersion"] == "1.0"


def test_task_validation_is_signed_non_executing_shadow_mode(tmp_path):
    root = runtime_root(tmp_path)
    server = HarnessProtocolServer(root)
    initialize(server)
    response = server.handle({"id": "2", "method": "task/validate", "params": signed_task(root)})
    assert response["result"] == {
        "taskId": "task-1",
        "agentId": "W1",
        "status": "shadow-validated",
        "gateOutcome": "accept",
        "executionEnabled": False,
    }


def test_task_replay_is_rejected(tmp_path):
    root = runtime_root(tmp_path)
    server = HarnessProtocolServer(root)
    initialize(server)
    task = signed_task(root)
    assert "result" in server.handle({"id": "2", "method": "task/validate", "params": task})
    replay = server.handle({"id": "3", "method": "task/validate", "params": task})
    assert replay["error"]["code"] == "REPLAY_DETECTED"


def test_tampered_task_is_rejected(tmp_path):
    root = runtime_root(tmp_path)
    server = HarnessProtocolServer(root)
    initialize(server)
    task = signed_task(root)
    task["task"]["prompt"] = "tampered"
    response = server.handle({"id": "2", "method": "task/validate", "params": task})
    assert response["error"]["code"] == "INVALID_SIGNATURE"


def test_unsigned_task_is_rejected(tmp_path):
    root = runtime_root(tmp_path)
    server = HarnessProtocolServer(root)
    initialize(server)
    task = signed_task(root)
    del task["signature"]
    response = server.handle({"id": "2", "method": "task/validate", "params": task})
    assert response["error"]["code"] == "INVALID_PARAMS"


def test_headless_issuer_has_reduced_task_and_path_scope(tmp_path):
    root = runtime_root(tmp_path)
    server = HarnessProtocolServer(root)
    server.handle(
        {
            "id": "1",
            "method": "initialize",
            "params": {
                "protocolVersion": "1.0",
                "client": {"name": "test"},
                "verificationKeys": {
                    "harness-api": base64.b64encode(KEY).decode("ascii"),
                    "harness-headless": base64.b64encode(KEY).decode("ascii"),
                },
            },
        }
    )

    denied_type = signed_task(root, "headless-type")
    denied_type["issuer"] = "harness-headless"
    denied_type["task"]["type"] = "code"
    denied_type["task"]["agentId"] = "D1"
    denied_type["task"]["outputFile"] = "staging/headless/a.py"
    denied_type["signature"] = sign_for_test(KEY, TaskEnvelopeV2.from_mapping(denied_type))
    type_response = server.handle({"id": "2", "method": "task/validate", "params": denied_type})

    denied_path = signed_task(root, "headless-path")
    denied_path["issuer"] = "harness-headless"
    denied_path["task"]["outputFile"] = "staging/a.md"
    denied_path["signature"] = sign_for_test(KEY, TaskEnvelopeV2.from_mapping(denied_path))
    path_response = server.handle({"id": "3", "method": "task/validate", "params": denied_path})

    allowed = signed_task(root, "headless-allowed")
    allowed["issuer"] = "harness-headless"
    allowed["task"]["outputFile"] = "staging/headless/a.md"
    allowed["signature"] = sign_for_test(KEY, TaskEnvelopeV2.from_mapping(allowed))
    allowed_response = server.handle({"id": "4", "method": "task/validate", "params": allowed})

    assert type_response["error"]["code"] == "ISSUER_SCOPE_DENIED"
    assert path_response["error"]["code"] == "ISSUER_SCOPE_DENIED"
    assert allowed_response["result"]["status"] == "shadow-validated"


def test_agent_provider_allowlist_accepts_vertex_and_denies_unknown(tmp_path):
    root = runtime_root(tmp_path)
    server = HarnessProtocolServer(root)
    initialize(server)

    vertex = signed_task(root, "vertex-allowed")
    vertex["task"]["provider"] = "vertex-gemini"
    vertex["task"]["model"] = "gemini-2.5-flash"
    vertex["signature"] = sign_for_test(KEY, TaskEnvelopeV2.from_mapping(vertex))
    allowed = server.handle({"id": "2", "method": "task/validate", "params": vertex})

    unknown = signed_task(root, "unknown-provider")
    unknown["task"]["provider"] = "unknown-provider"
    unknown["signature"] = sign_for_test(KEY, TaskEnvelopeV2.from_mapping(unknown))
    denied = server.handle({"id": "3", "method": "task/validate", "params": unknown})

    assert allowed["result"]["status"] == "shadow-validated"
    assert denied["error"]["code"] == "PROVIDER_DENIED"


def test_plugin_state_change_is_persisted_and_audited(tmp_path):
    root = runtime_root(tmp_path)
    server = HarnessProtocolServer(root)
    initialize(server)

    response = server.handle(
        {
            "id": "2",
            "method": "plugin/set-enabled",
            "params": {"pluginId": "vertex-gemini", "enabled": False},
        }
    )

    assert response["result"]["status"] == "disabled"
    assert json.loads((root / "state" / "plugins.json").read_text())["vertex-gemini"] is False
    assert any(
        json.loads(line)["event"] == "plugin_state_changed"
        for line in (root / "logs" / "AUDIT.jsonl").read_text().splitlines()
    )
