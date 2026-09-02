from pathlib import Path

from wesa_engine.legacy.importer import read_legacy_agents


def test_legacy_importer_is_read_only_and_reads_agent_metadata(tmp_path):
    skill = tmp_path / "writer.yaml"
    skill.write_text(
        "name: W1\nmodel: gemini\ntask_types:\n  - write\nsystem_prompt: ignored\n",
        encoding="utf-8",
    )
    before = skill.read_bytes()

    agents = read_legacy_agents(tmp_path)

    assert [(agent.name, agent.model, agent.task_types) for agent in agents] == [
        ("W1", "gemini", ("write",))
    ]
    assert skill.read_bytes() == before

