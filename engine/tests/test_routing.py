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
