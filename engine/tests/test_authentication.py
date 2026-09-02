import base64
from datetime import datetime, timedelta, timezone

import pytest

from wesa_engine.kernel.auth import AuthenticationError, TaskAuthenticator, sign_for_test
from wesa_engine.kernel.contracts import TaskEnvelopeV2
from wesa_engine.kernel.replay import NonceReplayStore, ReplayDetected


KEY = b"s" * 32


def envelope_mapping(issued_at: datetime, nonce: str = "nonce") -> dict:
    value = {
        "task": {
            "id": "task",
            "agentId": "W1",
            "workspaceId": "workspace",
            "workspaceRoot": "C:/workspace",
            "provider": "ollama-provider",
            "model": "test-model:latest",
            "type": "write",
            "prompt": "report",
            "outputFile": "staging/report.md",
        },
        "issuer": "harness-api",
        "issuedAt": issued_at.astimezone(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
        "nonce": nonce,
        "signature": "placeholder",
        "metadata": {},
    }
    value["signature"] = sign_for_test(KEY, TaskEnvelopeV2.from_mapping(value))
    return value


def authenticator(tmp_path) -> TaskAuthenticator:
    return TaskAuthenticator.from_base64_keys(
        {"harness-api": base64.b64encode(KEY).decode("ascii")},
        NonceReplayStore(tmp_path / "security.db"),
    )


def test_valid_signature_claims_nonce(tmp_path):
    now = datetime.now(timezone.utc).replace(microsecond=0)
    authenticator(tmp_path).verify(TaskEnvelopeV2.from_mapping(envelope_mapping(now)), now)


def test_tampering_fails_signature(tmp_path):
    now = datetime.now(timezone.utc).replace(microsecond=0)
    value = envelope_mapping(now)
    value["task"]["type"] = "code"
    with pytest.raises(AuthenticationError, match="signature"):
        authenticator(tmp_path).verify(TaskEnvelopeV2.from_mapping(value), now)


def test_stale_task_is_rejected(tmp_path):
    now = datetime.now(timezone.utc).replace(microsecond=0)
    envelope = TaskEnvelopeV2.from_mapping(envelope_mapping(now - timedelta(minutes=11)))
    with pytest.raises(AuthenticationError) as error:
        authenticator(tmp_path).verify(envelope, now)
    assert error.value.code == "STALE_TASK"


def test_nonce_replay_persists_across_authenticator_instances(tmp_path):
    now = datetime.now(timezone.utc).replace(microsecond=0)
    envelope = TaskEnvelopeV2.from_mapping(envelope_mapping(now))
    authenticator(tmp_path).verify(envelope, now)
    with pytest.raises(ReplayDetected):
        authenticator(tmp_path).verify(envelope, now)
