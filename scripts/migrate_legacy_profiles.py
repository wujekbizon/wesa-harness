from __future__ import annotations

import argparse
import os
import tempfile
from pathlib import Path

import yaml


def migrate(source: Path, destination: Path) -> int:
    destination.mkdir(parents=True, exist_ok=True)
    count = 0
    for skill_path in sorted(source.glob("*.yaml")):
        skill = yaml.safe_load(skill_path.read_text(encoding="utf-8")) or {}
        agent_id = skill.get("name")
        role = skill.get("role")
        task_types = skill.get("task_types")
        system_prompt = skill.get("system_prompt")
        if not isinstance(agent_id, str) or not isinstance(role, str):
            continue
        if not isinstance(task_types, list) or any(not isinstance(value, str) for value in task_types):
            continue
        if not isinstance(system_prompt, str) or not system_prompt.strip():
            continue
        profile = {
            "schemaVersion": 1,
            "id": agent_id,
            "role": role,
            "provider": "ollama",
            "defaultModel": None,
            "taskTypes": task_types,
            "systemPrompt": system_prompt.strip(),
            "capabilityManifest": f"config/manifests/{agent_id.lower()}.manifest.json",
            "source": f"legacy:agent-ecosystem/agents/skills/{skill_path.name}",
        }
        output = destination / f"{agent_id.lower()}.agent.yaml"
        rendered = yaml.safe_dump(profile, sort_keys=False, allow_unicode=True, width=100)
        with tempfile.NamedTemporaryFile("w", encoding="utf-8", dir=destination, delete=False) as handle:
            handle.write(rendered)
            temporary = Path(handle.name)
        os.replace(temporary, output)
        count += 1
    return count


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("source", type=Path)
    parser.add_argument("destination", type=Path)
    args = parser.parse_args()
    print(f"migrated_profiles={migrate(args.source, args.destination)}")


if __name__ == "__main__":
    main()

