from __future__ import annotations

import base64
import json
import re
import subprocess
import time
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Mapping


_NETWORK_POLICY_BARRIER = """\
import socket
import sys
import time

time.sleep(10)
targets = (("10.43.0.1", 443), ("1.1.1.1", 443))
for target in targets:
    try:
        connection = socket.create_connection(target, timeout=2)
        connection.close()
        print("NETWORK_POLICY_NOT_READY")
        sys.exit(70)
    except OSError:
        pass
print("NETWORK_POLICY_READY")
"""


class SandboxError(RuntimeError):
    pass


@dataclass(frozen=True, slots=True)
class SandboxProfile:
    name: str
    image: str
    command: tuple[str, ...]
    media_types: tuple[str, ...]
    timeout_seconds: int
    cpu_limit: str
    memory_limit: str
    output_limit_kb: int


@dataclass(frozen=True, slots=True)
class SandboxPolicy:
    namespace: str
    profiles: Mapping[str, SandboxProfile]

    @classmethod
    def from_file(cls, path: Path) -> "SandboxPolicy":
        value = json.loads(path.read_text(encoding="utf-8"))
        namespace = value.get("namespace")
        raw_profiles = value.get("profiles")
        if not isinstance(namespace, str) or not re.fullmatch(r"[a-z0-9-]+", namespace):
            raise SandboxError("sandbox namespace is invalid")
        if not isinstance(raw_profiles, Mapping) or not raw_profiles:
            raise SandboxError("sandbox profiles are required")
        profiles: dict[str, SandboxProfile] = {}
        for name, raw in raw_profiles.items():
            if not isinstance(name, str) or not isinstance(raw, Mapping):
                raise SandboxError("sandbox profile is invalid")
            image = raw.get("image")
            command = raw.get("command")
            media_types = raw.get("mediaTypes")
            if not isinstance(image, str) or "@sha256:" not in image:
                raise SandboxError("sandbox image must use an immutable sha256 digest")
            if not isinstance(command, list) or not command or any(not isinstance(item, str) for item in command):
                raise SandboxError("sandbox command must be a fixed string array")
            if not isinstance(media_types, list) or any(not isinstance(item, str) for item in media_types):
                raise SandboxError("sandbox mediaTypes must be strings")
            profiles[name] = SandboxProfile(
                name,
                image,
                tuple(command),
                tuple(media_types),
                int(raw.get("timeoutSeconds", 30)),
                str(raw.get("cpuLimit", "250m")),
                str(raw.get("memoryLimit", "128Mi")),
                int(raw.get("outputLimitKb", 256)),
            )
        return cls(namespace, profiles)

    def profile(self, name: str) -> SandboxProfile:
        profile = self.profiles.get(name)
        if profile is None:
            raise SandboxError(f"sandbox profile is not allowlisted: {name}")
        return profile


@dataclass(frozen=True, slots=True)
class SandboxExecutionRequest:
    execution_id: str
    profile: str
    media_type: str
    content: bytes


@dataclass(frozen=True, slots=True)
class SandboxExecutionResult:
    execution_id: str
    profile: str
    status: str
    exit_code: int | None
    stdout: str
    stderr: str
    duration_ms: int


class SandboxJobBuilder:
    def __init__(self, policy: SandboxPolicy):
        self.policy = policy

    def build(self, request: SandboxExecutionRequest) -> Mapping[str, Any]:
        if not re.fullmatch(r"[a-f0-9]{32}", request.execution_id):
            raise SandboxError("execution id must be 32 lowercase hexadecimal characters")
        profile = self.policy.profile(request.profile)
        if request.media_type not in profile.media_types:
            raise SandboxError("artifact media type is not allowed by sandbox profile")
        if len(request.content) > 512 * 1024:
            raise SandboxError("sandbox source exceeds 512 KiB")
        name = f"wesa-sbx-{request.execution_id[:20]}"
        source_key = "artifact.py"
        config_map = {
            "apiVersion": "v1",
            "kind": "ConfigMap",
            "metadata": {
                "name": name,
                "namespace": self.policy.namespace,
                "labels": {"app.kubernetes.io/name": "wesa-sandbox", "wesa.run": request.execution_id},
            },
            "binaryData": {source_key: base64.b64encode(request.content).decode("ascii")},
        }
        job = {
            "apiVersion": "batch/v1",
            "kind": "Job",
            "metadata": {
                "name": name,
                "namespace": self.policy.namespace,
                "labels": {"app.kubernetes.io/name": "wesa-sandbox", "wesa.run": request.execution_id},
            },
            "spec": {
                "backoffLimit": 0,
                "activeDeadlineSeconds": profile.timeout_seconds + 30,
                "ttlSecondsAfterFinished": 300,
                "template": {
                    "metadata": {
                        "labels": {"app.kubernetes.io/name": "wesa-sandbox", "wesa.run": request.execution_id}
                    },
                    "spec": {
                        "restartPolicy": "Never",
                        "automountServiceAccountToken": False,
                        "enableServiceLinks": False,
                        "securityContext": {
                            "runAsNonRoot": True,
                            "runAsUser": 65532,
                            "runAsGroup": 65532,
                            "fsGroup": 65532,
                            "seccompProfile": {"type": "RuntimeDefault"},
                        },
                        "initContainers": [
                            {
                                "name": "network-policy-barrier",
                                "image": profile.image,
                                "imagePullPolicy": "IfNotPresent",
                                "command": ["python", "-I", "-c", _NETWORK_POLICY_BARRIER],
                                "securityContext": {
                                    "allowPrivilegeEscalation": False,
                                    "readOnlyRootFilesystem": True,
                                    "capabilities": {"drop": ["ALL"]},
                                },
                                "resources": {
                                    "requests": {"cpu": "10m", "memory": "16Mi"},
                                    "limits": {"cpu": "50m", "memory": "32Mi"},
                                },
                            }
                        ],
                        "containers": [
                            {
                                "name": "runner",
                                "image": profile.image,
                                "imagePullPolicy": "IfNotPresent",
                                "command": list(profile.command),
                                "workingDir": "/workspace/work",
                                "env": [
                                    {"name": "HOME", "value": "/workspace/work"},
                                    {"name": "PYTHONPYCACHEPREFIX", "value": "/workspace/work/pycache"},
                                ],
                                "securityContext": {
                                    "allowPrivilegeEscalation": False,
                                    "readOnlyRootFilesystem": True,
                                    "capabilities": {"drop": ["ALL"]},
                                },
                                "resources": {
                                    "requests": {"cpu": "50m", "memory": "32Mi"},
                                    "limits": {"cpu": profile.cpu_limit, "memory": profile.memory_limit},
                                },
                                "volumeMounts": [
                                    {"name": "source", "mountPath": "/workspace/source", "readOnly": True},
                                    {"name": "work", "mountPath": "/workspace/work"},
                                    {"name": "tmp", "mountPath": "/tmp"},
                                ],
                            }
                        ],
                        "volumes": [
                            {"name": "source", "configMap": {"name": name, "defaultMode": 0o440}},
                            {"name": "work", "emptyDir": {"sizeLimit": "16Mi"}},
                            {"name": "tmp", "emptyDir": {"sizeLimit": "8Mi"}},
                        ],
                    },
                },
            },
        }
        return {"apiVersion": "v1", "kind": "List", "items": [config_map, job]}


