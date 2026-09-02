from __future__ import annotations

import base64
import hashlib
import hmac
import json
from dataclasses import dataclass
from datetime import datetime, timedelta, timezone
from typing import Any, Mapping

from wesa_engine.kernel.auth import AuthenticationError
from wesa_engine.kernel.replay import NonceReplayStore


ALLOWED_HEADLESS_TASKS = frozenset({"write", "research", "analyze", "general", "review"})


@dataclass(frozen=True, slots=True)
class HeadlessCommand:
    task_type: str
    prompt: str
    output_file: str
    issued_at: datetime
    issued_at_text: str
    nonce: str
    signature: str

    @classmethod
    def from_mapping(cls, value: Mapping[str, Any]) -> "HeadlessCommand":
        required = ("taskType", "prompt", "outputFile", "issuedAt", "nonce", "signature")
        if any(not isinstance(value.get(key), str) or not value[key] for key in required):
            raise AuthenticationError("INVALID_HEADLESS_COMMAND", "headless command fields are required")
        issued_text = str(value["issuedAt"])
        try:
            issued_at = datetime.fromisoformat(issued_text.replace("Z", "+00:00"))
        except ValueError as exc:
            raise AuthenticationError("INVALID_HEADLESS_COMMAND", "issuedAt must be ISO-8601") from exc
        return cls(
            str(value["taskType"]),
            str(value["prompt"]),
            str(value["outputFile"]),
            issued_at.astimezone(timezone.utc),
            issued_text,
            str(value["nonce"]),
            str(value["signature"]),
        )

    def unsigned_mapping(self) -> Mapping[str, str]:
        return {
            "issuedAt": self.issued_at_text,
            "nonce": self.nonce,
            "outputFile": self.output_file,
            "prompt": self.prompt,
            "taskType": self.task_type,
        }


class HeadlessCommandAuthenticator:
    def __init__(
        self,
        key: bytes,
        replay_store: NonceReplayStore,
        max_age: timedelta = timedelta(minutes=5),
    ):
        if len(key) < 32:
            raise ValueError("headless command key must be at least 256 bits")
        self.key = key
        self.replay_store = replay_store
        self.max_age = max_age

    def verify(self, command: HeadlessCommand, now: datetime | None = None) -> None:
        current = (now or datetime.now(timezone.utc)).astimezone(timezone.utc)
        if command.task_type not in ALLOWED_HEADLESS_TASKS:
            raise AuthenticationError("HEADLESS_SCOPE_DENIED", "headless task type is denied")
        normalized = command.output_file.replace("\\", "/")
        if not normalized.startswith("staging/headless/") or ".." in normalized.split("/"):
            raise AuthenticationError("HEADLESS_SCOPE_DENIED", "headless output path is denied")
        if command.issued_at < current - self.max_age or command.issued_at > current + timedelta(minutes=1):
            raise AuthenticationError("STALE_HEADLESS_COMMAND", "headless timestamp is outside its window")
        expected = self.sign(command)
        if not hmac.compare_digest(expected, command.signature):
            raise AuthenticationError("INVALID_HEADLESS_SIGNATURE", "headless command signature is invalid")
        self.replay_store.claim("harness-headless-command", command.nonce, current + self.max_age)

    def sign(self, command: HeadlessCommand) -> str:
        payload = json.dumps(
            command.unsigned_mapping(),
            sort_keys=True,
            separators=(",", ":"),
            ensure_ascii=False,
        ).encode("utf-8")
        digest = hmac.new(self.key, payload, hashlib.sha256).digest()
        return base64.urlsafe_b64encode(digest).decode("ascii").rstrip("=")
