from __future__ import annotations

import json
import logging
import os
import re
import mimetypes
import uuid
import threading
import urllib.error
import urllib.parse
import urllib.request
from collections.abc import Mapping
from pathlib import Path
from typing import Any

from google.auth import default as default_credentials
from google.auth.credentials import Credentials
from google.auth.transport.requests import Request as AuthRequest

from .contracts import PluginError, PluginHealth, PluginManifest, RetrievedContext


SCOPES = ("https://www.googleapis.com/auth/cloud-platform",)
log = logging.getLogger("wesa.vertex_rag")

QUERY_FILLER = {
    "wyjaśnij", "wyjasnij", "wytłumacz", "wytlumacz", "opisz", "omów", "omow",
    "przedstaw", "wymień", "wymien", "podaj", "powiedz", "opowiedz", "czym",
    "jest", "są", "sa", "to", "co", "jak", "jaki", "jaka", "jakie", "jakich",
    "dlaczego", "kiedy", "gdzie", "czy", "proszę", "prosze", "mi", "mnie", "mój",
    "moj", "pojęcie", "pojecie", "pojęcia", "pojecia", "definicja", "definicję",
    "definicje", "znaczy", "oznacza", "polega", "dokładnie", "dokladnie", "krótko",
    "krotko", "mógłbyś", "moglbys", "możesz", "mozesz",
}


def strip_query_filler(query: str) -> str:
    words = re.sub(r"[?!.,;:]", " ", query).split()
    kept = [word for word in words if word.lower() not in QUERY_FILLER]
    return " ".join(kept) if kept else query.strip()


