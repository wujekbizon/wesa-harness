from __future__ import annotations

import importlib
import json
from dataclasses import dataclass
from pathlib import Path
from typing import Any

from .contracts import (
    DocumentExtractorPlugin,
    EmbeddingProviderPlugin,
    ModelProviderPlugin,
    Plugin,
    PluginError,
    PluginHealth,
    PluginManifest,
    RetrievalPlugin,
    SchedulerPlugin,
    SandboxPlugin,
    ToolPlugin,
    WorkflowExtensionPlugin,
)


@dataclass(slots=True)
class PluginSlot:
    manifest: PluginManifest
    instance: Plugin | None = None
    error: str | None = None
    enabled: bool = True

    def health(self) -> PluginHealth:
        if not self.enabled:
            return PluginHealth("disabled")
        if self.error:
            return PluginHealth("failed", self.error)
        if self.instance is None:
            return PluginHealth("stopped")
        try:
            return self.instance.health()
        except Exception as exc:
            return PluginHealth("failed", str(exc))


class PluginRegistry:
    def __init__(self, plugin_root: Path, state_path: Path | None = None):
        self.plugin_root = plugin_root
        self.state_path = state_path
        self._slots: dict[str, PluginSlot] = {}

    def discover(self) -> tuple[PluginManifest, ...]:
        manifests: list[PluginManifest] = []
        for path in sorted(self.plugin_root.glob("*/plugin.json")):
            manifest = PluginManifest.from_file(path)
            if manifest.plugin_id in {item.plugin_id for item in manifests}:
                raise PluginError(f"duplicate plugin id: {manifest.plugin_id}")
            manifests.append(manifest)
        return tuple(manifests)

    def load_all(self) -> None:
        desired = self._read_state()
        for manifest in self.discover():
            slot = PluginSlot(manifest, enabled=desired.get(manifest.plugin_id, True))
            self._slots[manifest.plugin_id] = slot
            if not slot.enabled:
                continue
            self._start(slot)

    @staticmethod
    def _start(slot: PluginSlot) -> None:
        manifest = slot.manifest
        slot.error = None
        try:
            module_name, factory_name = manifest.entrypoint.split(":", 1)
            factory: Any = getattr(importlib.import_module(module_name), factory_name)
            instance = factory(manifest)
            if getattr(instance, "plugin_id", None) != manifest.plugin_id:
                raise PluginError("plugin instance id does not match manifest")
            instance.start()
            slot.instance = instance
        except Exception as exc:
            slot.instance = None
            slot.error = f"{type(exc).__name__}: {exc}"

    def set_enabled(self, plugin_id: str, enabled: bool) -> PluginHealth:
        slot = self._slots.get(plugin_id)
        if slot is None:
            raise PluginError(f"plugin not found: {plugin_id}")
        if enabled == slot.enabled:
            return slot.health()
        if not enabled:
            if slot.instance is not None:
                slot.instance.stop()
            slot.instance = None
            slot.error = None
            slot.enabled = False
        else:
            slot.enabled = True
            self._start(slot)
        self._write_state()
        return slot.health()

    def _read_state(self) -> dict[str, bool]:
        if self.state_path is None or not self.state_path.exists():
            return {}
        try:
            value = json.loads(self.state_path.read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError) as exc:
            raise PluginError(f"invalid plugin state: {exc}") from exc
        if not isinstance(value, dict) or any(not isinstance(key, str) or not isinstance(flag, bool) for key, flag in value.items()):
            raise PluginError("plugin state must be an object of boolean flags")
        return value

    def _write_state(self) -> None:
        if self.state_path is None:
            return
        self.state_path.parent.mkdir(parents=True, exist_ok=True)
        temporary = self.state_path.with_suffix(".tmp")
        temporary.write_text(
            json.dumps({key: slot.enabled for key, slot in sorted(self._slots.items())}, indent=2) + "\n",
            encoding="utf-8",
        )
        temporary.replace(self.state_path)

    def model_provider(self, plugin_id: str) -> ModelProviderPlugin:
        slot = self._slots.get(plugin_id)
        if slot is None:
            raise PluginError(f"plugin not found: {plugin_id}")
        if slot.error or slot.instance is None:
            raise PluginError(slot.error or f"plugin not running: {plugin_id}")
        if slot.manifest.kind != "model-provider":
            raise PluginError(f"plugin is not a model provider: {plugin_id}")
        return slot.instance  # type: ignore[return-value]

    def tool_plugin(self, plugin_id: str) -> ToolPlugin:
        slot = self._slots.get(plugin_id)
        if slot is None:
            raise PluginError(f"plugin not found: {plugin_id}")
        if slot.error or slot.instance is None:
            raise PluginError(slot.error or f"plugin not running: {plugin_id}")
        if slot.manifest.kind != "tool":
            raise PluginError(f"plugin is not a tool provider: {plugin_id}")
        return slot.instance  # type: ignore[return-value]

    def embedding_provider(self, plugin_id: str) -> EmbeddingProviderPlugin:
        return self._typed(plugin_id, "embedding-provider")  # type: ignore[return-value]

    def retrieval_plugin(self, plugin_id: str) -> RetrievalPlugin:
        return self._typed(plugin_id, "retrieval")  # type: ignore[return-value]

    def document_extractor(self, plugin_id: str) -> DocumentExtractorPlugin:
        return self._typed(plugin_id, "document-extractor")  # type: ignore[return-value]

    def scheduler_plugin(self, plugin_id: str) -> SchedulerPlugin:
        return self._typed(plugin_id, "scheduler")  # type: ignore[return-value]

    def workflow_extension(self, plugin_id: str) -> WorkflowExtensionPlugin:
        return self._typed(plugin_id, "pipeline")  # type: ignore[return-value]

    def sandbox_plugin(self, plugin_id: str) -> SandboxPlugin:
        slot = self._slots.get(plugin_id)
        if slot is None:
            raise PluginError(f"plugin not found: {plugin_id}")
        if slot.error or slot.instance is None:
            raise PluginError(slot.error or f"plugin not running: {plugin_id}")
        if slot.manifest.kind != "sandbox":
            raise PluginError(f"plugin is not a sandbox provider: {plugin_id}")
        return slot.instance  # type: ignore[return-value]

    def _typed(self, plugin_id: str, kind: str) -> Plugin:
        slot = self._slots.get(plugin_id)
        if slot is None:
            raise PluginError(f"plugin not found: {plugin_id}")
        if slot.error or slot.instance is None:
            raise PluginError(slot.error or f"plugin not running: {plugin_id}")
        if slot.manifest.kind != kind:
            raise PluginError(f"plugin is not a {kind}: {plugin_id}")
        return slot.instance

    def manifests(self) -> tuple[PluginManifest, ...]:
        return tuple(slot.manifest for _, slot in sorted(self._slots.items()))

    def statuses(self) -> dict[str, PluginHealth]:
        return {plugin_id: slot.health() for plugin_id, slot in sorted(self._slots.items())}

    def stop_all(self) -> None:
        for slot in reversed(tuple(self._slots.values())):
            if slot.instance is not None:
                try:
                    slot.instance.stop()
                except Exception:
                    pass
