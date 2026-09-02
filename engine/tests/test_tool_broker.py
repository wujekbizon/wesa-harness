from pathlib import Path

from wesa_engine.kernel.audit import AuditLog
from wesa_engine.kernel.capabilities import AgentCapabilityManifest
from wesa_engine.kernel.protected import ProtectedRegistry
from wesa_engine.plugins.contracts import ToolDefinition
from wesa_engine.plugins.registry import PluginRegistry
from wesa_engine.tools import ToolBroker


ROOT = Path(__file__).resolve().parents[2]


def manifest(with_grant: bool = True) -> AgentCapabilityManifest:
    tools = []
    if with_grant:
        tools = [
            {
                "name": "workspace.read_text",
                "operations": ["read"],
                "scopes": ["docs/**"],
                "approval": "auto-read",
            }
        ]
    return AgentCapabilityManifest.from_mapping(
        {
            "agentId": "W1",
            "taskTypes": ["write"],
            "readPaths": ["docs/**"],
            "writePaths": ["staging/**"],
            "denyPaths": [".env*", "config/**"],
            "tools": tools,
            "network": "none",
            "execution": "none",
            "maxArtifactKb": 16,
        }
    )


def broker(tmp_path: Path) -> tuple[ToolBroker, PluginRegistry]:
    registry = PluginRegistry(ROOT / "plugins")
    registry.load_all()
    value = ToolBroker(
        registry,
        ProtectedRegistry(("config/**", ".env*"), ("config/**", ".env*")),
        AuditLog(tmp_path / "tool-audit.jsonl"),
    )
    return value, registry


def test_scoped_read_tool_completes_and_is_audited(tmp_path):
    (tmp_path / "docs").mkdir()
    (tmp_path / "docs" / "guide.md").write_text("trusted tool result", encoding="utf-8")
    value, registry = broker(tmp_path)

    decision = value.invoke(
        agent_id="W1",
        manifest=manifest(),
        name="workspace.read_text",
        operation="read",
        workspace_root=str(tmp_path),
        arguments={"path": "docs/guide.md"},
    )

    assert decision.status == "completed"
    assert decision.result is not None and decision.result.content == "trusted tool result"
    assert "trusted tool result" not in (tmp_path / "tool-audit.jsonl").read_text(encoding="utf-8")
    registry.stop_all()


def test_missing_grant_path_escape_and_protected_path_are_denied(tmp_path):
    (tmp_path / "docs").mkdir()
    (tmp_path / "docs" / "guide.md").write_text("text", encoding="utf-8")
    (tmp_path / "config").mkdir()
    (tmp_path / "config" / "secret.txt").write_text("secret", encoding="utf-8")
    value, registry = broker(tmp_path)

    no_grant = value.invoke(
        agent_id="W1",
        manifest=manifest(False),
        name="workspace.read_text",
        operation="read",
        workspace_root=str(tmp_path),
        arguments={"path": "docs/guide.md"},
    )
    escaped = value.invoke(
        agent_id="W1",
        manifest=manifest(),
        name="workspace.read_text",
        operation="read",
        workspace_root=str(tmp_path),
        arguments={"path": "../outside.txt"},
    )
    protected = value.invoke(
        agent_id="W1",
        manifest=manifest(),
        name="workspace.read_text",
        operation="read",
        workspace_root=str(tmp_path),
        arguments={"path": "config/secret.txt"},
    )

    assert no_grant.status == "denied"
    assert escaped.status == "denied"
    assert protected.status == "denied"
    registry.stop_all()


def test_mutating_operation_requires_explicit_approval_before_plugin_call(tmp_path):
    value, registry = broker(tmp_path)
    value._tools["workspace.write_text"] = (
        "workspace-tools",
        ToolDefinition("workspace.write_text", "write", "test-only mutation contract"),
    )
    write_manifest = AgentCapabilityManifest.from_mapping(
        {
            "agentId": "D1",
            "taskTypes": ["code"],
            "readPaths": ["staging/**"],
            "writePaths": ["staging/**"],
            "denyPaths": [],
            "tools": [
                {
                    "name": "workspace.write_text",
                    "operations": ["write"],
                    "scopes": ["staging/**"],
                    "approval": "axel",
                }
            ],
            "network": "none",
            "execution": "none",
            "maxArtifactKb": 16,
        }
    )

    decision = value.invoke(
        agent_id="D1",
        manifest=write_manifest,
        name="workspace.write_text",
        operation="write",
        workspace_root=str(tmp_path),
        arguments={"path": "staging/a.py"},
    )

    assert decision.status == "approval_required"
    assert decision.reason == "axel approval required"
    registry.stop_all()
