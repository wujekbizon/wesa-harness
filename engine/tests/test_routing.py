from __future__ import annotations

import pytest

from wesa_engine.routing import RouteCandidate, RoutingError, ShadowModelRouter, TypeSafeDecisionClient


def candidates() -> tuple[RouteCandidate, ...]:
    return (
        RouteCandidate("ollama/gemma4:26b", "ollama-provider", "gemma4:26b", ("completion",)),
        RouteCandidate("vertex/gemini-2.5-flash", "vertex-gemini", "gemini-2.5-flash", ("completion", "vision", "pdf")),
    )


def test_router_questions_follow_typesafe_shapes():
    questions = ShadowModelRouter._questions(candidates())

    assert questions["intent"]["type"] == "choice"
    assert isinstance(questions["intent"]["criteria"], dict)
    assert questions["complexity"]["type"] == "score"
    assert len(questions["complexity"]["criteria"]) >= 2
    assert questions["requires_tools"]["type"] == "noul"
    assert set(questions["route"]["criteria"]) == {
        "ollama/gemma4:26b",
        "vertex/gemini-2.5-flash",
    }


def test_embedding_models_are_not_generation_candidates():
    assert ShadowModelRouter._is_embedding_model("nomic-embed-text:latest", ()) is True
    assert ShadowModelRouter._is_embedding_model("qwen3.8:27b", ("completion",)) is False
    assert ShadowModelRouter._is_embedding_model("custom-model", ("embedding",)) is True


def test_router_recommendation_accepts_only_declared_candidates():
    answer = {
        "route": {"choice": "vertex/gemini-2.5-flash"},
        "complexity": {"score": 2.5},
        "risk": {"score": 1.0},
    }
    result = ShadowModelRouter._recommendation(answer, candidates(), candidates()[0])

    assert result["provider"] == "vertex-gemini"
    assert result["model"] == "gemini-2.5-flash"
    assert result["candidateId"] == "vertex/gemini-2.5-flash"
    assert result["allowed"] is True
    assert "Vertex" in result["reason"]

    unknown = ShadowModelRouter._recommendation(
        {"route": {"choice": "untrusted/provider"}},
        candidates(),
        candidates()[0],
    )
    assert unknown["candidateId"] == "ollama/gemma4:26b"
    assert unknown["allowed"] is False

    simple = ShadowModelRouter._recommendation(
        {
            "route": {"choice": "vertex/gemini-2.5-flash"},
            "complexity": {"score": 0.5},
            "risk": {"score": 0.5},
        },
        candidates(),
        candidates()[0],
    )
    assert simple["provider"] == "ollama-provider"
    assert "Ollama" in simple["reason"]


def test_typesafe_client_requires_key(monkeypatch):
    monkeypatch.delenv("TYPESAFE_API_KEY", raising=False)
    client = TypeSafeDecisionClient()

    with pytest.raises(RoutingError, match="TYPESAFE_API_KEY is not configured"):
        client.evaluate({}, {})


def test_auto_questions_use_one_canonical_handler_choice():
    questions = ShadowModelRouter._questions(
        (),
        (
            {"id": "R1/research", "agentId": "R1", "taskType": "research", "role": "research"},
            {"id": "W1/write", "agentId": "W1", "taskType": "write", "role": "writer"},
        ),
    )

    assert questions["handler"]["type"] == "choice"
    assert set(questions["handler"]["criteria"]) == {"R1/research", "W1/write", "no_match"}
    assert "route" not in questions


def test_auto_preview_resolves_agent_and_task_before_provider(monkeypatch):
    local = RouteCandidate("ollama/local", "ollama-provider", "local", ("completion",))
    profile = type("Profile", (), {
        "role": "researcher", "task_types": ("research",),
        "allowed_providers": ("ollama-provider",),
    })()
    manifest = type("Manifest", (), {
        "allows_task_type": lambda self, task: task == "research",
    })()
    router = ShadowModelRouter.__new__(ShadowModelRouter)
    router.root = __import__("pathlib").Path(".")
    router.profiles = {"R1": profile}
    router._candidates = lambda *_: (local,)
    router.client = type("Client", (), {
        "evaluate": lambda self, state, questions: (
            {"handler": {"choice": "R1/research", "confidence": 0.91}, "complexity": {"score": 0.5}, "risk": {"score": 0.2}},
            {},
            4,
        ),
    })()
    router.audit = type("Audit", (), {"append": lambda *args: None})()
    monkeypatch.setattr("wesa_engine.routing.load_manifest_directory", lambda _: {"R1": manifest})

    result = router.preview("auto", "auto", "research this")

    assert result["status"] == "ok"
    assert result["recommendedAgentId"] == "R1"
    assert result["recommendedTaskType"] == "research"
    assert result["recommendedProvider"] == "ollama-provider"


def test_auto_preview_rejects_low_confidence_handler(monkeypatch):
    local = RouteCandidate("ollama/local", "ollama-provider", "local", ("completion",))
    profile = type("Profile", (), {
        "role": "writer", "task_types": ("write",),
        "allowed_providers": ("ollama-provider",),
    })()
    manifest = type("Manifest", (), {"allows_task_type": lambda self, task: True})()
    router = ShadowModelRouter.__new__(ShadowModelRouter)
    router.root = __import__("pathlib").Path(".")
    router.profiles = {"W1": profile}
    router._candidates = lambda *_: (local,)
    router.client = type("Client", (), {
        "handler_confidence_floor": 0.55,
        "evaluate": lambda self, state, questions: ({"handler": {"choice": "W1/write", "confidence": 0.2}}, {}, 1),
    })()
    router.audit = type("Audit", (), {"append": lambda *args: None})()
    monkeypatch.setattr("wesa_engine.routing.load_manifest_directory", lambda _: {"W1": manifest})

    result = router.preview("auto", "auto", "write this")

    assert result["status"] == "fallback"
    assert result["recommendationAllowed"] is False
