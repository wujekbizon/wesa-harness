from __future__ import annotations

import json
import hashlib
import os
import uuid
from datetime import datetime, timedelta, timezone
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Callable, Mapping, Sequence

import psycopg
from psycopg.rows import dict_row
from psycopg.types.json import Jsonb

from wesa_engine.kernel.audit import AuditLog
from wesa_engine.kernel.gate import redact_text


def _token_estimate(text: str) -> int:
    return max(1, len(text) // 4)


def _json_text(value: Any) -> str:
    if isinstance(value, str):
        return value
    return json.dumps(value, ensure_ascii=False, sort_keys=True)


@dataclass(frozen=True, slots=True)
class MemoryContext:
    text: str
    token_estimate: int
    policies: int
    preferences: int
    facts: int
    episodes: int
    fact_ids: tuple[str, ...]
    episode_ids: tuple[str, ...]
    retrieval_mode: str


@dataclass(frozen=True, slots=True)
class FactPromotionResult:
    outcome: str
    reason: str | None = None
    fact_id: str | None = None
    status: str | None = None
    superseded_fact_id: str | None = None


def assemble_memory_context(
    policies: Sequence[Mapping[str, Any]],
    preferences: Sequence[Mapping[str, Any]],
    facts: Sequence[Mapping[str, Any]],
    episodes: Sequence[Mapping[str, Any]],
    token_budget: int,
) -> MemoryContext:
    header = [
        '<memory_context trust="kernel-mediated">',
        "  <!-- Policies bind. Preferences guide. Facts are current assertions. Episodes are prior outcomes. -->",
    ]
    reserved: list[str] = []
    if policies:
        reserved.append("  <policies>")
        reserved.extend(
            f"    {row['policy_key']}={_json_text(row['policy_value'])}" for row in policies
        )
        reserved.append("  </policies>")
    if preferences:
        reserved.append("  <preferences>")
        reserved.extend(
            f"    {row['pref_key']}={_json_text(row['pref_value'])}" for row in preferences
        )
        reserved.append("  </preferences>")

    used = _token_estimate("\n".join(header + reserved + ["</memory_context>"]))
    fact_lines: list[str] = []
    fact_ids: list[str] = []
    for row in facts:
        line = f"    {row['subject']}::{row['predicate']} — {row['content']}"
        cost = _token_estimate(line)
        if used + cost <= token_budget:
            fact_lines.append(line)
            fact_ids.append(str(row["fact_id"]))
            used += cost

    episode_lines: list[str] = []
    episode_ids: list[str] = []
    for row in episodes:
        line = f"    [{row['task_type']}] {row['title']} — {row['summary']}"
        cost = _token_estimate(line)
        if used + cost <= token_budget:
            episode_lines.append(line)
            episode_ids.append(str(row["episode_id"]))
            used += cost

    body = header + reserved
    if fact_lines:
        body.extend(["  <facts>", *fact_lines, "  </facts>"])
    if episode_lines:
        body.extend(["  <episodes>", *episode_lines, "  </episodes>"])
    body.append("</memory_context>")
    text = "\n".join(body)
    return MemoryContext(
        text=text,
        token_estimate=_token_estimate(text),
        policies=len(policies),
        preferences=len(preferences),
        facts=len(fact_lines),
        episodes=len(episode_lines),
        fact_ids=tuple(fact_ids),
        episode_ids=tuple(episode_ids),
        retrieval_mode="lexical" if fact_lines or episode_lines else "scope-only",
    )


class MemoryService:
    def __init__(
        self,
        dsn: str | None,
        audit: AuditLog,
        tenant_id: str = "wesa",
        user_id: str = "greg",
        token_budget: int = 1600,
        connection_factory: Callable[[], Any] | None = None,
    ):
        self.dsn = dsn
        self.audit = audit
        self.tenant_id = tenant_id
        self.user_id = user_id
        self.token_budget = token_budget
        self._connection_factory = connection_factory

    @classmethod
    def from_environment(cls, audit: AuditLog) -> "MemoryService":
        budget = int(os.getenv("WESA_MEMORY_TOKEN_BUDGET", "1600"))
        return cls(
            os.getenv("WESA_HARNESS_DATABASE_URL"),
            audit,
            tenant_id=os.getenv("WESA_MEMORY_TENANT", "wesa"),
            user_id=os.getenv("WESA_MEMORY_USER", "greg"),
            token_budget=max(256, min(budget, 8000)),
        )

    @property
    def enabled(self) -> bool:
        return bool(self.dsn or self._connection_factory)

    def _connect(self):
        if self._connection_factory is not None:
            return self._connection_factory()
        if not self.dsn:
            raise RuntimeError("memory database is not configured")
        return psycopg.connect(self.dsn, row_factory=dict_row)

    def build_task_context(self, query: str, agent_id: str) -> MemoryContext:
        if not self.enabled:
            return assemble_memory_context([], [], [], [], self.token_budget)
        like_query = f"%{query[:500]}%"
        with self._connect() as connection:
            policies = connection.execute(
                """
                SELECT policy_key, policy_value
                  FROM memory.policy_memory
                 WHERE tenant_id = %s AND effective_until IS NULL
                 ORDER BY policy_key
                """,
                (self.tenant_id,),
            ).fetchall()
            preferences = connection.execute(
                """
                SELECT pref_key, pref_value
                  FROM memory.preference_memory
                 WHERE tenant_id = %s
                   AND (user_id IS NULL OR user_id = %s)
                   AND (agent_id IS NULL OR agent_id = %s)
                 ORDER BY pref_key
                """,
                (self.tenant_id, self.user_id, agent_id),
            ).fetchall()
            facts = connection.execute(
                """
                SELECT fact_id, subject, predicate, content
                  FROM memory.fact_memory
                 WHERE tenant_id = %s
                   AND status = 'active'
                   AND (user_id IS NULL OR user_id = %s)
                   AND (agent_id IS NULL OR agent_id = %s)
                   AND (expires_at IS NULL OR expires_at > now())
                   AND (
                       to_tsvector('english', content) @@ websearch_to_tsquery('english', %s)
                       OR content ILIKE %s
                   )
                 ORDER BY ts_rank(
                     to_tsvector('english', content),
                     websearch_to_tsquery('english', %s)
                 ) DESC, created_at DESC
                 LIMIT 8
                """,
                (self.tenant_id, self.user_id, agent_id, query[:500], like_query, query[:500]),
            ).fetchall()
            episodes = connection.execute(
                """
                SELECT episode_id, task_type, title, summary
                  FROM memory.episodic_memory
                 WHERE tenant_id = %s
                   AND status = 'active'
                   AND (user_id IS NULL OR user_id = %s)
                   AND (
                       to_tsvector('english', summary) @@ websearch_to_tsquery('english', %s)
                       OR summary ILIKE %s
                   )
                 ORDER BY ts_rank(
                     to_tsvector('english', summary),
                     websearch_to_tsquery('english', %s)
                 ) DESC, completed_at DESC
                 LIMIT 5
                """,
                (self.tenant_id, self.user_id, query[:500], like_query, query[:500]),
            ).fetchall()
        context = assemble_memory_context(
            policies,
            preferences,
            facts,
            episodes,
            self.token_budget,
        )
        self.audit.append(
            "kernel:memory",
            "memory_retrieved",
            {
                "agentId": agent_id,
                "tenantId": self.tenant_id,
                "factIds": list(context.fact_ids),
                "episodeIds": list(context.episode_ids),
                "tokens": context.token_estimate,
                "mode": context.retrieval_mode,
            },
        )
        return context

    def seed_curated(self, seed_file: Path) -> Mapping[str, int]:
        if not self.enabled or not seed_file.exists():
            return {"policies": 0, "preferences": 0}
        value = json.loads(seed_file.read_text(encoding="utf-8"))
        policies = value.get("policies", [])
        preferences = value.get("preferences", [])
        policy_changes = 0
        preference_changes = 0
        with self._connect() as connection:
            for policy in policies:
                current = connection.execute(
                    """
                    SELECT policy_id, policy_value, version
                      FROM memory.policy_memory
                     WHERE tenant_id = %s AND policy_key = %s AND effective_until IS NULL
                     ORDER BY version DESC LIMIT 1 FOR UPDATE
                    """,
                    (self.tenant_id, policy["key"]),
                ).fetchone()
                if current and current["policy_value"] == policy["value"]:
                    continue
                version = int(current["version"]) + 1 if current else 1
                if current:
                    connection.execute(
                        "UPDATE memory.policy_memory SET effective_until = now() WHERE policy_id = %s",
                        (current["policy_id"],),
                    )
                connection.execute(
                    """
                    INSERT INTO memory.policy_memory
                        (policy_id, tenant_id, policy_type, policy_key, policy_value,
                         version, created_by)
                    VALUES (%s, %s, %s, %s, %s, %s, 'axel')
                    """,
                    (
                        f"pol_{uuid.uuid4().hex[:12]}",
                        self.tenant_id,
                        policy["type"],
                        policy["key"],
                        Jsonb(policy["value"]),
                        version,
                    ),
                )
                policy_changes += 1
            for preference in preferences:
                current = connection.execute(
                    """
                    SELECT pref_value
                      FROM memory.preference_memory
                     WHERE tenant_id = %s
                       AND COALESCE(user_id, '') = COALESCE(%s, '')
                       AND COALESCE(agent_id, '') = COALESCE(%s, '')
                       AND pref_key = %s
                    """,
                    (
                        self.tenant_id,
                        preference.get("userId", self.user_id),
                        preference.get("agentId"),
                        preference["key"],
                    ),
                ).fetchone()
                if current and current["pref_value"] == preference["value"]:
                    continue
                connection.execute(
                    """
                    INSERT INTO memory.preference_memory
                        (tenant_id, user_id, agent_id, pref_key, pref_value,
                         source, confidence, updated_at)
                    VALUES (%s, %s, %s, %s, %s, 'axel_curated', 1.0, now())
                    ON CONFLICT
                        (tenant_id, COALESCE(user_id, ''), COALESCE(agent_id, ''), pref_key)
                    DO UPDATE SET
                        pref_value = EXCLUDED.pref_value,
                        source = EXCLUDED.source,
                        confidence = EXCLUDED.confidence,
                        updated_at = EXCLUDED.updated_at
                    """,
                    (
                        self.tenant_id,
                        preference.get("userId", self.user_id),
                        preference.get("agentId"),
                        preference["key"],
                        Jsonb(preference["value"]),
                    ),
                )
                preference_changes += 1
            connection.commit()
        result = {"policies": policy_changes, "preferences": preference_changes}
        self.audit.append("kernel:memory", "curated_seed_completed", result)
        return result

    def run_retention_if_due(self, marker_file: Path) -> Mapping[str, int] | None:
        if not self.enabled:
            return None
        if marker_file.exists():
            modified = datetime.fromtimestamp(marker_file.stat().st_mtime, tz=timezone.utc)
            if datetime.now(timezone.utc) - modified < timedelta(hours=24):
                return None
        trace_days = int(os.getenv("WESA_MEMORY_TRACE_RETENTION_DAYS", "0"))
        result = self.sweep_retention(trace_days=trace_days)
        marker_file.parent.mkdir(parents=True, exist_ok=True)
        temporary = marker_file.with_suffix(".tmp")
        temporary.write_text(datetime.now(timezone.utc).isoformat(), encoding="utf-8")
        os.replace(temporary, marker_file)
        return result

    def post_accept(
        self,
        *,
        run_id: str,
        agent_id: str,
        task_type: str,
        prompt: str,
        result: str,
        output_path: str | None,
    ) -> str | None:
        if not self.enabled:
            return None
        title = f"{agent_id} {task_type}: {Path(output_path).name if output_path else run_id}"
        summary = result[:4000]
        with self._connect() as connection:
            episode = connection.execute(
                """
                INSERT INTO memory.episodic_memory
                    (tenant_id, user_id, task_type, title, summary, outcome,
                     key_steps, artifacts, status, source_run_id)
                VALUES (%s, %s, %s, %s, %s, 'completed', %s, %s, 'active', %s)
                RETURNING episode_id
                """,
                (
                    self.tenant_id,
                    self.user_id,
                    task_type,
                    title,
                    summary,
                    Jsonb(["signed", "authorized", "generated", "gate_accepted"]),
                    Jsonb({"outputPath": output_path}),
                    run_id,
                ),
            ).fetchone()
            episode_id = str(episode["episode_id"])
            connection.execute(
                """
                INSERT INTO memory.trace_memory
                    (run_id, tenant_id, user_id, agent_id, event_type, payload)
                VALUES (%s, %s, %s, %s, 'task_completed', %s)
                """,
                (
                    run_id,
                    self.tenant_id,
                    self.user_id,
                    agent_id,
                    Jsonb(
                        {
                            "taskType": task_type,
                            "prompt": prompt[:500],
                            "episodeId": episode_id,
                            "outputPath": output_path,
                        }
                    ),
                ),
            )
            connection.commit()
        self.audit.append(
            "kernel:memory",
            "episode_promoted",
            {
                "taskId": run_id,
                "episodeId": episode_id,
                "agentId": agent_id,
                "source": "accepted_artifact",
            },
        )
        return episode_id

    def promote_fact(
        self,
        *,
        subject: str,
        predicate: str,
        content: str,
        confidence: float,
        source_run_id: str,
        source: str,
        agent_id: str | None = None,
        user_id: str | None = None,
    ) -> FactPromotionResult:
        if confidence < 0.7:
            return self._promotion_result(
                FactPromotionResult("rejected", reason="low_confidence"),
                source_run_id,
                subject,
                predicate,
                confidence,
            )
        if not source_run_id or not subject or not predicate:
            return self._promotion_result(
                FactPromotionResult("rejected", reason="missing_provenance_or_identity"),
                source_run_id,
                subject,
                predicate,
                confidence,
            )
        safe_content, _ = redact_text(content)
        normalized = " ".join(safe_content.split()).casefold()
        content_hash = hashlib.sha256(normalized.encode("utf-8")).hexdigest()
        effective_user = user_id
        effective_agent = agent_id
        trusted = source in {"greg_stated", "axel_curated", "admin_set", "user_stated"}
        status = "active" if trusted or effective_agent else "provisional"
        with self._connect() as connection:
            existing = connection.execute(
                """
                SELECT fact_id
                  FROM memory.fact_memory
                 WHERE tenant_id = %s
                   AND COALESCE(user_id, '') = COALESCE(%s, '')
                   AND COALESCE(agent_id, '') = COALESCE(%s, '')
                   AND content_hash = %s
                   AND status <> 'revoked'
                 LIMIT 1
                """,
                (self.tenant_id, effective_user, effective_agent, content_hash),
            ).fetchone()
            if existing:
                connection.rollback()
                return self._promotion_result(
                    FactPromotionResult("deduplicated", fact_id=str(existing["fact_id"])),
                    source_run_id,
                    subject,
                    predicate,
                    confidence,
                )
            contradiction = connection.execute(
                """
                SELECT fact_id
                  FROM memory.fact_memory
                 WHERE tenant_id = %s
                   AND COALESCE(user_id, '') = COALESCE(%s, '')
                   AND COALESCE(agent_id, '') = COALESCE(%s, '')
                   AND subject = %s AND predicate = %s
                   AND status = 'active'
                 ORDER BY created_at DESC
                 LIMIT 1
                 FOR UPDATE
                """,
                (self.tenant_id, effective_user, effective_agent, subject, predicate),
            ).fetchone()
            fact_id = f"fact_{uuid.uuid4().hex[:12]}"
            connection.execute(
                """
                INSERT INTO memory.fact_memory
                    (fact_id, tenant_id, user_id, agent_id, subject, predicate,
                     content, content_hash, status, source, source_run_id, confidence)
                VALUES (%s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s)
                """,
                (
                    fact_id,
                    self.tenant_id,
                    effective_user,
                    effective_agent,
                    subject,
                    predicate,
                    safe_content,
                    content_hash,
                    status,
                    source,
                    source_run_id,
                    confidence,
                ),
            )
            superseded_id: str | None = None
            if contradiction and status == "active":
                superseded_id = str(contradiction["fact_id"])
                connection.execute(
                    """
                    UPDATE memory.fact_memory
                       SET status = 'revoked', superseded_by = %s
                     WHERE fact_id = %s
                    """,
                    (fact_id, superseded_id),
                )
            connection.commit()
        outcome = "superseded" if superseded_id else "written"
        return self._promotion_result(
            FactPromotionResult(outcome, fact_id=fact_id, status=status, superseded_fact_id=superseded_id),
            source_run_id,
            subject,
            predicate,
            confidence,
        )

    def erase_user(self, user_id: str, reason: str = "gdpr_erasure") -> Mapping[str, int]:
        with self._connect() as connection:
            facts = connection.execute(
                """
                UPDATE memory.fact_memory
                   SET content = '[erased]', content_hash = 'erased', embedding = NULL, status = 'revoked'
                 WHERE tenant_id = %s AND user_id = %s
                """,
                (self.tenant_id, user_id),
            ).rowcount
            episodes = connection.execute(
                """
                UPDATE memory.episodic_memory
                   SET summary = '[erased]', embedding = NULL, status = 'revoked'
                 WHERE tenant_id = %s AND user_id = %s
                """,
                (self.tenant_id, user_id),
            ).rowcount
            preferences = connection.execute(
                "DELETE FROM memory.preference_memory WHERE tenant_id = %s AND user_id = %s",
                (self.tenant_id, user_id),
            ).rowcount
            traces = connection.execute(
                "DELETE FROM memory.trace_memory WHERE tenant_id = %s AND user_id = %s",
                (self.tenant_id, user_id),
            ).rowcount
            connection.execute(
                "INSERT INTO memory.deletion_events (user_id, scope, reason) VALUES (%s, 'all', %s)",
                (user_id, reason),
            )
            connection.commit()
        counts = {
            "facts": facts,
            "episodes": episodes,
            "preferences": preferences,
            "traces": traces,
        }
        self.audit.append(
            "kernel:memory",
            "gdpr_erasure_completed",
            {"tenantId": self.tenant_id, "userId": user_id, "counts": counts},
        )
        return counts

    def sweep_retention(self, trace_days: int = 0) -> Mapping[str, int]:
        with self._connect() as connection:
            revoked = connection.execute(
                """
                UPDATE memory.fact_memory
                   SET status = 'revoked', embedding = NULL
                 WHERE tenant_id = %s AND expires_at <= now() AND status <> 'revoked'
                """,
                (self.tenant_id,),
            ).rowcount
            traces = 0
            if trace_days > 0:
                traces = connection.execute(
                    """
                    DELETE FROM memory.trace_memory
                     WHERE tenant_id = %s
                       AND created_at < now() - make_interval(days => %s)
                    """,
                    (self.tenant_id, trace_days),
                ).rowcount
            connection.commit()
        result = {"expiredFacts": revoked, "deletedTraces": traces}
        self.audit.append("kernel:memory", "retention_sweep_completed", result)
        return result

    def _promotion_result(
        self,
        result: FactPromotionResult,
        source_run_id: str,
        subject: str,
        predicate: str,
        confidence: float,
    ) -> FactPromotionResult:
        self.audit.append(
            "kernel:promotion_gate",
            "fact_promotion_result",
            {
                "taskId": source_run_id,
                "subject": subject,
                "predicate": predicate,
                "confidence": confidence,
                "outcome": result.outcome,
                "reason": result.reason,
                "factId": result.fact_id,
                "status": result.status,
                "supersededFactId": result.superseded_fact_id,
            },
        )
        return result
