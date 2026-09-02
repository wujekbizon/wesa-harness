from __future__ import annotations

import base64
import hashlib
import hmac
from dataclasses import dataclass
from datetime import datetime, timedelta, timezone
from typing import Mapping

from .canonical import canonical_json_bytes
from .contracts import TaskEnvelopeV2
from .replay import NonceReplayStore


class AuthenticationError(ValueError):
    def __init__(self, code: str, message: str):
        super().__init__(message)
        self.code = code


def _decode_key(value: str) -> bytes:
    try:
        key = base64.b64decode(value, validate=True)
    except (ValueError, base64.binascii.Error) as exc:
        raise AuthenticationError("INVALID_ISSUER_KEY", "issuer key is not valid base64") from exc
    if len(key) < 32:
        raise AuthenticationError("INVALID_ISSUER_KEY", "issuer key must be at least 256 bits")
    return key


def _signature(key: bytes, envelope: TaskEnvelopeV2) -> str:
    digest = hmac.new(key, canonical_json_bytes(envelope.unsigned_mapping()), hashlib.sha256).digest()
    return base64.urlsafe_b64encode(digest).decode("ascii").rstrip("=")


@dataclass(slots=True)
class TaskAuthenticator:
    keys: Mapping[str, bytes]
    replay_store: NonceReplayStore
    max_age: timedelta = timedelta(minutes=10)
    future_skew: timedelta = timedelta(minutes=1)

    @classmethod
    def from_base64_keys(
        cls,
        values: Mapping[str, str],
        replay_store: NonceReplayStore,
    ) -> "TaskAuthenticator":
        return cls({issuer: _decode_key(value) for issuer, value in values.items()}, replay_store)

    def verify(self, envelope: TaskEnvelopeV2, now: datetime | None = None) -> None:
        key = self.keys.get(envelope.issuer)
        if key is None:
            raise AuthenticationError("UNKNOWN_ISSUER", "issuer has no verification key")
        expected = _signature(key, envelope)
        if not hmac.compare_digest(expected, envelope.signature):
            raise AuthenticationError("INVALID_SIGNATURE", "task signature is invalid")

        current = (now or datetime.now(timezone.utc)).astimezone(timezone.utc)
        if envelope.issued_at > current + self.future_skew:
            raise AuthenticationError("FUTURE_TASK", "task timestamp is too far in the future")
        if envelope.issued_at < current - self.max_age:
            raise AuthenticationError("STALE_TASK", "task timestamp is outside the acceptance window")
        self.replay_store.claim(envelope.issuer, envelope.nonce, current + self.max_age)


def sign_for_test(key: bytes, envelope: TaskEnvelopeV2) -> str:
    """Test/helper surface. Production signing belongs to the .NET control plane."""
    return _signature(key, envelope)

