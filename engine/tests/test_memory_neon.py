import os
import json
import uuid
from pathlib import Path

import psycopg
import pytest
from psycopg.rows import dict_row

from wesa_engine.kernel.audit import AuditLog
from wesa_engine.memory import MemoryService


DSN = os.getenv("WESA_MEMORY_TEST_DATABASE_URL") or os.getenv("WESA_HARNESS_DATABASE_URL")


@pytest.mark.skipif(not DSN, reason="live memory DSN is not configured")
def test_scope_before_rank_excludes_other_tenant(tmp_path: Path):
    tenant_a = f"test-a-{uuid.uuid4()}"
    tenant_b = f"test-b-{uuid.uuid4()}"
    fact_a = f"fact_{uuid.uuid4().hex[:12]}"
    fact_b = f"fact_{uuid.uuid4().hex[:12]}"
    with psycopg.connect(DSN, row_factory=dict_row) as connection:
        try:
            connection.execute(
                """
                INSERT INTO memory.fact_memory
                    (fact_id, tenant_id, subject, predicate, content, content_hash,
                     status, source, source_run_id, confidence)
                VALUES
                    (%s, %s, 'scope-test', 'owner', 'shared needle tenant A', %s,
                     'active', 'axel_curated', 'scope-test', 1.0),
                    (%s, %s, 'scope-test', 'owner', 'shared needle tenant B', %s,
                     'active', 'axel_curated', 'scope-test', 1.0)
                """,
                (fact_a, tenant_a, uuid.uuid4().hex, fact_b, tenant_b, uuid.uuid4().hex),
            )
            connection.commit()
            service = MemoryService(
                DSN,
                AuditLog(tmp_path / "scope-audit.jsonl"),
                tenant_id=tenant_a,
            )
            context = service.build_task_context("shared needle", "D1")
            assert fact_a in context.fact_ids
            assert fact_b not in context.fact_ids
        finally:
            connection.execute(
                "DELETE FROM memory.fact_memory WHERE tenant_id IN (%s, %s)",
                (tenant_a, tenant_b),
            )
            connection.commit()


@pytest.mark.skipif(not DSN, reason="live memory DSN is not configured")
def test_promotion_gate_rejects_redacts_deduplicates_and_supersedes(tmp_path: Path):
    tenant = f"promotion-{uuid.uuid4()}"
    service = MemoryService(DSN, AuditLog(tmp_path / "promotion-audit.jsonl"), tenant_id=tenant)
    try:
        rejected = service.promote_fact(
            subject="security", predicate="threshold", content="low confidence candidate",
            confidence=0.69, source_run_id="promotion-test-low", source="inferred",
        )
        assert rejected.outcome == "rejected"
        assert rejected.reason == "low_confidence"

        raw_secret = "sk-ABCDEFGHIJKLMNOPQRSTUVWXYZ123456"
        first = service.promote_fact(
            subject="security", predicate="credential", content=f"api_key={raw_secret}",
            confidence=1.0, source_run_id="promotion-test-redact", source="axel_curated",
        )
        duplicate = service.promote_fact(
            subject="security", predicate="credential",
            content="api_key=sk-ZYXWVUTSRQPONMLKJIHGFEDCBA654321",
            confidence=1.0, source_run_id="promotion-test-dedup", source="axel_curated",
        )
        old = service.promote_fact(
            subject="harness", predicate="phase", content="P3", confidence=1.0,
            source_run_id="promotion-test-old", source="axel_curated",
        )
        new = service.promote_fact(
            subject="harness", predicate="phase", content="P4", confidence=1.0,
            source_run_id="promotion-test-new", source="axel_curated",
        )

        assert first.outcome == "written"
        assert duplicate.outcome == "deduplicated"
        assert old.outcome == "written"
        assert new.outcome == "superseded"
        with psycopg.connect(DSN, row_factory=dict_row) as connection:
            credential = connection.execute(
                "SELECT content FROM memory.fact_memory WHERE fact_id = %s", (first.fact_id,)
            ).fetchone()
            assert raw_secret not in credential["content"]
            assert "[REDACTED:CREDENTIAL]" in credential["content"]
            phase_rows = connection.execute(
                """
                SELECT fact_id, status, superseded_by FROM memory.fact_memory
                 WHERE tenant_id = %s AND subject = 'harness' AND predicate = 'phase'
                """,
                (tenant,),
            ).fetchall()
            assert sum(row["status"] == "active" for row in phase_rows) == 1
            assert any(
                row["status"] == "revoked" and row["superseded_by"] == new.fact_id
                for row in phase_rows
            )
    finally:
        with psycopg.connect(DSN) as connection:
            connection.execute("DELETE FROM memory.fact_memory WHERE tenant_id = %s", (tenant,))
            connection.commit()


