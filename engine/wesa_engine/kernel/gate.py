from __future__ import annotations

import ast
import importlib.util
import json
import re
from pathlib import Path
from typing import Iterable

import yaml

from .capabilities import AgentCapabilityManifest
from .contracts import Artifact, GateDecision, GateOutcome, TaskEnvelopeV2
from .protected import ProtectedRegistry


class PathDenied(ValueError):
    pass


_GENERIC_SECRETS = (
    re.compile(r"AIza[0-9A-Za-z_-]{20,}"),
    re.compile(r"gh[pousr]_[0-9A-Za-z]{20,}"),
    re.compile(r"sk-[0-9A-Za-z_-]{20,}"),
    re.compile(r"postgres(?:ql)?://[^\s\"']+:[^\s\"']+@", re.IGNORECASE),
)
_QUOTED_ASSIGNMENT_SECRET = re.compile(
    r"(?i)(password|token|api[_-]?key)(\s*[:=]\s*)([\"'])(.*?)(\3)"
)
_PLAIN_ASSIGNMENT_SECRET = re.compile(
    r"(?i)(password|token|api[_-]?key)(\s*[:=]\s*)([^\s,;]+)"
)


def resolve_task_output(envelope: TaskEnvelopeV2, manifest: AgentCapabilityManifest) -> tuple[Path, str] | None:
    if envelope.output_file is None:
        return None
    raw = Path(envelope.output_file)
    if raw.is_absolute() or envelope.has_lexical_path_escape():
        raise PathDenied("output path must be relative and cannot contain parent traversal")
    root = Path(envelope.workspace_root).resolve(strict=False)
    target = (root / raw).resolve(strict=False)
    try:
        relative = target.relative_to(root).as_posix()
    except ValueError as exc:
        raise PathDenied("output path escapes workspace root") from exc
    if not manifest.allows_write_path(relative):
        raise PathDenied("agent capability manifest denies output path")
    return target, relative


def validate_task_path(
    envelope: TaskEnvelopeV2,
    manifest: AgentCapabilityManifest,
    protected: ProtectedRegistry,
) -> str | None:
    resolved = resolve_task_output(envelope, manifest)
    if resolved is None:
        return None
    _, relative = resolved
    if protected.is_protected(relative):
        raise PathDenied("output path is protected")
    return relative


def redact_text(content: str, known_values: Iterable[str] = ()) -> tuple[str, bool]:
    changed = False
    for value in known_values:
        if value and value in content:
            content = content.replace(value, "[REDACTED:KNOWN_SECRET]")
            changed = True
    for pattern in _GENERIC_SECRETS:
        content, count = pattern.subn("[REDACTED:CREDENTIAL]", content)
        changed = changed or count > 0
    content, quoted_count = _QUOTED_ASSIGNMENT_SECRET.subn(
        lambda match: f'{match.group(1)}{match.group(2)}{match.group(3)}[REDACTED:CREDENTIAL]{match.group(3)}',
        content,
    )
    content, plain_count = _PLAIN_ASSIGNMENT_SECRET.subn(
        lambda match: f'{match.group(1)}{match.group(2)}"[REDACTED:CREDENTIAL]"',
        content,
    )
    changed = changed or quoted_count > 0 or plain_count > 0
    return content, changed


def _python_findings(content: str, network: str) -> tuple[list[str], list[str]]:
    review: list[str] = []
    quarantine: list[str] = []
    try:
        tree = ast.parse(content)
    except SyntaxError as exc:
        return review, [f"python syntax error: {exc.msg}"]

    for node in ast.walk(tree):
        if isinstance(node, ast.NamedExpr):
            review.append("walrus operator requires review")
        if isinstance(node, ast.Call):
            name = ""
            if isinstance(node.func, ast.Name):
                name = node.func.id
            elif isinstance(node.func, ast.Attribute):
                name = node.func.attr
            if name in {"eval", "exec", "system"}:
                quarantine.append(f"dynamic execution call: {name}")
            if name in {"run", "Popen", "call"}:
                for keyword in node.keywords:
                    if keyword.arg == "shell" and isinstance(keyword.value, ast.Constant) and keyword.value.value is True:
                        quarantine.append("subprocess shell=True")
        if isinstance(node, (ast.Import, ast.ImportFrom)):
            names = [alias.name for alias in node.names] if isinstance(node, ast.Import) else [node.module or ""]
            for module in names:
                top = module.split(".", 1)[0]
                if top and importlib.util.find_spec(top) is None:
                    review.append(f"unresolved import: {top}")
                if network == "none" and top in {"requests", "httpx", "urllib3", "aiohttp", "socket"}:
                    review.append(f"network import denied by manifest: {top}")
    return review, quarantine


