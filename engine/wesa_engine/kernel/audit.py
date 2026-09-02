from __future__ import annotations

import hashlib
import json
import os
import time
from contextlib import contextmanager
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Mapping


class AuditError(RuntimeError):
    pass


def resolve_audit_path(root: Path) -> Path:
    configured = os.getenv("WESA_AUDIT_PATH", "").strip()
    return Path(configured) if configured else root / "logs" / "AUDIT.jsonl"


def _canonical(record: Mapping[str, Any]) -> bytes:
    return json.dumps(record, sort_keys=True, separators=(",", ":"), ensure_ascii=False).encode()


@dataclass(frozen=True, slots=True)
class AuditVerification:
    ok: bool
    records: int
    head: str
    error: str | None = None


class AuditLog:
    def __init__(self, path: Path):
        self.path = path

    def _records(self) -> list[dict[str, Any]]:
        if not self.path.exists():
            return []
        records: list[dict[str, Any]] = []
        for number, line in enumerate(self.path.read_text(encoding="utf-8").splitlines(), 1):
            try:
                value = json.loads(line)
            except json.JSONDecodeError as exc:
                raise AuditError(f"invalid audit JSON at line {number}") from exc
            if not isinstance(value, dict):
                raise AuditError(f"audit line {number} is not an object")
            records.append(value)
        return records

    @contextmanager
    def _exclusive_lock(self):
        self.path.parent.mkdir(parents=True, exist_ok=True)
        lock_path = self.path.with_name(f"{self.path.name}.lock")
        deadline = time.monotonic() + 30
        descriptor: int | None = None
        while descriptor is None:
            try:
                descriptor = os.open(lock_path, os.O_CREAT | os.O_EXCL | os.O_WRONLY)
                os.write(descriptor, str(os.getpid()).encode("ascii"))
            except FileExistsError:
                try:
                    if time.time() - lock_path.stat().st_mtime > 60:
                        lock_path.unlink()
                        continue
                except FileNotFoundError:
                    continue
                if time.monotonic() >= deadline:
                    raise AuditError("timed out waiting for audit lock")
                time.sleep(0.01)
        try:
            yield
        finally:
            os.close(descriptor)
            lock_path.unlink(missing_ok=True)

    @staticmethod
    def _verify_records(records: list[dict[str, Any]]) -> AuditVerification:
        previous = ""
        for index, record in enumerate(records, 1):
            stored_hash = record.get("hash")
            unsigned = {key: value for key, value in record.items() if key != "hash"}
            expected = hashlib.sha256(_canonical(unsigned)).hexdigest()
            if record.get("seq") != index:
                return AuditVerification(False, len(records), previous, f"sequence mismatch at {index}")
            if record.get("previousHash", "") != previous:
                return AuditVerification(False, len(records), previous, f"chain mismatch at {index}")
            if stored_hash != expected:
                return AuditVerification(False, len(records), previous, f"hash mismatch at {index}")
            previous = str(stored_hash)
        return AuditVerification(True, len(records), previous)

    def verify(self) -> AuditVerification:
        with self._exclusive_lock():
            try:
                records = self._records()
            except AuditError as exc:
                return AuditVerification(False, 0, "", str(exc))
            return self._verify_records(records)

    def metrics(self) -> Mapping[str, Any]:
        with self._exclusive_lock():
            records = self._records()
            promotion_outcomes: dict[str, int] = {}
            sandbox_outcomes: dict[str, int] = {}
            for record in records:
                payload = record.get("payload") if isinstance(record.get("payload"), dict) else {}
                if record.get("event") == "fact_promotion_result":
                    outcome = str(payload.get("outcome", "unknown"))
                    promotion_outcomes[outcome] = promotion_outcomes.get(outcome, 0) + 1
                if record.get("event") == "sandbox_execution_completed":
                    outcome = str(payload.get("status", "unknown"))
                    sandbox_outcomes[outcome] = sandbox_outcomes.get(outcome, 0) + 1
            verification = self._verify_records(records)
        return {
            "auditChainOk": verification.ok,
            "auditRecords": verification.records,
            "auditHead": verification.head,
            "memoryPromotions": promotion_outcomes,
            "sandboxExecutions": sandbox_outcomes,
        }

    def append(self, actor: str, event: str, payload: Mapping[str, Any] | None = None) -> dict[str, Any]:
        with self._exclusive_lock():
            try:
                verification = self._verify_records(self._records())
            except AuditError as exc:
                raise AuditError(str(exc)) from exc
            if not verification.ok:
                raise AuditError(verification.error or "audit verification failed")
            record: dict[str, Any] = {
                "seq": verification.records + 1,
                "timestamp": datetime.now(timezone.utc).isoformat(),
                "actor": actor,
                "event": event,
                "payload": dict(payload or {}),
                "previousHash": verification.head,
            }
            record["hash"] = hashlib.sha256(_canonical(record)).hexdigest()
            with self.path.open("a", encoding="utf-8", newline="\n") as handle:
                handle.write(json.dumps(record, separators=(",", ":"), ensure_ascii=False) + "\n")
            return record
