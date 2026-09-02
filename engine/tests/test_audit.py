import json
from concurrent.futures import ThreadPoolExecutor

from wesa_engine.kernel.audit import AuditLog


def test_audit_chain_detects_tampering(tmp_path):
    path = tmp_path / "AUDIT.jsonl"
    audit = AuditLog(path)
    audit.append("kernel", "started")
    audit.append("kernel", "checked")
    assert audit.verify().ok

    records = path.read_text(encoding="utf-8").splitlines()
    first = json.loads(records[0])
    first["event"] = "tampered"
    records[0] = json.dumps(first)
    path.write_text("\n".join(records) + "\n", encoding="utf-8")

    assert not audit.verify().ok


def test_audit_chain_serializes_concurrent_appenders(tmp_path):
    path = tmp_path / "AUDIT.jsonl"

    def append(index: int):
        AuditLog(path).append("worker", "concurrent", {"index": index})

    with ThreadPoolExecutor(max_workers=8) as executor:
        list(executor.map(append, range(100)))

    verification = AuditLog(path).verify()
    assert verification.ok
    assert verification.records == 100
