from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path

import yaml


@dataclass(frozen=True, slots=True)
class LegacyAgentDefinition:
    name: str
    model: str
    task_types: tuple[str, ...]
    source: str


def read_legacy_agents(skills_dir: Path) -> tuple[LegacyAgentDefinition, ...]:
    """Read legacy skill metadata. Never writes to the legacy workspace."""
    definitions: list[LegacyAgentDefinition] = []
    for path in sorted(skills_dir.glob("*.yaml")):
        data = yaml.safe_load(path.read_text(encoding="utf-8")) or {}
        name = data.get("name")
        model = data.get("model")
        task_types = data.get("task_types")
        if not isinstance(name, str) or not isinstance(model, str) or not isinstance(task_types, list):
            continue
        if any(not isinstance(value, str) for value in task_types):
            continue
        definitions.append(LegacyAgentDefinition(name, model, tuple(task_types), str(path)))
    return tuple(definitions)