@pytest.mark.skipif(not DSN, reason="live memory DSN is not configured")
def test_gdpr_erasure_removes_user_from_every_retrieval_projection(tmp_path: Path):
    tenant = f"gdpr-{uuid.uuid4()}"
    user = f"user-{uuid.uuid4()}"
    service = MemoryService(
        DSN, AuditLog(tmp_path / "gdpr-audit.jsonl"), tenant_id=tenant, user_id=user,
    )
    try:
        fact = service.promote_fact(
            subject="private", predicate="marker", content="private marker erase me",
            confidence=1.0, source_run_id="gdpr-test", source="greg_stated", user_id=user,
        )
        with psycopg.connect(DSN) as connection:
            connection.execute(
                """
                INSERT INTO memory.preference_memory
                    (tenant_id, user_id, pref_key, pref_value, source, confidence)
                VALUES (%s, %s, 'gdpr-test', 'true'::jsonb, 'greg_stated', 1.0)
                """,
                (tenant, user),
            )
            connection.execute(
                """
                INSERT INTO memory.episodic_memory
                    (tenant_id, user_id, task_type, title, summary, outcome, status, source_run_id)
                VALUES (%s, %s, 'test', 'GDPR', 'private marker episode', 'completed', 'active', 'gdpr-test')
                """,
                (tenant, user),
            )
            connection.execute(
                """
                INSERT INTO memory.trace_memory
                    (run_id, tenant_id, user_id, event_type, payload)
                VALUES ('gdpr-test', %s, %s, 'task_completed', '{}'::jsonb)
                """,
                (tenant, user),
            )
            connection.commit()

        counts = service.erase_user(user, "integration_test")
        context = service.build_task_context("private marker", "D1")
        assert fact.outcome == "written"
        assert all(value >= 1 for value in counts.values())
        assert not context.fact_ids
        assert not context.episode_ids
        with psycopg.connect(DSN, row_factory=dict_row) as connection:
            erased = connection.execute(
                "SELECT content, status, embedding IS NULL AS no_embedding "
                "FROM memory.fact_memory WHERE fact_id = %s",
                (fact.fact_id,),
            ).fetchone()
            assert erased == {"content": "[erased]", "status": "revoked", "no_embedding": True}
    finally:
        with psycopg.connect(DSN) as connection:
            connection.execute("DELETE FROM memory.trace_memory WHERE tenant_id = %s", (tenant,))
            connection.execute("DELETE FROM memory.preference_memory WHERE tenant_id = %s", (tenant,))
            connection.execute("DELETE FROM memory.episodic_memory WHERE tenant_id = %s", (tenant,))
            connection.execute("DELETE FROM memory.fact_memory WHERE tenant_id = %s", (tenant,))
            connection.execute("DELETE FROM memory.deletion_events WHERE user_id = %s", (user,))
            connection.commit()


@pytest.mark.skipif(not DSN, reason="live memory DSN is not configured")
def test_curated_seed_is_idempotent(tmp_path: Path):
    tenant = f"seed-{uuid.uuid4()}"
    user = f"user-{uuid.uuid4()}"
    seed_file = tmp_path / "seed.json"
    seed_file.write_text(
        json.dumps(
            {
                "policies": [
                    {"key": "security.test", "type": "security", "value": {"enabled": True}}
                ],
                "preferences": [
                    {"key": "response.test", "userId": user, "value": "concise"}
                ],
            }
        ),
        encoding="utf-8",
    )
    service = MemoryService(
        DSN, AuditLog(tmp_path / "seed-audit.jsonl"), tenant_id=tenant, user_id=user,
    )
    try:
        first = service.seed_curated(seed_file)
        second = service.seed_curated(seed_file)
        assert first == {"policies": 1, "preferences": 1}
        assert second == {"policies": 0, "preferences": 0}
    finally:
        with psycopg.connect(DSN) as connection:
            connection.execute("DELETE FROM memory.preference_memory WHERE tenant_id = %s", (tenant,))
            connection.execute("DELETE FROM memory.policy_memory WHERE tenant_id = %s", (tenant,))
            connection.commit()
