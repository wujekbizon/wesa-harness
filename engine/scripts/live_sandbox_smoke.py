from __future__ import annotations

import argparse
import uuid
from pathlib import Path

from wesa_engine.sandbox import KubernetesSandboxClient, SandboxExecutionRequest, SandboxPolicy
from wesa_engine.sandbox.kubernetes import SandboxError


SANDBOX_POLICY_PATH = Path(__file__).resolve().parents[2] / "config" / "sandbox-policy.json"


def execute(
    client: KubernetesSandboxClient,
    profile: str,
    source: bytes,
):
    return client.execute(
        SandboxExecutionRequest(
            execution_id=uuid.uuid4().hex,
            profile=profile,
            media_type="text/x-python",
            content=source,
        )
    )


def main() -> int:
    parser = argparse.ArgumentParser(description="Run live WESA Kubernetes sandbox checks")
    parser.add_argument("--kubeconfig", required=True)
    arguments = parser.parse_args()

    policy = SandboxPolicy.from_file(SANDBOX_POLICY_PATH)
    client = KubernetesSandboxClient(policy, arguments.kubeconfig)

    compile_result = execute(client, "python-compile", b"print('COMPILE_OK')\n")
    assert compile_result.status == "completed" and compile_result.exit_code == 0, compile_result
    print("compile=PASS")

    network_source = b"""\
import socket
try:
    socket.create_connection(("1.1.1.1", 443), timeout=3).close()
    print("EGRESS_ALLOWED")
    raise SystemExit(42)
except OSError:
    print("EGRESS_BLOCKED")
"""
    network_result = execute(client, "python-run", network_source)
    assert network_result.status == "completed" and "EGRESS_BLOCKED" in network_result.stdout, network_result
    print("egress=PASS")

    timeout_result = execute(client, "python-run", b"while True:\n    pass\n")
    assert timeout_result.status == "failed" and timeout_result.exit_code not in (None, 0), timeout_result
    print("timeout=PASS")

    try:
        execute(client, "python-run", b"print('x' * (300 * 1024))\n")
    except SandboxError as exc:
        assert "output exceeds configured limit" in str(exc), exc
    else:
        raise AssertionError("oversized sandbox output was accepted")
    print("output-limit=PASS")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
