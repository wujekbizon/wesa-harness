from datetime import timezone

import pytest

from wesa_engine.kernel.contracts import ContractError, TaskEnvelopeV2


def valid_envelope() -> dict:
    return {
        "task": {
            "id": "task-1",
            "agentId": "W1",
            "workspaceId": "workspace-1",
            "workspaceRoot": "C:/workspace",
            "provider": "ollama-provider",
            "model": "test-model:latest",
            "type": "write",
            "prompt": "Write a report",
            "outputFile": "reports/a.md",
        },
        "issuer": "harness-api",
        "issuedAt": "2026-08-28T00:00:00Z",
        "nonce": "nonce-1",
        "signature": "test-signature",
        "metadata": {},
    }


def test_task_envelope_parses_utc_and_fields():
    envelope = TaskEnvelopeV2.from_mapping(valid_envelope())
    assert envelope.task_id == "task-1"
    assert envelope.issued_at.tzinfo == timezone.utc
    assert not envelope.has_lexical_path_escape()


def test_task_envelope_preserves_whitespace_in_signed_prompt():
    data = valid_envelope()
    data["task"]["prompt"] = "  Write code\nwith exact formatting.  \n"

    envelope = TaskEnvelopeV2.from_mapping(data)

    assert envelope.prompt == data["task"]["prompt"]
    assert envelope.unsigned_mapping()["task"]["prompt"] == data["task"]["prompt"]


def test_task_envelope_rejects_missing_signature():
    data = valid_envelope()
    del data["signature"]
    with pytest.raises(ContractError):
        TaskEnvelopeV2.from_mapping(data)


def test_task_envelope_detects_lexical_parent_traversal():
    data = valid_envelope()
    data["task"]["outputFile"] = "../../escape"
    assert TaskEnvelopeV2.from_mapping(data).has_lexical_path_escape()
