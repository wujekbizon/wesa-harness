from __future__ import annotations

import hashlib
import json
import re
import unicodedata
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Mapping
from uuid import UUID

import pymupdf

from .contracts import PluginError, PluginHealth, PluginManifest


VISUAL_REFERENCE = re.compile(
    r"\b(?:tabel[aiąęe]|tablic[ayąę]|rycin[ayąę]|rysunk(?:u|iem|i|ach)|ilustracj[aiąę])\b",
    re.IGNORECASE,
)
TOC_LINE = re.compile(r"\.{4,}\s*\d+\s*$")
SKIP_WORDS = (
    "spis treści",
    "bibliografia",
    "piśmiennictwo",
    "indeks",
    "skorowidz",
    "isbn",
    "wydawnictwo",
)


class WolfmedContentPlugin:
    def __init__(self, manifest: PluginManifest):
        self.manifest = manifest
        self._started = False
        self.max_source_mb = int(manifest.config.get("maxSourceMb", 100))
        self.max_chunks = int(manifest.config.get("maxChunks", 400))

    @property
    def plugin_id(self) -> str:
        return self.manifest.plugin_id

    def start(self) -> None:
        self._started = True

    def stop(self) -> None:
        self._started = False

    def health(self) -> PluginHealth:
        return PluginHealth("healthy", "deterministic PDF/TXT preparation and release") if self._started else PluginHealth("stopped")

    def execute_stage(self, stage_type: str, inputs: Mapping[str, Any]) -> Mapping[str, Any]:
        if not self._started:
            raise PluginError("Wolfmed content plugin is stopped")
        if stage_type == "prepare":
            return self._prepare(inputs)
        if stage_type == "release":
            return self._release(inputs)
        raise PluginError(f"unsupported Wolfmed content operation: {stage_type}")

    def _prepare(self, inputs: Mapping[str, Any]) -> Mapping[str, Any]:
        root = Path(self._required(inputs, "workspaceRoot")).resolve()
        relative_source = self._relative_path(self._required(inputs, "sourcePath"))
        source = (root / relative_source).resolve()
        try:
            source.relative_to(root)
        except ValueError as exc:
            raise PluginError("Wolfmed source escapes workspace") from exc
        if not source.is_file() or source.suffix.lower() not in {".pdf", ".txt"}:
            raise PluginError("Wolfmed source must be an existing PDF or TXT")
        if source.stat().st_size > self.max_source_mb * 1024 * 1024:
            raise PluginError("Wolfmed source exceeds size limit")
        category = self._required(inputs, "category").strip().lower()
        if not re.fullmatch(r"[a-z0-9-]{2,100}", category):
            raise PluginError("invalid Wolfmed category")
        chunk_pages = self._bounded_int(inputs.get("chunkPages", 10), 1, 30, "chunkPages")
        skip_start = self._bounded_int(inputs.get("skipStart", 0), 0, 10_000, "skipStart")
        skip_end = self._bounded_int(inputs.get("skipEnd", 0), 0, 10_000, "skipEnd")
        auto_trim = bool(inputs.get("autoTrim", False))
        book = self._slug(source.stem)

        if source.suffix.lower() == ".txt":
            text = source.read_text(encoding="utf-8")
            chunks = [self._chunk(book, "text", text)] if text.strip() else []
            total_pages = None
            content_start = None
            content_end = None
        else:
            document = pymupdf.open(source)
            try:
                total_pages = document.page_count
                start = skip_start
                end = total_pages - skip_end
                if start >= end:
                    raise PluginError("PDF trimming leaves no pages")
                if auto_trim:
                    start, end = self._trim(document, start, end)
                chunks = []
                for index in range(start, end, chunk_pages):
                    chunk_end = min(index + chunk_pages, end)
                    text = "\n".join(document[page].get_text() or "" for page in range(index, chunk_end))
                    page_range = f"{index + 1}-{chunk_end}"
                    chunks.append(self._chunk(book, page_range, text))
                content_start = start + 1
                content_end = end
            finally:
                document.close()
        if len(chunks) > self.max_chunks:
            raise PluginError("Wolfmed source creates too many chunks")
        return {
            "schemaVersion": 1,
            "sourcePath": relative_source.as_posix(),
            "sourceSha256": self._file_hash(source),
            "book": book,
            "category": category,
            "totalPages": total_pages,
            "contentStartPage": content_start,
            "contentEndPage": content_end,
            "chunkCount": len(chunks),
            "chunks": chunks,
        }

    def _release(self, inputs: Mapping[str, Any]) -> Mapping[str, Any]:
        category = self._required(inputs, "category").strip().lower()
        raw_batches = inputs.get("validatedBatches")
        if not isinstance(raw_batches, list):
            raise PluginError("validatedBatches must be an array")
        questions: list[dict[str, Any]] = []
        seen: set[str] = set()
        dropped_invalid = 0
        dropped_duplicate = 0
        dropped_visual = 0
        for raw in raw_batches:
            batch = self._question_array(raw)
            for value in batch:
                normalized = self._normalize_question(value, category)
                if normalized is None:
                    dropped_invalid += 1
                    continue
                question_key = normalized["data"]["question"].strip().casefold()
                if VISUAL_REFERENCE.search(question_key):
                    dropped_visual += 1
                    continue
                if question_key in seen:
                    dropped_duplicate += 1
                    continue
                seen.add(question_key)
                normalized["id"] = self._stable_uuid(category, question_key)
                questions.append(normalized)
        questions.sort(key=lambda item: item["data"]["question"].casefold())
        encoded = (json.dumps(questions, ensure_ascii=False, indent=2) + "\n").encode("utf-8")
        return {
            "schemaVersion": 1,
            "category": category,
            "course": "opiekun-medyczny" if category == "opiekun-medyczny" else "pielegniarstwo",
            "createdAt": datetime.now(timezone.utc).isoformat(),
            "questionCount": len(questions),
            "droppedInvalid": dropped_invalid,
            "droppedDuplicate": dropped_duplicate,
            "droppedVisualReference": dropped_visual,
            "outputSha256": hashlib.sha256(encoded).hexdigest(),
            "questions": questions,
        }

    @staticmethod
    def _question_array(raw: Any) -> list[Any]:
        if isinstance(raw, str):
            try:
                raw = json.loads(raw)
            except json.JSONDecodeError as exc:
                raise PluginError(f"validated question batch is invalid JSON: {exc}") from exc
        if not isinstance(raw, list):
            raise PluginError("validated question batch must be an array")
        return raw

    @staticmethod
    def _normalize_question(value: Any, category: str) -> dict[str, Any] | None:
        if not isinstance(value, Mapping):
            return None
        data = value.get("data")
        if not isinstance(data, Mapping):
            return None
        question = data.get("question")
        answers = data.get("answers")
        if not isinstance(question, str) or len(question.strip()) < 5 or not isinstance(answers, list) or len(answers) != 4:
            return None
        normalized_answers: list[dict[str, Any]] = []
        for answer in answers:
            if not isinstance(answer, Mapping) or not isinstance(answer.get("option"), str) or not answer["option"].strip():
                return None
            normalized_answers.append({"option": answer["option"].strip(), "isCorrect": answer.get("isCorrect") is True})
        if sum(answer["isCorrect"] for answer in normalized_answers) != 1:
            return None
        result = dict(value)
        result["meta"] = {
            "course": "opiekun-medyczny" if category == "opiekun-medyczny" else "pielegniarstwo",
            "category": category,
        }
        result["data"] = {"question": " ".join(question.split()), "answers": normalized_answers}
        result["createdAt"] = value.get("createdAt") or datetime.now(timezone.utc).strftime("%Y-%m-%d %H:%M:%S.%f")
        result["updatedAt"] = value.get("updatedAt")
        return result

    @staticmethod
    def _stable_uuid(category: str, question_key: str) -> str:
        digest = hashlib.sha256(f"{category}\0{question_key}".encode("utf-8")).digest()
        return str(UUID(bytes=digest[:16], version=4))

    @staticmethod
    def _chunk(book: str, pages: str, text: str) -> dict[str, Any]:
        content = text.strip()
        identifier = f"{book}_page_{pages}" if pages != "text" else book
        return {
            "id": identifier,
            "pages": pages,
            "text": content,
            "charCount": len(content),
            "sha256": hashlib.sha256(content.encode("utf-8")).hexdigest(),
            "quality": {
                "replacementRatio": round(content.count("�") / len(content), 5) if content else 0.0,
                "usable": len(content) >= 80,
            },
        }

    @staticmethod
    def _trim(document: pymupdf.Document, start: int, end: int) -> tuple[int, int]:
        for page in range(start, min(start + 20, end)):
            if WolfmedContentPlugin._is_content(document[page].get_text() or ""):
                start = page
                break
        for page in range(end - 1, max(start - 1, end - 21), -1):
            if WolfmedContentPlugin._is_content(document[page].get_text() or ""):
                end = page + 1
                break
        return start, end

    @staticmethod
    def _is_content(text: str) -> bool:
        stripped = text.strip()
        lines = [line for line in stripped.splitlines() if line.strip()]
        short_ratio = sum(len(line.strip()) < 35 for line in lines) / len(lines) if lines else 1.0
        lowered = stripped.casefold()
        if len(stripped) < 150 or sum(bool(TOC_LINE.search(line)) for line in lines) >= 3:
            return False
        if short_ratio > 0.72 and len(stripped) < 1800:
            return False
        return sum(word in lowered for word in SKIP_WORDS) < 2

    @staticmethod
    def _slug(value: str) -> str:
        ascii_value = unicodedata.normalize("NFKD", value).encode("ascii", "ignore").decode().lower()
        slug = re.sub(r"[^a-z0-9_-]", "", ascii_value.replace(" ", "_"))
        return slug or "source"

    @staticmethod
    def _relative_path(value: str) -> Path:
        path = Path(value.replace("\\", "/"))
        if path.is_absolute() or ".." in path.parts:
            raise PluginError("sourcePath must be workspace-relative")
        return path

    @staticmethod
    def _file_hash(path: Path) -> str:
        digest = hashlib.sha256()
        with path.open("rb") as handle:
            for block in iter(lambda: handle.read(1024 * 1024), b""):
                digest.update(block)
        return digest.hexdigest()

    @staticmethod
    def _required(inputs: Mapping[str, Any], key: str) -> str:
        value = inputs.get(key)
        if not isinstance(value, str) or not value.strip():
            raise PluginError(f"{key} is required")
        return value

    @staticmethod
    def _bounded_int(value: Any, minimum: int, maximum: int, name: str) -> int:
        if not isinstance(value, int) or not minimum <= value <= maximum:
            raise PluginError(f"{name} must be {minimum}..{maximum}")
        return value


def create_plugin(manifest: PluginManifest) -> WolfmedContentPlugin:
    return WolfmedContentPlugin(manifest)