def _format_findings(content: str, media_type: str, suffix: str, network: str) -> tuple[list[str], list[str]]:
    review: list[str] = []
    quarantine: list[str] = []
    if media_type in {"application/json"} or suffix == ".json":
        try:
            json.loads(content)
        except json.JSONDecodeError as exc:
            quarantine.append(f"invalid JSON: {exc.msg}")
    elif media_type in {"application/yaml", "text/yaml"} or suffix in {".yaml", ".yml"}:
        try:
            yaml.safe_load(content)
        except yaml.YAMLError:
            quarantine.append("invalid YAML")
    elif media_type in {"text/x-python", "application/x-python"} or suffix == ".py":
        py_review, py_quarantine = _python_findings(content, network)
        review.extend(py_review)
        quarantine.extend(py_quarantine)
    elif suffix in {".js", ".ts", ".tsx", ".jsx"}:
        if re.search(r"\beval\s*\(|child_process|\bexecSync\s*\(", content):
            quarantine.append("dynamic JavaScript execution")
        if network == "none" and re.search(r"\bfetch\s*\(|axios\.", content):
            review.append("network call denied by manifest")
        review.append("JavaScript/TypeScript requires compiler validation")
    return review, quarantine


class ArtifactGate:
    def __init__(self, protected: ProtectedRegistry, known_secrets: Iterable[str] = ()):
        self.protected = protected
        self.known_secrets = tuple(value for value in known_secrets if value)

    def evaluate(
        self,
        envelope: TaskEnvelopeV2,
        artifact: Artifact,
        manifest: AgentCapabilityManifest,
    ) -> GateDecision:
        if len(artifact.content) > manifest.max_artifact_kb * 1024:
            return GateDecision(GateOutcome.QUARANTINE, ("artifact exceeds capability size limit",))
        try:
            relative = validate_task_path(envelope, manifest, self.protected)
        except PathDenied as exc:
            return GateDecision(GateOutcome.QUARANTINE, (str(exc),))

        try:
            text = artifact.content.decode("utf-8")
        except UnicodeDecodeError:
            return GateDecision(GateOutcome.QUARANTINE, ("artifact is not valid UTF-8",))
        if not text.strip():
            return GateDecision(GateOutcome.QUARANTINE, ("artifact is empty",))
        if "\x00" in text:
            return GateDecision(GateOutcome.QUARANTINE, ("artifact contains NUL bytes",))

        redacted, secret_found = redact_text(text, self.known_secrets)
        requested_target = (artifact.requested_path or "").replace("\\", "/")
        if requested_target and self.protected.is_protected(requested_target):
            if artifact.media_type != "text/x-diff" and not requested_target.endswith(".patch"):
                return GateDecision(
                    GateOutcome.QUARANTINE,
                    ("protected-file changes must be submitted as a patch proposal",),
                    redacted.encode("utf-8"),
                )
            reasons = ["protected-file patch requires Axel/Greg review"]
            if secret_found:
                reasons.append("credential content was redacted")
            return GateDecision(GateOutcome.PROPOSAL, tuple(reasons), redacted.encode("utf-8"))

        suffix = Path(relative or artifact.requested_path or "").suffix.lower()
        review, quarantine = _format_findings(redacted, artifact.media_type, suffix, manifest.network)
        if quarantine:
            return GateDecision(
                GateOutcome.QUARANTINE,
                tuple(quarantine + review),
                redacted.encode("utf-8"),
            )
        reasons = list(review)
        if secret_found:
            reasons.append("credential content was redacted")
        if reasons:
            return GateDecision(GateOutcome.REVIEW, tuple(reasons), redacted.encode("utf-8"))
        return GateDecision(GateOutcome.ACCEPT, (), redacted.encode("utf-8"))
