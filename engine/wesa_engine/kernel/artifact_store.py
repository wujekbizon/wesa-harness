from __future__ import annotations

import json
import os
import tempfile
from pathlib import Path

from .audit import AuditLog
from .contracts import Artifact, GateDecision, GateOutcome, TaskEnvelopeV2
from .capabilities import AgentCapabilityManifest
from .gate import resolve_task_output


class ArtifactReviewStore:
    def __init__(self, root: Path, audit: AuditLog):
        self.root = root
        self.audit = audit

    def route(self, envelope: TaskEnvelopeV2, artifact: Artifact, decision: GateDecision) -> Path | None:
        if decision.outcome not in {GateOutcome.REVIEW, GateOutcome.PROPOSAL, GateOutcome.QUARANTINE}:
            return None
        bucket = {
            GateOutcome.REVIEW: "review",
            GateOutcome.PROPOSAL: "proposals",
            GateOutcome.QUARANTINE: "quarantine",
        }[decision.outcome]
        destination = self.root / bucket / envelope.task_id / artifact.artifact_id
        destination.mkdir(parents=True, exist_ok=True)
        content = decision.redacted_content if decision.redacted_content is not None else artifact.content
        self._atomic_write(destination / "artifact.bin", content)
        report = {
            "artifactId": artifact.artifact_id,
            "taskId": envelope.task_id,
            "outcome": decision.outcome.value,
            "reasons": list(decision.reasons),
            "requestedPath": artifact.requested_path,
            "mediaType": artifact.media_type,
        }
        self._atomic_write(
            destination / "gate.json",
            json.dumps(report, indent=2, sort_keys=True).encode("utf-8"),
        )
        self.audit.append(
            "kernel:artifact_gate",
            f"artifact_{decision.outcome.value}",
            {"taskId": envelope.task_id, "artifactId": artifact.artifact_id, "path": str(destination)},
        )
        return destination

    @staticmethod
    def _atomic_write(path: Path, content: bytes) -> None:
        with tempfile.NamedTemporaryFile(dir=path.parent, delete=False) as handle:
            handle.write(content)
            temporary = Path(handle.name)
        os.replace(temporary, path)


class AcceptedArtifactWriter:
    def __init__(self, audit: AuditLog):
        self.audit = audit

    def write(
        self,
        envelope: TaskEnvelopeV2,
        artifact: Artifact,
        decision: GateDecision,
        manifest: AgentCapabilityManifest,
    ) -> Path:
        if decision.outcome is not GateOutcome.ACCEPT:
            raise ValueError("only accepted artifacts can be written")
        resolved = resolve_task_output(envelope, manifest)
        if resolved is None:
            raise ValueError("accepted artifact requires an output path")
        target, relative = resolved
        target.parent.mkdir(parents=True, exist_ok=True)
        content = decision.redacted_content if decision.redacted_content is not None else artifact.content
        ArtifactReviewStore._atomic_write(target, content)
        self.audit.append(
            "kernel:artifact_gate",
            "artifact_written",
            {
                "taskId": envelope.task_id,
                "artifactId": artifact.artifact_id,
                "relativePath": relative,
            },
        )
        return target
