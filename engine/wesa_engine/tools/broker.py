from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path
from typing import Any, Mapping

from wesa_engine.kernel.audit import AuditLog
from wesa_engine.kernel.capabilities import AgentCapabilityManifest
from wesa_engine.kernel.protected import ProtectedRegistry
from wesa_engine.plugins.contracts import ToolInvocation, ToolResult
from wesa_engine.plugins.registry import PluginRegistry


class ToolBrokerError(PermissionError):
    pass


@dataclass(frozen=True, slots=True)
class ToolBrokerDecision:
    status: str
    reason: str
    result: ToolResult | None = None


class ToolBroker:
    MUTATING_OPERATIONS = frozenset({"write", "delete", "exec", "network"})

    def __init__(self, plugins: PluginRegistry, protected: ProtectedRegistry, audit: AuditLog):
        self.plugins = plugins
        self.protected = protected
        self.audit = audit
        self._tools = {
            definition.name: ("workspace-tools", definition)
            for definition in plugins.tool_plugin("workspace-tools").definitions()
        }

    def invoke(
        self,
        *,
        agent_id: str,
        manifest: AgentCapabilityManifest,
        name: str,
        operation: str,
        workspace_root: str,
        arguments: Mapping[str, Any],
        approved_by: str | None = None,
    ) -> ToolBrokerDecision:
        registered = self._tools.get(name)
        if registered is None or registered[1].operation != operation:
            return self._deny(agent_id, name, operation, "tool or operation is not registered")
        raw_path = arguments.get("path")
        if not isinstance(raw_path, str) or not raw_path:
            return self._deny(agent_id, name, operation, "tool path is required")
        root = Path(workspace_root).resolve(strict=False)
        target = (root / raw_path).resolve(strict=False)
        try:
            relative = target.relative_to(root).as_posix()
        except ValueError:
            return self._deny(agent_id, name, operation, "tool path escapes workspace")
        if self.protected.is_protected(relative) or not manifest.allows_read_path(relative):
            return self._deny(agent_id, name, operation, "tool scope denies path")
        grant = manifest.tool_grant(name, operation, relative)
        if grant is None:
            return self._deny(agent_id, name, operation, "agent manifest has no matching tool grant")
        if operation in self.MUTATING_OPERATIONS:
            required = "greg" if operation in {"delete", "exec", "network"} else grant.approval
            if approved_by != required:
                self.audit.append(
                    "kernel:tool_broker",
                    "tool_approval_required",
                    {"agentId": agent_id, "tool": name, "operation": operation, "required": required},
                )
                return ToolBrokerDecision("approval_required", f"{required} approval required")
        plugin_id, _ = registered
        result = self.plugins.tool_plugin(plugin_id).invoke(
            ToolInvocation(name, operation, workspace_root, dict(arguments))
        )
        self.audit.append(
            "kernel:tool_broker",
            "tool_completed",
            {
                "agentId": agent_id,
                "tool": name,
                "operation": operation,
                "path": relative,
                "metadata": dict(result.metadata),
            },
        )
        return ToolBrokerDecision("completed", "authorized", result)

    def _deny(self, agent_id: str, name: str, operation: str, reason: str) -> ToolBrokerDecision:
        self.audit.append(
            "kernel:tool_broker",
            "tool_denied",
            {"agentId": agent_id, "tool": name, "operation": operation, "reason": reason},
        )
        return ToolBrokerDecision("denied", reason)
