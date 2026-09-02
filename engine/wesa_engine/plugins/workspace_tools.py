from __future__ import annotations

import hashlib
from pathlib import Path

from .contracts import (
    PluginError,
    PluginHealth,
    PluginManifest,
    ToolDefinition,
    ToolInvocation,
    ToolResult,
)


class WorkspaceToolPlugin:
    def __init__(self, manifest: PluginManifest):
        self.manifest = manifest
        self.max_read_bytes = int(manifest.config.get("maxReadKb", 256)) * 1024
        self._started = False

    @property
    def plugin_id(self) -> str:
        return self.manifest.plugin_id

    def start(self) -> None:
        self._started = True

    def stop(self) -> None:
        self._started = False

    def health(self) -> PluginHealth:
        return PluginHealth("healthy" if self._started else "stopped", "kernel-local read tools")

    def definitions(self) -> tuple[ToolDefinition, ...]:
        return (
            ToolDefinition("workspace.read_text", "read", "Read one UTF-8 workspace file"),
            ToolDefinition("workspace.list_files", "read", "List files in one workspace directory"),
        )

    def invoke(self, request: ToolInvocation) -> ToolResult:
        if not self._started:
            raise PluginError("workspace tool plugin is stopped")
        root = Path(request.workspace_root).resolve(strict=False)
        relative = str(request.arguments.get("path", ""))
        target = (root / relative).resolve(strict=False)
        try:
            normalized = target.relative_to(root).as_posix()
        except ValueError as exc:
            raise PluginError("workspace tool path escapes root") from exc
        if request.name == "workspace.read_text":
            if not target.is_file():
                raise PluginError("workspace file does not exist")
            content = target.read_bytes()
            if len(content) > self.max_read_bytes:
                raise PluginError("workspace file exceeds read limit")
            try:
                text = content.decode("utf-8")
            except UnicodeDecodeError as exc:
                raise PluginError("workspace file is not UTF-8 text") from exc
            return ToolResult(
                request.name,
                text,
                {
                    "path": normalized,
                    "bytes": len(content),
                    "sha256": hashlib.sha256(content).hexdigest(),
                },
            )
        if request.name == "workspace.list_files":
            if not target.is_dir():
                raise PluginError("workspace directory does not exist")
            files = sorted(
                child.relative_to(root).as_posix()
                for child in target.iterdir()
                if child.is_file()
            )
            return ToolResult(request.name, "\n".join(files), {"path": normalized, "count": len(files)})
        raise PluginError(f"unsupported workspace tool: {request.name}")


def create_plugin(manifest: PluginManifest) -> WorkspaceToolPlugin:
    return WorkspaceToolPlugin(manifest)
