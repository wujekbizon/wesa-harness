from pathlib import Path

from wesa_engine.kernel.audit import AuditLog
from wesa_engine.memory.service import MemoryService, assemble_memory_context


class FakeResult:
    def __init__(self, rows):
        self.rows = rows

    def fetchall(self):
        return self.rows

    def fetchone(self):
        return self.rows[0]


class FakeConnection:
    def __init__(self):
        self.queries = []
        self.committed = False

    def __enter__(self):
        return self

    def __exit__(self, *_):
        return False

    def execute(self, query, parameters):
        normalized = " ".join(query.split())
        self.queries.append((normalized, parameters))
        if "policy_memory" in normalized:
            return FakeResult([{"policy_key": "security.defaultDeny", "policy_value": True}])
        if "preference_memory" in normalized:
            return FakeResult([{"pref_key": "response.concise", "pref_value": True}])
        if "FROM memory.fact_memory" in normalized:
            return FakeResult(
                [
                    {
                        "fact_id": "fact-1",
                        "subject": "harness",
                        "predicate": "provider",
                        "content": "Ollama is the only P3 provider.",
                    }
                ]
            )
        if "FROM memory.episodic_memory" in normalized:
            return FakeResult(
                [
                    {
                        "episode_id": "episode-1",
                        "task_type": "code",
                        "title": "P3",
                        "summary": "Gate accepted a local model artifact.",
                    }
                ]
            )
        if "INSERT INTO memory.episodic_memory" in normalized:
            return FakeResult([{"episode_id": "episode-new"}])
        return FakeResult([])

    def commit(self):
        self.committed = True


def test_memory_context_reserves_rules_and_bounds_ranked_items():
    context = assemble_memory_context(
        [{"policy_key": "security.defaultDeny", "policy_value": True}],
        [{"pref_key": "response.concise", "pref_value": True}],
        [
            {
                "fact_id": "fact-small",
                "subject": "harness",
                "predicate": "phase",
                "content": "P4",
            },
            {
                "fact_id": "fact-large",
                "subject": "x",
                "predicate": "y",
                "content": "z" * 4000,
            },
        ],
        [],
        200,
    )

    assert "security.defaultDeny=true" in context.text
    assert "response.concise=true" in context.text
    assert context.fact_ids == ("fact-small",)


def test_scope_filters_are_in_sql_before_ranking(tmp_path: Path):
    connection = FakeConnection()
    service = MemoryService(
        None,
        AuditLog(tmp_path / "audit.jsonl"),
        connection_factory=lambda: connection,
    )

    context = service.build_task_context("Ollama provider", "D1")

    fact_query = next(query for query, _ in connection.queries if "FROM memory.fact_memory" in query)
    episode_query = next(query for query, _ in connection.queries if "FROM memory.episodic_memory" in query)
    assert fact_query.index("tenant_id = %s") < fact_query.index("ORDER BY")
    assert fact_query.index("agent_id IS NULL OR agent_id = %s") < fact_query.index("ORDER BY")
    assert episode_query.index("tenant_id = %s") < episode_query.index("ORDER BY")
    assert context.fact_ids == ("fact-1",)
    assert context.episode_ids == ("episode-1",)


def test_post_accept_writes_episode_and_trace_in_one_connection(tmp_path: Path):
    connection = FakeConnection()
    service = MemoryService(
        None,
        AuditLog(tmp_path / "audit.jsonl"),
        connection_factory=lambda: connection,
    )

    episode_id = service.post_accept(
        run_id="task-1",
        agent_id="D1",
        task_type="code",
        prompt="Create code",
        result="def add(a, b): return a + b",
        output_path="staging/sum.py",
    )

    assert episode_id == "episode-new"
    assert any("INSERT INTO memory.episodic_memory" in query for query, _ in connection.queries)
    assert any("INSERT INTO memory.trace_memory" in query for query, _ in connection.queries)
    assert connection.committed