class VertexRagPlugin:
    """Harness-mediated Vertex RAG retrieval.

    Corpus resource names are supplied by trusted configuration only. Lumio never
    provides a Vertex resource name to this plugin.
    """

    def __init__(self, manifest: PluginManifest):
        self.manifest = manifest
        config = manifest.config
        self.project_env = str(config.get("projectEnvironmentVariable", "WESA_VERTEX_RAG_PROJECT"))
        self.location_env = str(config.get("locationEnvironmentVariable", "WESA_VERTEX_RAG_LOCATION"))
        self.corpora_env = str(config.get("corporaEnvironmentVariable", "WESA_VERTEX_RAG_CORPORA"))
        self.default_location = str(config.get("defaultLocation", "us-central1"))
        self.timeout = float(config.get("timeoutSeconds", 30))
        self.vector_distance_threshold = float(config.get("vectorDistanceThreshold", 0.34))
        if not 0 <= self.vector_distance_threshold <= 2:
            raise PluginError("Vertex RAG vector distance threshold must be between 0 and 2")
        self._credentials: Credentials | None = None
        self._credential_lock = threading.Lock()
        self._started = False

    @property
    def plugin_id(self) -> str:
        return self.manifest.plugin_id

    @property
    def project(self) -> str:
        return str(self._file_config().get("project", os.getenv(self.project_env, ""))).strip()

    @property
    def location(self) -> str:
        return str(self._file_config().get("location", os.getenv(self.location_env, self.default_location))).strip() or self.default_location

    def start(self) -> None:
        self._started = True

    def stop(self) -> None:
        self._started = False

    def health(self) -> PluginHealth:
        if not self._started:
            return PluginHealth("stopped")
        if not self.project:
            return PluginHealth("disabled", f"{self.project_env} is not configured")
        if not self._corpora():
            return PluginHealth("disabled", f"{self.corpora_env} has no approved corpus mapping")
        try:
            self._get_credentials(refresh=False)
            return PluginHealth("healthy", f"project={self.project}; location={self.location}; auth=ADC")
        except Exception as exc:
            return PluginHealth("unhealthy", f"ADC unavailable: {exc}")

    def retrieve(self, collection_id: str, query: str, top_k: int) -> tuple[RetrievedContext, ...]:
        if not self._started:
            raise PluginError("Vertex RAG plugin is stopped")
        if not query.strip():
            raise PluginError("RAG query is required")
        if not 1 <= top_k <= 50:
            raise PluginError("RAG top_k must be between 1 and 50")
        corpus = self._corpora().get(collection_id)
        if not corpus:
            raise PluginError("RAG collection is not approved")
        retrieval_query = strip_query_filler(query)
        body = {
            "vertexRagStore": {
                "ragResources": [{"ragCorpus": corpus}],
            },
            "query": {
                "text": retrieval_query,
                "ragRetrievalConfig": {
                    "topK": top_k,
                    "filter": {"vectorDistanceThreshold": self.vector_distance_threshold},
                },
            },
        }
        value = self._post(self._retrieve_url(), body)
        contexts = value.get("contexts", {}).get("contexts", [])
        if not isinstance(contexts, list):
            raise PluginError("Vertex RAG returned invalid contexts")
        result: list[RetrievedContext] = []
        for item in contexts:
            if not isinstance(item, Mapping):
                continue
            text = item.get("text")
            if not isinstance(text, str) or not text.strip():
                continue
            source_name = str(item.get("sourceDisplayName") or item.get("sourceUri") or collection_id)
            score = item.get("score")
            result.append(
                RetrievedContext(
                    source_id=str(item.get("sourceUri") or source_name),
                    source_name=source_name,
                    content=text,
                    score=float(score) if isinstance(score, (int, float)) else None,
                )
            )
        scored = [item.score for item in result if item.score is not None]
        if scored and min(scored) > self.vector_distance_threshold:
            log.info(
                "Vertex RAG corpus miss. Collection=%s BestDistance=%.3f Threshold=%.3f",
                collection_id,
                min(scored),
                self.vector_distance_threshold,
            )
            return ()
        return tuple(result)

    def upload_file(self, collection_id: str, path: str, display_name: str) -> Mapping[str, Any]:
        corpus = self._corpora().get(collection_id)
        if not corpus:
            raise PluginError("RAG collection is not approved")
        source = Path(path).resolve()
        if not source.is_file():
            raise PluginError("RAG source file does not exist")
        if source.stat().st_size > 20 * 1024 * 1024:
            raise PluginError("RAG source exceeds 20 MB")
        corpus_id = corpus.rsplit("/", 1)[-1]
        url = self._upload_url(corpus_id)
        boundary = f"wesa-{uuid.uuid4().hex}"
        media_type = mimetypes.guess_type(source.name)[0] or "application/octet-stream"
        content = source.read_bytes()
        metadata = json.dumps({"rag_file": {"display_name": display_name}}, ensure_ascii=False).encode("utf-8")
        body = b"".join(
            [
                f"--{boundary}\r\nContent-Disposition: form-data; name=\"metadata\"\r\n\r\n".encode(),
                metadata,
                f"\r\n--{boundary}\r\nContent-Disposition: form-data; name=\"file\"; filename=\"{display_name}\"\r\nContent-Type: {media_type}\r\n\r\n".encode(),
                content,
                f"\r\n--{boundary}--\r\n".encode(),
            ]
        )
        return self._post_bytes(
            url,
            body,
            {"X-Goog-Upload-Protocol": "multipart", "Content-Type": f"multipart/form-data; boundary={boundary}"},
        )

    def delete_file(self, collection_id: str, file_id: str) -> Mapping[str, Any]:
        corpus = self._corpora().get(collection_id)
        if not corpus or not file_id or "/" in file_id:
            raise PluginError("RAG file is not approved")
        corpus_id = corpus.rsplit("/", 1)[-1]
        return self._delete(self._file_url(corpus_id, file_id))

    def _corpora(self) -> dict[str, str]:
        configured = self._file_config().get("corpora")
        raw = json.dumps(configured) if isinstance(configured, dict) else os.getenv(self.corpora_env, "").strip()
        if not raw:
            return {}
        try:
            value = json.loads(raw)
        except json.JSONDecodeError as exc:
            raise PluginError(f"{self.corpora_env} is not valid JSON") from exc
        if not isinstance(value, dict) or any(not isinstance(key, str) or not isinstance(item, str) for key, item in value.items()):
            raise PluginError(f"{self.corpora_env} must map collection IDs to resource names")
        prefix = f"projects/{self.project}/locations/{self.location}/ragCorpora/"
        if any(not item.startswith(prefix) for item in value.values()):
            raise PluginError("RAG corpus mapping contains an unapproved resource")
        return value

    def _file_config(self) -> dict[str, Any]:
        path = self.manifest.runtime_root / str(self.manifest.config.get("stateConfigPath", "state/vertex-rag-config.json")) if self.manifest.runtime_root else None
        if path is None or not path.exists():
            return {}
        try:
            value = json.loads(path.read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError):
            return {}
        return value if isinstance(value, dict) else {}

    def _retrieve_url(self) -> str:
        host = f"{self.location}-aiplatform.googleapis.com"
        parent = urllib.parse.quote(f"projects/{self.project}/locations/{self.location}", safe="/-_")
        return f"https://{host}/v1beta1/{parent}:retrieveContexts"

    def _upload_url(self, corpus_id: str) -> str:
        host = f"{self.location}-aiplatform.googleapis.com"
        project = urllib.parse.quote(self.project, safe="-_")
        location = urllib.parse.quote(self.location, safe="-_")
        return f"https://{host}/upload/v1beta1/projects/{project}/locations/{location}/ragCorpora/{corpus_id}/ragFiles:upload"

    def _file_url(self, corpus_id: str, file_id: str) -> str:
        host = f"{self.location}-aiplatform.googleapis.com"
        project = urllib.parse.quote(self.project, safe="-_")
        location = urllib.parse.quote(self.location, safe="-_")
        return f"https://{host}/v1beta1/projects/{project}/locations/{location}/ragCorpora/{corpus_id}/ragFiles/{urllib.parse.quote(file_id, safe='-_')}"

    def _post(self, url: str, body: Mapping[str, Any]) -> Mapping[str, Any]:
        credentials = self._get_credentials(refresh=True)
        request = urllib.request.Request(
            url,
            data=json.dumps(body).encode("utf-8"),
            headers={"Authorization": f"Bearer {credentials.token}", "Content-Type": "application/json"},
            method="POST",
        )
        try:
            with urllib.request.urlopen(request, timeout=self.timeout) as response:
                value = json.loads(response.read())
        except urllib.error.HTTPError as exc:
            detail = exc.read().decode("utf-8", errors="replace")[:1000]
            raise PluginError(f"Vertex RAG HTTP {exc.code}: {detail}") from exc
        except (urllib.error.URLError, TimeoutError, json.JSONDecodeError) as exc:
            raise PluginError(f"Vertex RAG request failed: {exc}") from exc
        if not isinstance(value, Mapping):
            raise PluginError("Vertex RAG returned a non-object response")
        return value

    def _post_bytes(self, url: str, body: bytes, headers: Mapping[str, str]) -> Mapping[str, Any]:
        credentials = self._get_credentials(refresh=True)
        request = urllib.request.Request(
            url,
            data=body,
            headers={"Authorization": f"Bearer {credentials.token}", **headers},
            method="POST",
        )
        try:
            with urllib.request.urlopen(request, timeout=self.timeout) as response:
                value = json.loads(response.read())
        except (urllib.error.HTTPError, urllib.error.URLError, TimeoutError, json.JSONDecodeError) as exc:
            raise PluginError(f"Vertex RAG upload failed: {exc}") from exc
        if not isinstance(value, Mapping):
            raise PluginError("Vertex RAG upload returned a non-object response")
        return value

    def _delete(self, url: str) -> Mapping[str, Any]:
        credentials = self._get_credentials(refresh=True)
        request = urllib.request.Request(url, headers={"Authorization": f"Bearer {credentials.token}"}, method="DELETE")
        try:
            with urllib.request.urlopen(request, timeout=self.timeout) as response:
                payload = response.read()
                return json.loads(payload) if payload else {"deleted": True}
        except (urllib.error.HTTPError, urllib.error.URLError, TimeoutError, json.JSONDecodeError) as exc:
            raise PluginError(f"Vertex RAG delete failed: {exc}") from exc

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


def create_plugin(manifest: PluginManifest) -> VertexRagPlugin:
    return VertexRagPlugin(manifest)
