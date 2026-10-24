from __future__ import annotations

import json
import os
import time
import urllib.error
import urllib.request
from dataclasses import dataclass
from typing import Any, Mapping

from wesa_engine.kernel.audit import AuditLog
from wesa_engine.kernel.capabilities import load_manifest_directory
from wesa_engine.kernel.gate import redact_text
from wesa_engine.plugins.registry import PluginRegistry
from wesa_engine.profiles import AgentProfile


class RoutingError(RuntimeError):
    pass


@dataclass(frozen=True, slots=True)
class RouteCandidate:
    identifier: str
    provider: str
    model: str
    capabilities: tuple[str, ...]
    agent_id: str | None = None
    task_type: str | None = None

    def as_mapping(self) -> dict[str, Any]:
        return {
            "id": self.identifier,
            "provider": self.provider,
            "model": self.model,
            "capabilities": list(self.capabilities),
        }


class TypeSafeDecisionClient:
    def __init__(self) -> None:
        self.api_key = os.getenv("TYPESAFE_API_KEY", "").strip()
        self.endpoint = os.getenv("TYPESAFE_API_URL", "https://api.typesafe.ai/v1/systemone").strip()
        self.model = os.getenv("TYPESAFE_MODEL", "jev-latest").strip() or "jev-latest"
        self.timeout = float(os.getenv("TYPESAFE_TIMEOUT_SECONDS", "30"))
        self.max_retries = max(0, int(os.getenv("TYPESAFE_MAX_RETRIES", "2")))
        self.handler_confidence_floor = float(os.getenv("TYPESAFE_HANDLER_CONFIDENCE_FLOOR", "0.55"))

    def evaluate(self, state: Mapping[str, Any], questions: Mapping[str, Any]) -> tuple[Mapping[str, Any], Mapping[str, Any], int]:
        if not self.api_key:
            raise RoutingError("TYPESAFE_API_KEY is not configured")
        body = json.dumps(
            {"state": state, "model": self.model, "questions": questions},
            ensure_ascii=False,
        ).encode("utf-8")
        last_error: Exception | None = None
        started = time.monotonic()
        for attempt in range(self.max_retries + 1):
            request = urllib.request.Request(
                self.endpoint,
                data=body,
                headers={
                    "Authorization": f"Bearer {self.api_key}",
                    "Content-Type": "application/json",
                },
                method="POST",
            )
            try:
                with urllib.request.urlopen(request, timeout=self.timeout) as response:
                    value = json.loads(response.read())
                if not isinstance(value, Mapping):
                    raise RoutingError("TypeSafe returned a non-object response")
                answers = value.get("answers")
                usage = value.get("usage")
                if not isinstance(answers, Mapping):
                    raise RoutingError("TypeSafe response has no answers object")
                return answers, usage if isinstance(usage, Mapping) else {}, int((time.monotonic() - started) * 1000)
            except urllib.error.HTTPError as exc:
                detail = exc.read().decode("utf-8", errors="replace")[:500]
                last_error = RoutingError(f"TypeSafe HTTP {exc.code}: {detail}")
                if exc.code not in {429, 529} or attempt >= self.max_retries:
                    break
            except (urllib.error.URLError, TimeoutError, json.JSONDecodeError, RoutingError) as exc:
                last_error = exc
                if attempt >= self.max_retries:
                    break
            time.sleep(min(0.25 * (2**attempt), 1.0))
        raise RoutingError(str(last_error or "TypeSafe request failed")) from last_error


