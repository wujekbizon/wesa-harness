from __future__ import annotations

import argparse
import hashlib
import json
import shutil
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Mapping

from wesa_engine.kernel.audit import AuditLog


def canonical(record: Mapping[str, Any]) -> bytes:
    return json.dumps(record, sort_keys=True, separators=(",", ":"), ensure_ascii=False).encode()


def record_hash(record: Mapping[str, Any]) -> str:
    unsigned = {key: value for key, value in record.items() if key != "hash"}
    return hashlib.sha256(canonical(unsigned)).hexdigest()


def main() -> int:
    parser = argparse.ArgumentParser(description="Preserve and re-chain one concurrent audit fork")
    parser.add_argument("audit_file", type=Path)
    parser.add_argument("--backup-dir", type=Path, required=True)
    parser.add_argument("--expect-records", type=int, required=True)
    parser.add_argument("--expect-first-invalid-line", type=int, required=True)
    arguments = parser.parse_args()

    lines = arguments.audit_file.read_text(encoding="utf-8").splitlines()
    records = [json.loads(line) for line in lines]
    if len(records) != arguments.expect_records:
        raise SystemExit(f"record count changed: expected {arguments.expect_records}, found {len(records)}")

    previous_hash = ""
    first_invalid: int | None = None
    for line_number, record in enumerate(records, 1):
        valid = (
            record.get("seq") == line_number
            and record.get("previousHash", "") == previous_hash
            and record.get("hash") == record_hash(record)
        )
        if not valid:
            first_invalid = line_number
            break
        previous_hash = str(record["hash"])
    if first_invalid != arguments.expect_first_invalid_line:
        raise SystemExit(
            f"first invalid line changed: expected {arguments.expect_first_invalid_line}, found {first_invalid}"
        )

    fork = records[first_invalid - 1]
    prior = records[first_invalid - 2]
    if not (
        fork.get("seq") == prior.get("seq")
        and fork.get("previousHash") == prior.get("previousHash")
        and fork.get("hash") == record_hash(fork)
    ):
        raise SystemExit("invalid record is not the expected independently valid concurrent fork")

    previous_hash = str(prior["hash"])
    for line_number in range(first_invalid, len(records) + 1):
        record = records[line_number - 1]
        record["seq"] = line_number
        record["previousHash"] = previous_hash
        record["hash"] = record_hash(record)
        previous_hash = str(record["hash"])

    arguments.backup_dir.mkdir(parents=True, exist_ok=True)
    timestamp = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ")
    backup = arguments.backup_dir / f"{arguments.audit_file.stem}.pre-rechain-{timestamp}.jsonl"
    shutil.copy2(arguments.audit_file, backup)

    temporary = arguments.audit_file.with_name(f"{arguments.audit_file.name}.repairing")
    temporary.write_text(
        "".join(json.dumps(record, separators=(",", ":"), ensure_ascii=False) + "\n" for record in records),
        encoding="utf-8",
        newline="\n",
    )
    verification = AuditLog(temporary).verify()
    if not verification.ok:
        temporary.unlink(missing_ok=True)
        raise SystemExit(f"re-chained candidate invalid: {verification.error}")
    temporary.replace(arguments.audit_file)
    print(
        f"repaired_from_line={first_invalid} records_preserved={verification.records} backup={backup}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
