from pathlib import Path

from wesa_engine.kernel.capabilities import AgentCapabilityManifest
from wesa_engine.kernel.contracts import Artifact, GateOutcome, TaskEnvelopeV2
from wesa_engine.kernel.gate import ArtifactGate
from wesa_engine.kernel.protected import ProtectedRegistry


def manifest() -> AgentCapabilityManifest:
    return AgentCapabilityManifest.from_mapping(
        {
            "agentId": "D1",
            "taskTypes": ["code"],
            "readPaths": ["workspace/**"],
            "writePaths": ["staging/**"],
            "denyPaths": ["config/**"],
            "tools": [],
            "network": "none",
            "execution": "none",
            "maxArtifactKb": 16,
        }
    )


def envelope(root: Path, output: str = "staging/file.py") -> TaskEnvelopeV2:
    return TaskEnvelopeV2.from_mapping(
        {
            "task": {
                "id": "task",
                "agentId": "D1",
                "workspaceId": "workspace",
                "workspaceRoot": str(root),
                "provider": "ollama-provider",
                "model": "test-model:latest",
                "type": "code",
                "prompt": "code",
                "outputFile": output,
            },
            "issuer": "harness-api",
            "issuedAt": "2026-08-28T00:00:00Z",
            "nonce": "nonce",
            "signature": "signature",
            "metadata": {},
        }
    )


def gate() -> ArtifactGate:
    return ArtifactGate(ProtectedRegistry(("config/**",), ("config/**",)))


def test_valid_python_artifact_is_accepted(tmp_path):
    artifact = Artifact("artifact", "task", "text/x-python", b"def add(a, b):\n    return a + b\n", "staging/file.py")
    assert gate().evaluate(envelope(tmp_path), artifact, manifest()).outcome is GateOutcome.ACCEPT


def test_empty_artifact_is_quarantined(tmp_path):
    artifact = Artifact("artifact", "task", "text/x-python", b"", "staging/file.py")
    decision = gate().evaluate(envelope(tmp_path), artifact, manifest())
    assert decision.outcome is GateOutcome.QUARANTINE
    assert decision.reasons == ("artifact is empty",)


def test_path_traversal_is_quarantined(tmp_path):
    artifact = Artifact("artifact", "task", "text/x-python", b"print('safe')\n", "../../escape.py")
    decision = gate().evaluate(envelope(tmp_path, "../../escape.py"), artifact, manifest())
    assert decision.outcome is GateOutcome.QUARANTINE


def test_dynamic_execution_is_quarantined(tmp_path):
    artifact = Artifact("artifact", "task", "text/x-python", b"eval(user_input)\n", "staging/file.py")
    decision = gate().evaluate(envelope(tmp_path), artifact, manifest())
    assert decision.outcome is GateOutcome.QUARANTINE
    assert any("dynamic execution" in reason for reason in decision.reasons)


def test_secret_is_redacted_and_routed_to_review(tmp_path):
    artifact = Artifact(
        "artifact",
        "task",
        "text/x-python",
        b'password="super-secret-value"\n',
        "staging/file.py",
    )
    decision = gate().evaluate(envelope(tmp_path), artifact, manifest())
    assert decision.outcome is GateOutcome.REVIEW
    assert b"super-secret-value" not in (decision.redacted_content or b"")
    assert b"[REDACTED:CREDENTIAL]" in (decision.redacted_content or b"")


def test_invalid_json_is_quarantined(tmp_path):
    artifact = Artifact("artifact", "task", "application/json", b"{broken", "staging/file.json")
    decision = gate().evaluate(envelope(tmp_path, "staging/file.json"), artifact, manifest())
    assert decision.outcome is GateOutcome.QUARANTINE


def test_unresolved_import_is_flagged_for_review(tmp_path):
    artifact = Artifact(
        "artifact",
        "task",
        "text/x-python",
        b"import module_that_does_not_exist_anywhere\n",
        "staging/file.py",
    )
    decision = gate().evaluate(envelope(tmp_path), artifact, manifest())
    assert decision.outcome is GateOutcome.REVIEW
    assert any("unresolved import" in reason for reason in decision.reasons)


def test_protected_target_becomes_patch_proposal(tmp_path):
    artifact = Artifact(
        "artifact",
        "task",
        "text/x-diff",
        b"--- a/config/manifests/w1.manifest.json\n+++ b/config/manifests/w1.manifest.json\n",
        "config/manifests/w1.manifest.json",
    )
    decision = gate().evaluate(envelope(tmp_path, "staging/proposal.patch"), artifact, manifest())
    assert decision.outcome is GateOutcome.PROPOSAL