class ShadowModelRouter:
    def __init__(
        self,
        root,
        profiles: Mapping[str, AgentProfile],
        plugins: PluginRegistry,
        audit: AuditLog,
    ) -> None:
        self.root = root
        self.profiles = profiles
        self.plugins = plugins
        self.audit = audit
        self.client = TypeSafeDecisionClient()

    def preview(
        self,
        agent_id: str,
        task_type: str,
        prompt: str,
        current_provider: str | None = None,
        current_model: str | None = None,
        task_id: str | None = None,
    ) -> dict[str, Any]:
        if agent_id == "auto" or task_type == "auto":
            return self._preview_auto(prompt, current_provider, current_model, task_id)
        profile = self.profiles.get(agent_id)
        if profile is None:
            raise RoutingError(f"unknown agent: {agent_id}")
        if task_type not in profile.task_types:
            raise RoutingError(f"task type is not allowed for agent: {task_type}")

        manifests = load_manifest_directory(self.root / "config" / "manifests")
        manifest = manifests.get(agent_id)
        if manifest is None:
            raise RoutingError(f"agent has no capability manifest: {agent_id}")
        if not manifest.allows_task_type(task_type):
            raise RoutingError(f"capability manifest denies task type: {task_type}")

        candidates = self._candidates(profile, manifest)
        current = next(
            (
                item
                for item in candidates
                if current_provider
                and current_model
                and item.provider == current_provider
                and item.model == current_model
            ),
            next((item for item in candidates if item.provider == "ollama-provider"), None),
        )
        if current is None and candidates:
            current = candidates[0]
        if current is None:
            raise RoutingError("no authorized generation candidates are available")

        safe_prompt, input_redacted = redact_text(prompt)
        state = {
            "task": {
                "agentId": agent_id,
                "taskType": task_type,
                "prompt": safe_prompt,
                "taskId": task_id,
            },
            "currentRoute": current.as_mapping(),
            "candidates": [item.as_mapping() for item in candidates],
            "policy": {
                "mode": "shadow",
                "selectionAuthority": "Harness deterministic policy",
                "executionChanges": False,
                "inputRedacted": input_redacted,
            },
        }
        questions = self._questions(candidates)
        try:
            answers, usage, latency_ms = self.client.evaluate(state, questions)
            recommendation = self._recommendation(answers, candidates, current)
            status = "ok"
            error = None
        except RoutingError as exc:
            answers = {}
            usage = {}
            latency_ms = 0
            recommendation = {
                "provider": current.provider,
                "model": current.model,
                "candidateId": current.identifier,
                "allowed": True,
                "reason": "current explicit route fallback",
            }
            status = "fallback"
            error = str(exc)

        self.audit.append(
            "engine:routing",
            "route_shadow_preview",
            {
                "agentId": agent_id,
                "taskId": task_id,
                "taskType": task_type,
                "currentProvider": current.provider,
                "currentModel": current.model,
                "recommendedProvider": recommendation["provider"],
                "recommendedModel": recommendation["model"],
                "status": status,
                "latencyMs": latency_ms,
                "error": error,
            },
        )
        return {
            "mode": "shadow",
            "status": status,
            "decisionProvider": "typesafe/jev",
            "currentProvider": current.provider,
            "currentModel": current.model,
            "recommendedProvider": recommendation["provider"],
            "recommendedModel": recommendation["model"],
            "recommendationAllowed": recommendation["allowed"],
            "recommendationReason": recommendation["reason"],
            "candidateCount": len(candidates),
            "latencyMs": latency_ms,
            "answers": answers,
            "usage": usage,
            "fallbackReason": error,
            "recommendedAgentId": agent_id,
            "recommendedTaskType": task_type,
        }

    def _preview_auto(
        self,
        prompt: str,
        current_provider: str | None,
        current_model: str | None,
        task_id: str | None,
    ) -> dict[str, Any]:
        handlers: list[dict[str, Any]] = []
        manifests = load_manifest_directory(self.root / "config" / "manifests")
        routes: dict[str, tuple[RouteCandidate, ...]] = {}
        for agent_id, profile in sorted(self.profiles.items()):
            manifest = manifests.get(agent_id)
            if manifest is None:
                continue
            for task_type in profile.task_types:
                if not manifest.allows_task_type(task_type):
                    continue
                candidates = self._candidates(profile, manifest)
                if not candidates:
                    continue
                key = f"{agent_id}/{task_type}"
                handlers.append({
                    "id": key,
                    "agentId": agent_id,
                    "taskType": task_type,
                    "role": profile.role,
                })
                routes[key] = candidates
        if not handlers:
            raise RoutingError("no authorized agent/task handlers are available")
        safe_prompt, input_redacted = redact_text(prompt)
        state = {
            "task": {"agentId": "auto", "taskType": "auto", "prompt": safe_prompt, "taskId": task_id},
            "handlers": handlers,
            "policy": {"mode": "automatic", "selectionAuthority": "Harness validates Jev recommendation", "inputRedacted": input_redacted},
        }
        questions = self._questions((), tuple(handlers))
        try:
            answers, usage, latency_ms = self.client.evaluate(state, questions)
            handler_answer = answers.get("handler")
            handler_id = handler_answer.get("choice") if isinstance(handler_answer, Mapping) else None
            confidence = handler_answer.get("confidence") if isinstance(handler_answer, Mapping) else None
            if not isinstance(handler_id, str) or handler_id not in routes:
                raise RoutingError("Jev returned an undeclared handler")
            confidence_floor = getattr(self.client, "handler_confidence_floor", 0.55)
            if not isinstance(confidence, (int, float)) or confidence < confidence_floor:
                raise RoutingError("Jev handler selection confidence is below the automatic threshold")
            selected_handler = next(item for item in handlers if item["id"] == handler_id)
            candidates = routes[handler_id]
            current = next((item for item in candidates if item.provider == current_provider and item.model == current_model), candidates[0])
            recommendation = self._recommendation(answers, candidates, current)
            status = "ok"
            error = None
        except RoutingError as exc:
            answers, usage, latency_ms = {}, {}, 0
            selected_handler = {"agentId": "", "taskType": ""}
            recommendation = {"provider": "", "model": "", "candidateId": "", "allowed": False, "reason": "automatic selection requires manual fallback"}
            status = "fallback"
            error = str(exc)
        self.audit.append("engine:routing", "route_auto_preview", {
            "agentId": selected_handler.get("agentId"), "taskId": task_id,
            "taskType": selected_handler.get("taskType"), "recommendedProvider": recommendation["provider"],
            "recommendedModel": recommendation["model"], "status": status, "latencyMs": latency_ms, "error": error,
        })
        return {
            "mode": "automatic", "status": status, "decisionProvider": "typesafe/jev",
            "currentProvider": current.provider if status == "ok" else "", "currentModel": current.model if status == "ok" else "",
            "recommendedProvider": recommendation["provider"], "recommendedModel": recommendation["model"],
            "recommendationAllowed": recommendation["allowed"], "recommendationReason": recommendation["reason"],
            "candidateCount": len(handlers), "latencyMs": latency_ms, "answers": answers, "usage": usage,
            "fallbackReason": error, "recommendedAgentId": selected_handler.get("agentId", ""),
            "recommendedTaskType": selected_handler.get("taskType", ""),
        }

    def _candidates(self, profile: AgentProfile, manifest) -> tuple[RouteCandidate, ...]:
        candidates: list[RouteCandidate] = []
        for provider_id in profile.allowed_providers:
            if not manifest.allows_provider(provider_id):
                continue
            try:
                models = self.plugins.model_provider(provider_id).list_models()
            except Exception:
                continue
            for model in models:
                if self._is_embedding_model(model.name, model.capabilities):
                    continue
                candidates.append(
                    RouteCandidate(
                        f"{provider_id}/{model.name}",
                        provider_id,
                        model.name,
                        tuple(model.capabilities),
                    )
                )
        return tuple(sorted(candidates, key=lambda item: item.identifier.lower()))

    @staticmethod
    def _is_embedding_model(name: str, capabilities: tuple[str, ...]) -> bool:
        return "embedding" in {value.lower() for value in capabilities} or "embed" in name.lower()

    @staticmethod
    def _questions(candidates: tuple[RouteCandidate, ...], handlers: tuple[Mapping[str, Any], ...] = ()) -> dict[str, Any]:
        route_criteria = {
            item.identifier: (
                f"Provider {item.provider}; model {item.model}; "
                f"capabilities: {', '.join(item.capabilities) or 'text'}; "
                f"routing policy: {ShadowModelRouter._provider_policy(item.provider)}"
            )
            for item in candidates
        }
        questions = {
            "intent": {
                "type": "choice",
                "instructions": "What is the primary intent of `task.prompt`?",
                "criteria": {
                    "code": "Code generation or code change",
                    "research": "Research or evidence synthesis",
                    "review": "Review, critique, or validation",
                    "content": "Content or document generation",
                    "rag": "Retrieval or grounded-answer workflow",
                    "security": "Security analysis or security-sensitive action",
                    "general": "Other general task",
                    "other": "None of the listed intents",
                },
            },
            "complexity": {
                "type": "score",
                "instructions": "How much reasoning does `task.prompt` require?",
                "criteria": [
                    "Routine: clear bounded operation",
                    "Moderate: several related judgments or transformations",
                    "Complex: substantial domain reasoning or dependencies",
                    "Deep reasoning: long-horizon or highly ambiguous reasoning",
                ],
            },
            "risk": {
                "type": "score",
                "instructions": "What is the consequence of selecting the wrong route?",
                "criteria": [
                    "Low: reversible and non-sensitive",
                    "Medium: operational impact or limited sensitive context",
                    "High: security, privacy, medical, financial, or important workflow impact",
                    "Critical: irreversible, safety-sensitive, or authority-bearing action",
                ],
            },
            "sensitivity": {
                "type": "choice",
                "instructions": "What is the highest sensitivity of the state needed for this task?",
                "criteria": {
                    "public": "Public or non-sensitive information",
                    "internal": "WESA internal information",
                    "confidential": "Confidential business, code, or user information",
                    "restricted": "Highly restricted secret, regulated, or protected information",
                },
            },
            "requires_vision": {
                "type": "noul",
                "instructions": "Does the task require image or visual understanding?",
            },
            "requires_pdf": {
                "type": "noul",
                "instructions": "Does the task require PDF-native understanding rather than extracted text?",
            },
            "requires_retrieval": {
                "type": "noul",
                "instructions": "Does the task require retrieval from a configured knowledge source?",
            },
            "requires_tools": {
                "type": "noul",
                "instructions": "Does the task require authorized tool calls or external actions?",
            },
        }
        if handlers:
            questions["handler"] = {
                "type": "choice",
                "instructions": "Which authorized agent/task handler best matches the user's primary request? Choose no_match when none fits.",
                "criteria": {item["id"]: f"Agent {item['agentId']}; task {item['taskType']}; role {item['role']}" for item in handlers} | {"no_match": "No authorized handler fits"},
            }
        if candidates:
            questions["route"] = {
                "type": "choice",
                "instructions": "Which authorized candidate is the best handler for this task?",
                "criteria": route_criteria,
            }
        return questions

    @staticmethod
    def _recommendation(
        answers: Mapping[str, Any],
        candidates: tuple[RouteCandidate, ...],
        current: RouteCandidate,
    ) -> dict[str, Any]:
        route_answer = answers.get("route")
        route_id = route_answer.get("choice") if isinstance(route_answer, Mapping) else None
        selected = next((item for item in candidates if item.identifier == route_id), None)
        complexity = ShadowModelRouter._score(answers, "complexity")
        risk = ShadowModelRouter._score(answers, "risk")
        has_policy_signal = "complexity" in answers or "risk" in answers
        policy_provider = "vertex-gemini" if complexity >= 2.0 or risk >= 2.0 else "ollama-provider"
        policy_selected = next(
            (item for item in candidates if item.provider == policy_provider),
            None,
        )
        if policy_selected is not None and has_policy_signal:
            selected = policy_selected
            reason = (
                f"Harness policy: {'Vertex for complex/high-risk task' if policy_provider == 'vertex-gemini' else 'Ollama for routine/low-risk task'}; "
                f"complexity={complexity:.2f}; risk={risk:.2f}"
            )
        elif selected is not None:
            reason = f"Jev route choice; preferred policy provider unavailable; complexity={complexity:.2f}; risk={risk:.2f}"
        elif route_answer is None:
            selected = current
            reason = f"Current authorized route; no stronger provider policy candidate; complexity={complexity:.2f}; risk={risk:.2f}"
        else:
            return {
                "provider": current.provider,
                "model": current.model,
                "candidateId": current.identifier,
                "allowed": False,
                "reason": "Jev returned an undeclared route and no policy candidate was available",
            }
        return {
            "provider": selected.provider,
            "model": selected.model,
            "candidateId": selected.identifier,
            "allowed": True,
            "reason": reason,
        }

    @staticmethod
    def _score(answers: Mapping[str, Any], key: str) -> float:
        answer = answers.get(key)
        if not isinstance(answer, Mapping):
            return 0.0
        value = answer.get("score")
        return float(value) if isinstance(value, (int, float)) else 0.0

    @staticmethod
    def _provider_policy(provider_id: str) -> str:
        if provider_id == "ollama-provider":
            return "local and no external API cost; prefer for routine, low-risk tasks"
        if provider_id == "vertex-gemini":
            return "remote and metered; prefer for complex, high-risk, or capability-heavy tasks"
        return "use only when explicitly authorized"
