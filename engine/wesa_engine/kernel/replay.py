from __future__ import annotations

import sqlite3
from datetime import datetime, timezone
from pathlib import Path


class ReplayDetected(ValueError):
    pass


class NonceReplayStore:
    def __init__(self, path: Path):
        self.path = path
        self.path.parent.mkdir(parents=True, exist_ok=True)
        with self._connect() as connection:
            connection.execute(
                """
                CREATE TABLE IF NOT EXISTS used_nonces (
                    issuer TEXT NOT NULL,
                    nonce TEXT NOT NULL,
                    expires_at TEXT NOT NULL,
                    PRIMARY KEY (issuer, nonce)
                )
                """
            )

    def _connect(self) -> sqlite3.Connection:
        connection = sqlite3.connect(self.path, timeout=5, isolation_level=None)
        connection.execute("PRAGMA busy_timeout=5000")
        return connection

    def claim(self, issuer: str, nonce: str, expires_at: datetime) -> None:
        now_text = datetime.now(timezone.utc).isoformat()
        with self._connect() as connection:
            connection.execute("BEGIN IMMEDIATE")
            try:
                connection.execute("DELETE FROM used_nonces WHERE expires_at < ?", (now_text,))
                connection.execute(
                    "INSERT INTO used_nonces (issuer, nonce, expires_at) VALUES (?, ?, ?)",
                    (issuer, nonce, expires_at.astimezone(timezone.utc).isoformat()),
                )
                connection.execute("COMMIT")
            except sqlite3.IntegrityError as exc:
                connection.execute("ROLLBACK")
                raise ReplayDetected("task nonce was already used") from exc
            except Exception:
                connection.execute("ROLLBACK")
                raise

