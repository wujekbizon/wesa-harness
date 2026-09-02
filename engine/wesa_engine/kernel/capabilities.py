from __future__ import annotations

import json
import fnmatch
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Mapping


class CapabilityError(ValueError):
    pass


def _string_tuple(data: Mapping[str, Any], key: str) -> tuple[str, ...]:
    value = data.get(key, [])
    if not isinstance(value, list) or any(not isinstance(item, str) or not item for item in value):
        raise CapabilityError(f"{key} must be an array of non-empty strings")
    return tuple(value)


@dataclass(frozen=True, slots=True)
class ToolGrant:
    name: str
    operations: tuple[str, ...]
    scopes: tuple[str, ...]
    approval: str

    @classmethod
    def from_mapping(cls, value: Mapping[str, Any]) -> "ToolGrant":
        name = value.get("name")
        if not isinstance(name, str) or not name:
            raise CapabilityError("tool name must be a non-empty string")
        operations = _string_tuple(value, "operations")
        scopes = _string_tuple(value, "scopes")
        approval = value.get("approval", "auto-read")
        if approval not in {"auto-read", "axel", "greg"}:
            raise CapabilityError("tool approval must be auto-read, axel or greg")
        return cls(name, operations, scopes, str(approval))


def _tool_tuple(data: Mapping[str, Any]) -> tuple[ToolGrant, ...]:
    value = data.get("tools", [])
    if not isinstance(value, list):
        raise CapabilityError("tools must be an array")
    grants: list[ToolGrant] = []
    for item in value:
        if not isinstance(item, Mapping):
            raise CapabilityError("tools must contain operation-scoped objects")
        grants.append(ToolGrant.from_mapping(item))
    return tuple(grants)


@dataclass(frozen=True, slots=True)
class AgentCapabilityManifest:
    agent_id: str
    providers: tuple[str, ...]
    task_types: tuple[str, ...]
    read_paths: tuple[str, ...]
    write_paths: tuple[str, ...]
    deny_paths: tuple[str, ...]
    tools: tuple[ToolGrant, ...]
    network: str
    execution: str
    max_artifact_kb: int

    @classmethod
    def from_mapping(cls, data: Mapping[str, Any]) -> "AgentCapabilityManifest":
        agent_id = data.get("agentId")
        if not isinstance(agent_id, str) or not agent_id:
            raise CapabilityError("agentId must be a non-empty string")
        network = data.get("network", "none")
        execution = data.get("execution", "none")
        if network not in {"none", "public-read"}:
            raise CapabilityError("network must be none or public-read")
        if execution not in {"none", "sandbox"}:
            raise CapabilityError("execution must be none or sandbox")
        max_artifact_kb = data.get("maxArtifactKb", 512)
        if not isinstance(max_artifact_kb, int) or max_artifact_kb < 1:
            raise CapabilityError("maxArtifactKb must be a positive integer")
        return cls(
            agent_id=agent_id,
            providers=_string_tuple(data, "providers"),
            task_types=_string_tuple(data, "taskTypes"),
            read_paths=_string_tuple(data, "readPaths"),
            write_paths=_string_tuple(data, "writePaths"),
            deny_paths=_string_tuple(data, "denyPaths"),
            tools=_tool_tuple(data),
            network=network,
            execution=execution,
            max_artifact_kb=max_artifact_kb,
        )

    @classmethod
    def from_file(cls, path: Path) -> "AgentCapabilityManifest":
        data = json.loads(path.read_text(encoding="utf-8"))
        if not isinstance(data, Mapping):
            raise CapabilityError(f"manifest root must be an object: {path.name}")
        return cls.from_mapping(data)

    def allows_task_type(self, task_type: str) -> bool:
        return task_type in self.task_types

    def allows_provider(self, provider_id: str) -> bool:
        return provider_id in self.providers

    def allows_write_path(self, relative_path: str) -> bool:
        normalized = relative_path.replace("\\", "/")
        if any(fnmatch.fnmatch(normalized, pattern) for pattern in self.deny_paths):
            return False
        return any(fnmatch.fnmatch(normalized, pattern) for pattern in self.write_paths)

    def allows_read_path(self, relative_path: str) -> bool:
        normalized = relative_path.replace("\\", "/")
        if any(fnmatch.fnmatch(normalized, pattern) for pattern in self.deny_paths):
            return False
        return any(fnmatch.fnmatch(normalized, pattern) for pattern in self.read_paths)

    def tool_grant(self, name: str, operation: str, relative_path: str) -> ToolGrant | None:
        normalized = relative_path.replace("\\", "/")
        for grant in self.tools:
            if grant.name != name or operation not in grant.operations:
                continue
            if any(fnmatch.fnmatch(normalized, scope) for scope in grant.scopes):
                return grant
        return None


def load_manifest_directory(path: Path) -> dict[str, AgentCapabilityManifest]:
    manifests: dict[str, AgentCapabilityManifest] = {}
    for file in sorted(path.glob("*.manifest.json")):
        manifest = AgentCapabilityManifest.from_file(file)
        if manifest.agent_id in manifests:
            raise CapabilityError(f"duplicate agentId: {manifest.agent_id}")
        manifests[manifest.agent_id] = manifest
    if not manifests:
        raise CapabilityError("no capability manifests found")
    return manifests