class KubernetesSandboxClient:
    def __init__(self, policy: SandboxPolicy, kubeconfig: str, kubectl: str = "kubectl"):
        if not kubeconfig:
            raise SandboxError("sandbox kubeconfig is not configured")
        self.policy = policy
        self.kubeconfig = kubeconfig
        self.kubectl = kubectl
        self.builder = SandboxJobBuilder(policy)

    def execute(self, request: SandboxExecutionRequest) -> SandboxExecutionResult:
        profile = self.policy.profile(request.profile)
        resources = self.builder.build(request)
        name = f"wesa-sbx-{request.execution_id[:20]}"
        started = time.monotonic()
        apply = self._run(
            ["apply", "-f", "-"],
            input_text=json.dumps(resources, separators=(",", ":")),
            timeout=30,
        )
        if apply.returncode != 0:
            raise SandboxError(f"kubectl apply failed: {apply.stderr[-1000:]}")
        stdout = ""
        stderr = ""
        exit_code: int | None = None
        status = "failed"
        try:
            completed = self._wait_for_job_terminal(name, profile.timeout_seconds + 35)
            logs = self._run(
                ["logs", f"job/{name}", f"--limit-bytes={profile.output_limit_kb * 1024 + 1}"],
                timeout=15,
            )
            stdout = logs.stdout
            stderr = logs.stderr
            if len(stdout.encode("utf-8")) + len(stderr.encode("utf-8")) > profile.output_limit_kb * 1024:
                raise SandboxError("sandbox output exceeds configured limit")
            exit_result = self._run(
                [
                    "get",
                    "pods",
                    "-l",
                    f"wesa.run={request.execution_id}",
                    "-o",
                    "jsonpath={.items[0].status.containerStatuses[0].state.terminated.exitCode}",
                ],
                timeout=15,
            )
            exit_code = int(exit_result.stdout.strip()) if exit_result.stdout.strip().isdigit() else None
            status = "completed" if completed and exit_code == 0 else "failed"
        finally:
            self._run(["delete", "job", name, "--ignore-not-found=true"], timeout=15)
            self._run(["delete", "configmap", name, "--ignore-not-found=true"], timeout=15)
        return SandboxExecutionResult(
            request.execution_id,
            request.profile,
            status,
            exit_code,
            stdout,
            stderr,
            int((time.monotonic() - started) * 1000),
        )

    def _wait_for_job_terminal(self, name: str, timeout_seconds: int) -> bool:
        deadline = time.monotonic() + timeout_seconds
        while time.monotonic() < deadline:
            result = self._run(["get", "job", name, "-o", "json"], timeout=10)
            if result.returncode == 0:
                value = json.loads(result.stdout)
                status = value.get("status", {})
                conditions = {
                    condition.get("type"): condition.get("status")
                    for condition in status.get("conditions", [])
                    if isinstance(condition, Mapping)
                }
                if status.get("succeeded", 0) > 0 or conditions.get("Complete") == "True":
                    return True
                if status.get("failed", 0) > 0 or conditions.get("Failed") == "True":
                    return False
            time.sleep(0.5)
        raise SandboxError("sandbox job did not reach a terminal state before deadline")

    def _run(
        self,
        arguments: list[str],
        *,
        input_text: str | None = None,
        timeout: int,
    ) -> subprocess.CompletedProcess[str]:
        return subprocess.run(
            [
                self.kubectl,
                "--kubeconfig",
                self.kubeconfig,
                "--namespace",
                self.policy.namespace,
                *arguments,
            ],
            input=input_text,
            text=True,
            capture_output=True,
            timeout=timeout,
            check=False,
            shell=False,
        )
