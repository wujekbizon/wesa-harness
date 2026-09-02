from __future__ import annotations

import json
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any, Iterator, Mapping, Protocol


PLUGIN_API_VERSION = "1.0"


class PluginError(RuntimeError):
    pass


@dataclass(frozen=True, slots=True)
class PluginManifest:
    plugin_id: str
    version: str
    api_version: str
    kind: str
    entrypoint: str
    trusted: bool
    capabilities: tuple[str, ...]
    config: Mapping[str, Any] = field(default_factory=dict)
    runtime_root: Path | None = None

    @classmethod
    def from_file(cls, path: Path) -> "PluginManifest":
        value = json.loads(path.read_text(encoding="utf-8"))
        if not isinstance(value, Mapping):
            raise PluginError("plugin manifest root must be an object")
        required = ("id", "version", "apiVersion", "kind", "entrypoint", "trusted", "capabilities", "config")
        missing = [key for key in required if key not in value]
        if missing:
            raise PluginError(f"plugin manifest missing: {', '.join(missing)}")
        if value["apiVersion"] != PLUGIN_API_VERSION:
            raise PluginError(f"unsupported plugin API: {value['apiVersion']}")
        if value["trusted"] is not True:
            raise PluginError("P3 loads only trusted built-in plugins")
        capabilities = value["capabilities"]
        config = value["config"]
        if not isinstance(capabilities, list) or any(not isinstance(item, str) for item in capabilities):
            raise PluginError("capabilities must be an array of strings")
        if not isinstance(config, Mapping):
            raise PluginError("config must be an object")
        return cls(
            str(value["id"]),
            str(value["version"]),
            str(value["apiVersion"]),
            str(value["kind"]),
            str(value["entrypoint"]),
            True,
            tuple(capabilities),
            dict(config),
            path.parent.parent.parent,
        )


@dataclass(frozen=True, slots=True)
class PluginHealth:
    status: str
    detail: str = ""


@dataclass(frozen=True, slots=True)
class ModelInfo:
    name: str
    size: int
    modified_at: str
    family: str | None = None
    parameter_size: str | None = None
    quantization: str | None = None
    capabilities: tuple[str, ...] = ()


@dataclass(frozen=True, slots=True)
class ChatRequest:
    model: str
    system_prompt: str
    user_prompt: str
    options: Mapping[str, Any] = field(default_factory=dict)
    response_format: str | Mapping[str, Any] | None = None


@dataclass(frozen=True, slots=True)
class ModelResponse:
    model: str
    content: str
    total_duration_ns: int | None = None
    prompt_tokens: int | None = None
    completion_tokens: int | None = None


@dataclass(frozen=True, slots=True)
class ToolDefinition:
    name: str
    operation: str
    description: str


@dataclass(frozen=True, slots=True)
class ToolInvocation:
    name: str
    operation: str
    workspace_root: str
    arguments: Mapping[str, Any]


@dataclass(frozen=True, slots=True)
class ToolResult:
    name: str
    content: str
    metadata: Mapping[str, Any] = field(default_factory=dict)


class Plugin(Protocol):
    @property
    def plugin_id(self) -> str: ...

    def start(self) -> None: ...

    def stop(self) -> None: ...

    def health(self) -> PluginHealth: ...


class ModelProviderPlugin(Plugin, Protocol):
    def list_models(self) -> tuple[ModelInfo, ...]: ...

    def stream_chat(self, request: ChatRequest) -> Iterator[str]: ...

    def generate(self, request: ChatRequest) -> ModelResponse: ...


@dataclass(frozen=True, slots=True)
class EmbeddingRequest:
    model: str
    inputs: tuple[str, ...]


@dataclass(frozen=True, slots=True)
class EmbeddingResponse:
    model: str
    vectors: tuple[tuple[float, ...], ...]
    dimensions: int


class EmbeddingProviderPlugin(Plugin, Protocol):
    def list_models(self) -> tuple[ModelInfo, ...]: ...

    def embed(self, request: EmbeddingRequest) -> EmbeddingResponse: ...


@dataclass(frozen=True, slots=True)
class RetrievedContext:
    source_id: str
    source_name: str
    content: str
    score: float | None = None


class RetrievalPlugin(Plugin, Protocol):
    def retrieve(
        self,
        collection_id: str,
        query: str,
        top_k: int,
    ) -> tuple[RetrievedContext, ...]: ...


class DocumentExtractorPlugin(Plugin, Protocol):
    def extract(self, media_type: str, content: bytes) -> str: ...


class SchedulerPlugin(Plugin, Protocol):
    def due(self, now_iso: str) -> tuple[Mapping[str, Any], ...]: ...


class WorkflowExtensionPlugin(Plugin, Protocol):
    def execute_stage(self, stage_type: str, inputs: Mapping[str, Any]) -> Mapping[str, Any]: ...


class ToolPlugin(Plugin, Protocol):
    def definitions(self) -> tuple[ToolDefinition, ...]: ...

    def invoke(self, request: ToolInvocation) -> ToolResult: ...


class SandboxPlugin(Plugin, Protocol):
    def execute(self, request: Any) -> Any: ...
