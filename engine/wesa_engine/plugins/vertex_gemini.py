from __future__ import annotations

import json
import os
import threading
import urllib.error
import urllib.parse
import urllib.request
from collections.abc import Iterator
from typing import Any, Mapping

from google.auth import default as default_credentials
from google.auth.credentials import Credentials
from google.auth.transport.requests import Request as AuthRequest

from .contracts import ChatRequest, ModelInfo, ModelResponse, PluginError, PluginHealth, PluginManifest


SCOPES = ("https://www.googleapis.com/auth/cloud-platform",)


class VertexGeminiProviderPlugin:
    def __init__(self, manifest: PluginManifest):
        self.manifest = manifest
        config = manifest.config
        self.project_env = str(config.get("projectEnvironmentVariable", "GOOGLE_CLOUD_PROJECT"))
        self.location_env = str(config.get("locationEnvironmentVariable", "GOOGLE_CLOUD_LOCATION"))
        self.models_env = str(config.get("modelsEnvironmentVariable", "WESA_VERTEX_MODELS"))
        self.default_location = str(config.get("defaultLocation", "global"))
        configured = config.get("defaultModels", ["gemini-2.5-flash"])
        if not isinstance(configured, list) or any(not isinstance(item, str) for item in configured):
            raise PluginError("Vertex defaultModels must be an array of strings")
        self.default_models = tuple(configured)
        self.timeout = float(config.get("timeoutSeconds", 300))
        self._started = False
        self._credential_lock = threading.Lock()
        self._credentials: Credentials | None = None

    @property
    def plugin_id(self) -> str:
        return self.manifest.plugin_id

    @property
    def project(self) -> str:
        return os.getenv(self.project_env, "").strip()

    @property
    def location(self) -> str:
        return os.getenv(self.location_env, self.default_location).strip() or self.default_location

    def start(self) -> None:
        self._started = True

    def stop(self) -> None:
        self._started = False

    def health(self) -> PluginHealth:
        if not self._started:
            return PluginHealth("stopped")
        if not self.project:
            return PluginHealth("disabled", f"{self.project_env} is not configured")
        try:
            self._get_credentials(refresh=False)
            return PluginHealth("healthy", f"project={self.project}; location={self.location}; auth=ADC")
        except Exception as exc:
            return PluginHealth("unhealthy", f"ADC unavailable: {exc}")

    def list_models(self) -> tuple[ModelInfo, ...]:
        configured = os.getenv(self.models_env, "").strip()
        names = tuple(item.strip() for item in configured.split(",") if item.strip()) or self.default_models
        return tuple(
            ModelInfo(
                name=name,
                size=0,
                modified_at="",
                family="gemini",
                capabilities=("completion", "vision", "pdf"),
            )
            for name in names
        )

    def stream_chat(self, request: ChatRequest) -> Iterator[str]:
        yield self.generate(request).content

    def generate(self, request: ChatRequest) -> ModelResponse:
        if not self._started:
            raise PluginError("Vertex Gemini plugin is stopped")
        if not self.project:
            raise PluginError(f"Vertex Gemini is disabled: {self.project_env} is not configured")
        if request.model not in {model.name for model in self.list_models()}:
            raise PluginError(f"Vertex model is not configured: {request.model}")

        generation: dict[str, Any] = {}
        if "temperature" in request.options:
            generation["temperature"] = float(request.options["temperature"])
        if "num_predict" in request.options:
            generation["maxOutputTokens"] = int(request.options["num_predict"])
        if isinstance(request.response_format, Mapping):
            generation["responseMimeType"] = "application/json"
            generation["responseSchema"] = request.response_format
        elif request.response_format == "json":
            generation["responseMimeType"] = "application/json"
        body = {
            "systemInstruction": {"parts": [{"text": request.system_prompt}]},
            "contents": [{"role": "user", "parts": [{"text": request.user_prompt}]}],
            "generationConfig": generation,
        }
        value = self._post(request.model, body)
        candidates = value.get("candidates")
        if not isinstance(candidates, list) or not candidates:
            raise PluginError("Vertex Gemini returned no candidates")
        content = candidates[0].get("content") if isinstance(candidates[0], Mapping) else None
        parts = content.get("parts") if isinstance(content, Mapping) else None
        text = "".join(
            str(part.get("text", ""))
            for part in parts or []
            if isinstance(part, Mapping) and part.get("text")
        ).strip()
        if not text:
            raise PluginError("Vertex Gemini returned no final content")
        usage = value.get("usageMetadata") if isinstance(value.get("usageMetadata"), Mapping) else {}
        return ModelResponse(
            request.model,
            text,
            prompt_tokens=self._integer(usage.get("promptTokenCount")),
            completion_tokens=self._integer(usage.get("candidatesTokenCount")),
        )

    def _post(self, model: str, body: Mapping[str, Any]) -> Mapping[str, Any]:
        token = self._get_credentials(refresh=True).token
        host = "aiplatform.googleapis.com" if self.location == "global" else f"{self.location}-aiplatform.googleapis.com"
        model_path = urllib.parse.quote(model, safe="-_.")
        url = (
            f"https://{host}/v1/projects/{urllib.parse.quote(self.project, safe='-_')}/"
            f"locations/{urllib.parse.quote(self.location, safe='-_')}/publishers/google/models/"
            f"{model_path}:generateContent"
        )
        request = urllib.request.Request(
            url,
            data=json.dumps(body).encode("utf-8"),
            headers={"Authorization": f"Bearer {token}", "Content-Type": "application/json"},
            method="POST",
        )
        try:
            with urllib.request.urlopen(request, timeout=self.timeout) as response:
                value = json.loads(response.read())
        except urllib.error.HTTPError as exc:
            detail = exc.read().decode("utf-8", errors="replace")[:1000]
            raise PluginError(f"Vertex Gemini HTTP {exc.code}: {detail}") from exc
        except (urllib.error.URLError, TimeoutError, json.JSONDecodeError) as exc:
            raise PluginError(f"Vertex Gemini request failed: {exc}") from exc
        if not isinstance(value, Mapping):
            raise PluginError("Vertex Gemini returned a non-object response")
        return value

    def _get_credentials(self, refresh: bool) -> Credentials:
        with self._credential_lock:
            if self._credentials is None:
                credentials, detected_project = default_credentials(scopes=SCOPES)
                self._credentials = credentials
                if not self.project and detected_project:
                    os.environ[self.project_env] = detected_project
            if refresh and (not self._credentials.valid or not self._credentials.token):
                self._credentials.refresh(AuthRequest())
            return self._credentials

    @staticmethod
    def _integer(value: Any) -> int | None:
        return int(value) if isinstance(value, (int, float)) else None


def create_plugin(manifest: PluginManifest) -> VertexGeminiProviderPlugin:
    return VertexGeminiProviderPlugin(manifest)
