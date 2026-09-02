from __future__ import annotations

from dataclasses import dataclass, field
from datetime import datetime, timezone
from enum import Enum
from pathlib import PurePosixPath
from typing import Any, Mapping, Protocol, Sequence, runtime_checkable


class ContractError(ValueError):
    pass


def _required_text(data: Mapping[str, Any], name: str) -> str:
    value = data.get(name)
    if not isinstance(value, str) or not value.strip():
        raise ContractError(f"{name} must be a non-empty string")
    # Signed fields must remain byte-for-byte identical to the control-plane payload.
    # Normalizing here changes canonical JSON and invalidates otherwise valid HMACs.
    return value


@dataclass(frozen=True, slots=True)
class TaskAttachment:
    path: str
    media_type: str
    sha256: str | None = None

    @classmethod
    def from_mapping(cls, value: Mapping[str, Any]) -> "TaskAttachment":
        sha256 = value.get("sha256")
        if sha256 is not None and (not isinstance(sha256, str) or len(sha256) != 64):
            raise ContractError("attachment sha256 must be a 64-character string or null")
        return cls(
            _required_text(value, "path"),
            _required_text(value, "mediaType"),
            sha256,
        )


@dataclass(frozen=True, slots=True)
class TaskEnvelopeV2:
    task_id: str
    agent_id: str
    workspace_id: str
    workspace_root: str
    provider_id: str
    model: str
    task_type: str
    prompt: str
    output_file: str | None
    attachments: tuple[TaskAttachment, ...]
    issuer: str
    issued_at: datetime
    issued_at_text: str
    nonce: str
    signature: str
    metadata: Mapping[str, Any] = field(default_factory=dict)

    @classmethod
    def from_mapping(cls, data: Mapping[str, Any]) -> "TaskEnvelopeV2":
        task = data.get("task")
        if not isinstance(task, Mapping):
            raise ContractError("task must be an object")
        issued_raw = _required_text(data, "issuedAt")
        try:
            issued_at = datetime.fromisoformat(issued_raw.replace("Z", "+00:00"))
        except ValueError as exc:
            raise ContractError("issuedAt must be ISO-8601") from exc
        if issued_at.tzinfo is None:
            raise ContractError("issuedAt must include a timezone")
        output_file = task.get("outputFile")
        if output_file is not None and not isinstance(output_file, str):
            raise ContractError("task.outputFile must be a string or null")
        metadata = data.get("metadata") or {}
        if not isinstance(metadata, Mapping):
            raise ContractError("metadata must be an object")
        attachment_values = task.get("attachments", [])
        if not isinstance(attachment_values, list) or len(attachment_values) > 8:
            raise ContractError("task.attachments must be an array with at most 8 items")
        attachments: list[TaskAttachment] = []
        for value in attachment_values:
            if not isinstance(value, Mapping):
                raise ContractError("task.attachments must contain objects")
            attachments.append(TaskAttachment.from_mapping(value))
        return cls(
            task_id=_required_text(task, "id"),
            agent_id=_required_text(task, "agentId"),
            workspace_id=_required_text(task, "workspaceId"),
            workspace_root=_required_text(task, "workspaceRoot"),
            provider_id=_required_text(task, "provider"),
            model=_required_text(task, "model"),
            task_type=_required_text(task, "type"),
            prompt=_required_text(task, "prompt"),
            output_file=output_file,
            attachments=tuple(attachments),
            issuer=_required_text(data, "issuer"),
            issued_at=issued_at.astimezone(timezone.utc),
            issued_at_text=issued_raw,
            nonce=_required_text(data, "nonce"),
            signature=_required_text(data, "signature"),
            metadata=dict(metadata),
        )

    def has_lexical_path_escape(self) -> bool:
        if not self.output_file:
            return False
        return ".." in PurePosixPath(self.output_file.replace("\\", "/")).parts

    def unsigned_mapping(self) -> dict[str, Any]:
        return {
            "issuedAt": self.issued_at_text,
            "issuer": self.issuer,
            "metadata": dict(self.metadata),
            "nonce": self.nonce,
            "task": {
                "agentId": self.agent_id,
                "attachments": [
                    {
                        "mediaType": value.media_type,
                        "path": value.path,
                        "sha256": value.sha256,
                    }
                    for value in self.attachments
                ],
                "id": self.task_id,
                "outputFile": self.output_file,
                "prompt": self.prompt,
                "provider": self.provider_id,
                "model": self.model,
                "type": self.task_type,
                "workspaceId": self.workspace_id,
                "workspaceRoot": self.workspace_root,
            },
        }


@dataclass(frozen=True, slots=True)
class Artifact:
    artifact_id: str
    task_id: str
    media_type: str
    content: bytes
    requested_path: str | None = None


class GateOutcome(str, Enum):
    ACCEPT = "accept"
    REVIEW = "review"
    PROPOSAL = "proposal"
    QUARANTINE = "quarantine"
    REJECT = "reject"


@dataclass(frozen=True, slots=True)
class GateDecision:
    outcome: GateOutcome
    reasons: tuple[str, ...] = ()
    redacted_content: bytes | None = None


@runtime_checkable
class Provider(Protocol):
    @property
    def name(self) -> str: ...

    def health(self) -> Mapping[str, Any]: ...

    def run(self, envelope: TaskEnvelopeV2) -> str: ...


@runtime_checkable
class Queue(Protocol):
    def submit(self, envelope: TaskEnvelopeV2) -> None: ...

    def claim(self, worker_id: str) -> TaskEnvelopeV2 | None: ...


@runtime_checkable
class ToolBroker(Protocol):
    def call(self, agent_id: str, tool: str, arguments: Mapping[str, Any]) -> Any: ...


@runtime_checkable
class Memory(Protocol):
    def retrieve(self, tenant_id: str, agent_id: str, query: str) -> Sequence[Mapping[str, Any]]: ...
