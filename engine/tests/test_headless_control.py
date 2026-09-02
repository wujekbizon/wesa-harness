from datetime import datetime, timedelta, timezone

import pytest

from wesa_engine.control import HeadlessCommand, HeadlessCommandAuthenticator
from wesa_engine.kernel.auth import AuthenticationError
from wesa_engine.kernel.replay import NonceReplayStore, ReplayDetected


KEY = b"h" * 32


def command(now: datetime, nonce: str = "nonce", task_type: str = "write", output="staging/headless/a.md"):
    value = {
        "taskType": task_type,
        "prompt": "write report",
        "outputFile": output,
        "issuedAt": now.strftime("%Y-%m-%dT%H:%M:%SZ"),
        "nonce": nonce,
        "signature": "placeholder",
    }
    return value


def authenticator(tmp_path):
    return HeadlessCommandAuthenticator(KEY, NonceReplayStore(tmp_path / "headless.db"))


def test_signed_headless_command_is_accepted_once(tmp_path):
    now = datetime.now(timezone.utc).replace(microsecond=0)
    value = command(now)
    parsed = HeadlessCommand.from_mapping(value)
    value["signature"] = authenticator(tmp_path).sign(parsed)
    parsed = HeadlessCommand.from_mapping(value)
    verifier = authenticator(tmp_path)

    verifier.verify(parsed, now)
    with pytest.raises(ReplayDetected):
        verifier.verify(parsed, now)


def test_unsigned_stale_and_overprivileged_commands_are_denied(tmp_path):
    now = datetime.now(timezone.utc).replace(microsecond=0)
    verifier = authenticator(tmp_path)
    unsigned = HeadlessCommand.from_mapping(command(now, "unsigned"))
    with pytest.raises(AuthenticationError) as invalid:
        verifier.verify(unsigned, now)
    assert invalid.value.code == "INVALID_HEADLESS_SIGNATURE"

    stale_value = command(now - timedelta(minutes=6), "stale")
    stale = HeadlessCommand.from_mapping(stale_value)
    stale_value["signature"] = verifier.sign(stale)
    with pytest.raises(AuthenticationError) as stale_error:
        verifier.verify(HeadlessCommand.from_mapping(stale_value), now)
    assert stale_error.value.code == "STALE_HEADLESS_COMMAND"

    denied_value = command(now, "denied", task_type="code", output="staging/headless/a.py")
    denied = HeadlessCommand.from_mapping(denied_value)
    denied_value["signature"] = verifier.sign(denied)
    with pytest.raises(AuthenticationError) as denied_error:
        verifier.verify(HeadlessCommand.from_mapping(denied_value), now)
    assert denied_error.value.code == "HEADLESS_SCOPE_DENIED"
