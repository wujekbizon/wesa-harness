from pathlib import Path

from wesa_engine.profiles import load_profiles


ROOT = Path(__file__).resolve().parents[2]


def test_all_legacy_agents_have_explicit_provider_policy():
    profiles = load_profiles(ROOT / "config" / "profiles")
    assert len(profiles) == 14
    assert profiles["W1"].default_provider == "ollama-provider"
    assert profiles["W1"].allowed_providers == ("ollama-provider", "vertex-gemini")
    assert profiles["W1"].task_types == ("write",)
    assert profiles["W1"].default_model is None
    assert profiles["D1"].source.startswith("legacy:")
