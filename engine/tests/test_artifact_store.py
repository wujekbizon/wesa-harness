import json

from wesa_engine.kernel.artifact_store import ArtifactReviewStore
from wesa_engine.kernel.audit import AuditLog
from wesa_engine.kernel.contracts import Artifact, GateDecision, GateOutcome
from test_gate import envelope


def test_quarantine_store_writes_redacted_artifact_and_report(tmp_path):
    store = ArtifactReviewStore(tmp_path, AuditLog(tmp_path / "logs" / "AUDIT.jsonl"))
    artifact = Artifact("artifact", "task", "text/plain", b"raw-secret", "staging/file.txt")
    decision = GateDecision(GateOutcome.QUARANTINE, ("secret",), b"[REDACTED]")

    destination = store.route(envelope(tmp_path, "staging/file.txt"), artifact, decision)

    assert destination is not None
    assert (destination / "artifact.bin").read_bytes() == b"[REDACTED]"
    report = json.loads((destination / "gate.json").read_text(encoding="utf-8"))
    assert report["outcome"] == "quarantine"
    assert AuditLog(tmp_path / "logs" / "AUDIT.jsonl").verify().ok


def test_protected_change_routes_to_proposals_bucket(tmp_path):
    store = ArtifactReviewStore(tmp_path, AuditLog(tmp_path / "logs" / "AUDIT.jsonl"))
    artifact = Artifact("artifact-2", "task", "text/x-diff", b"patch", "config/protected.json")
    decision = GateDecision(GateOutcome.PROPOSAL, ("review",), b"patch")

    destination = store.route(envelope(tmp_path, "staging/proposal.patch"), artifact, decision)

    assert destination is not None
    assert destination.parts[-3] == "proposals"
