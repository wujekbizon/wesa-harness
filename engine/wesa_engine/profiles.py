from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path
from typing import Any, Mapping

import yaml


class ProfileError(ValueError):
    pass


@dataclass(frozen=True, slots=True)
class AgentProfile:
    agent_id: str
    role: str
    default_provider: str
    allowed_providers: tuple[str, ...]
    default_model: str | None
    task_types: tuple[str, ...]
    system_prompt: str
    capability_manifest: str
    source: str

    @classmethod
    def from_file(cls, path: Path) -> "AgentProfile":
        value: Any = yaml.safe_load(path.read_text(encoding="utf-8"))
        if not isinstance(value, Mapping) or value.get("schemaVersion") != 2:
            raise ProfileError(f"invalid profile schema: {path.name}")
        required = (
            "id",
            "role",
            "defaultProvider",
            "allowedProviders",
            "taskTypes",
            "systemPrompt",
            "capabilityManifest",
            "source",
        )
        if any(key not in value for key in required):
            raise ProfileError(f"profile missing required field: {path.name}")
        task_types = value["taskTypes"]
        if not isinstance(task_types, list) or any(not isinstance(item, str) for item in task_types):
            raise ProfileError(f"invalid taskTypes: {path.name}")
        default_model = value.get("defaultModel")
        if default_model is not None and not isinstance(default_model, str):
            raise ProfileError(f"invalid defaultModel: {path.name}")
        allowed_providers = value["allowedProviders"]
        if (
            not isinstance(allowed_providers, list)
            or not allowed_providers
            or any(not isinstance(item, str) or not item for item in allowed_providers)
        ):
            raise ProfileError(f"invalid allowedProviders: {path.name}")
        profile = cls(
            str(value["id"]),
            str(value["role"]),
            str(value["defaultProvider"]),
            tuple(allowed_providers),
            default_model,
            tuple(task_types),
            str(value["systemPrompt"]),
            str(value["capabilityManifest"]),
            str(value["source"]),
        )
        if profile.default_provider not in profile.allowed_providers:
            raise ProfileError(f"defaultProvider must be allowed: {path.name}")
        if len(profile.system_prompt) < 50:
            raise ProfileError(f"system prompt too short: {path.name}")
        return profile

    def allows_provider(self, provider_id: str) -> bool:
        return provider_id in self.allowed_providers


def load_profiles(path: Path) -> dict[str, AgentProfile]:
    profiles: dict[str, AgentProfile] = {}
    for file in sorted(path.glob("*.agent.yaml")):
        profile = AgentProfile.from_file(file)
        if profile.agent_id in profiles:
            raise ProfileError(f"duplicate agent profile: {profile.agent_id}")
        profiles[profile.agent_id] = profile
    if not profiles:
        raise ProfileError("no agent profiles found")
    return profiles
