from __future__ import annotations

import json
import os
import threading
import time
import urllib.error
import urllib.parse
import urllib.request
from collections.abc import Iterator
from typing import Any, Mapping

from .contracts import ChatRequest, ModelInfo, ModelResponse, PluginError, PluginHealth, PluginManifest


class CircuitOpen(PluginError):
    pass


class CircuitBreaker:
    def __init__(self, failure_threshold: int, reset_seconds: float):
        self.failure_threshold = failure_threshold
        self.reset_seconds = reset_seconds
        self.failures = 0
        self.opened_at: float | None = None
        self._lock = threading.Lock()

    def before_call(self) -> None:
        with self._lock:
            if self.opened_at is None:
                return
            if time.monotonic() - self.opened_at >= self.reset_seconds:
                self.failures = 0
                self.opened_at = None
                return
            raise CircuitOpen("Ollama circuit is open")

    def success(self) -> None:
        with self._lock:
            self.failures = 0
            self.opened_at = None

    def failure(self) -> None:
        with self._lock:
            self.failures += 1
            if self.failures >= self.failure_threshold:
                self.opened_at = time.monotonic()


class OllamaProviderPlugin:
    def __init__(self, manifest: PluginManifest):
        self.manifest = manifest
        config = manifest.config
        env_name = str(config.get("endpointEnvironmentVariable", "WESA_OLLAMA_URL"))
        endpoint = os.getenv(env_name, str(config.get("defaultEndpoint", "http://127.0.0.1:11434")))
        self.endpoint = self._normalize_endpoint(endpoint)
        self.timeout = float(config.get("timeoutSeconds", 300))
        self.max_retries = int(config.get("maxRetries", 2))
        self._semaphore = threading.BoundedSemaphore(int(config.get("maxConcurrency", 1)))
        self._breaker = CircuitBreaker(
            int(config.get("failureThreshold", 3)),
            float(config.get("resetSeconds", 30)),
        )
        self._started = False

    @property
    def plugin_id(self) -> str:
        return self.manifest.plugin_id

    @staticmethod
    def _normalize_endpoint(value: str) -> str:
        if "://" not in value:
            value = f"http://{value}"
        parsed = urllib.parse.urlparse(value)
        if parsed.scheme not in {"http", "https"} or not parsed.hostname:
            raise PluginError("Ollama endpoint must be HTTP(S)")
        host = "127.0.0.1" if parsed.hostname == "0.0.0.0" else parsed.hostname
        port = parsed.port or 11434
        return f"{parsed.scheme}://{host}:{port}"

    def start(self) -> None:
        self._started = True

    def stop(self) -> None:
        self._started = False

    def health(self) -> PluginHealth:
        if not self._started:
            return PluginHealth("stopped")
        try:
            self._json_request("GET", "/api/tags")
            return PluginHealth("healthy", self.endpoint)
        except Exception as exc:
            return PluginHealth("unhealthy", str(exc))

    def list_models(self) -> tuple[ModelInfo, ...]:
        payload = self._json_request("GET", "/api/tags")
        models: list[ModelInfo] = []
        for value in payload.get("models", []):
            if not isinstance(value, Mapping) or not isinstance(value.get("name"), str):
                continue
            details = value.get("details") if isinstance(value.get("details"), Mapping) else {}
            models.append(
                ModelInfo(
                    name=str(value["name"]),
                    size=int(value.get("size", 0)),
                    modified_at=str(value.get("modified_at", "")),
                    family=str(details.get("family")) if details.get("family") else None,
                    parameter_size=str(details.get("parameter_size")) if details.get("parameter_size") else None,
                    quantization=str(details.get("quantization_level")) if details.get("quantization_level") else None,
                    capabilities=self._model_capabilities(str(value["name"])),
                )
            )
        return tuple(sorted(models, key=lambda model: model.name.lower()))

    def _model_capabilities(self, model: str) -> tuple[str, ...]:
        try:
            value = self._json_request("POST", "/api/show", {"model": model})
            capabilities = value.get("capabilities", [])
            if isinstance(capabilities, list):
                return tuple(str(item) for item in capabilities if isinstance(item, str))
        except Exception:
            pass
        return ()

    def stream_chat(self, request: ChatRequest) -> Iterator[str]:
        if not request.model.strip():
            raise PluginError("Ollama model is required")
        body = {
            "model": request.model,
            "stream": True,
            "think": False,
            "messages": [
                {"role": "system", "content": request.system_prompt},
                {"role": "user", "content": request.user_prompt},
            ],
            "options": dict(request.options),
        }
        if request.response_format is not None:
            body["format"] = request.response_format
        self._breaker.before_call()
        with self._semaphore:
            for attempt in range(self.max_retries + 1):
                emitted = False
                thinking_emitted = False
                done_reason = "unknown"
                try:
                    self._breaker.before_call()
                    request_object = urllib.request.Request(
                        f"{self.endpoint}/api/chat",
                        data=json.dumps(body).encode("utf-8"),
                        headers={"Content-Type": "application/json"},
                        method="POST",
                    )
                    with urllib.request.urlopen(request_object, timeout=self.timeout) as response:
                        for raw_line in response:
                            if not raw_line.strip():
                                continue
                            event = json.loads(raw_line)
                            message = event.get("message", {})
                            content = message.get("content", "") if isinstance(message, Mapping) else ""
                            thinking = message.get("thinking", "") if isinstance(message, Mapping) else ""
                            thinking_emitted = thinking_emitted or bool(thinking)
                            if event.get("done"):
                                done_reason = str(event.get("done_reason", "unknown"))
                            if content:
                                emitted = True
                                yield str(content)
                    if not emitted:
                        detail = " after reasoning-only output" if thinking_emitted else ""
                        raise PluginError(
                            f"Ollama returned no final content{detail} (done_reason={done_reason})"
                        )
                    self._breaker.success()
                    return
                except Exception as exc:
                    self._breaker.failure()
                    if emitted or attempt >= self.max_retries:
                        suffix = " after partial output" if emitted else ""
                        raise PluginError(f"Ollama chat failed{suffix}: {exc}") from exc
                    time.sleep(min(2**attempt, 4))

    def generate(self, request: ChatRequest) -> ModelResponse:
        content = "".join(self.stream_chat(request))
        return ModelResponse(request.model, content)

    def _json_request(
        self,
        method: str,
        path: str,
        body: Mapping[str, Any] | None = None,
    ) -> Mapping[str, Any]:
        data = json.dumps(body).encode("utf-8") if body is not None else None
        last_error: Exception | None = None
        for attempt in range(self.max_retries + 1):
            self._breaker.before_call()
            request = urllib.request.Request(
                f"{self.endpoint}{path}",
                data=data,
                headers={"Content-Type": "application/json"} if data else {},
                method=method,
            )
            try:
                with urllib.request.urlopen(request, timeout=min(self.timeout, 15)) as response:
                    value = json.loads(response.read())
                if not isinstance(value, Mapping):
                    raise PluginError("Ollama returned a non-object response")
                self._breaker.success()
                return value
            except (urllib.error.URLError, TimeoutError, json.JSONDecodeError, PluginError) as exc:
                last_error = exc
                self._breaker.failure()
                if attempt < self.max_retries:
                    time.sleep(min(2**attempt, 4))
        raise PluginError(f"Ollama request failed: {last_error}") from last_error


def create_plugin(manifest: PluginManifest) -> OllamaProviderPlugin:
    return OllamaProviderPlugin(manifest)
