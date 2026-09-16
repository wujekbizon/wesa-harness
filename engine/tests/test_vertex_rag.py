import json

from wesa_engine.plugins.contracts import PluginManifest
from wesa_engine.plugins.vertex_rag import VertexRagPlugin


def make_plugin(tmp_path):
    manifest = PluginManifest(
        plugin_id="vertex-rag",
        version="0.1.0",
        api_version="1.0",
        kind="retrieval",
        entrypoint="wesa_engine.plugins.vertex_rag:create_plugin",
        trusted=True,
        capabilities=("retrieve-contexts",),
        config={
            "projectEnvironmentVariable": "TEST_RAG_PROJECT",
            "locationEnvironmentVariable": "TEST_RAG_LOCATION",
            "corporaEnvironmentVariable": "TEST_RAG_CORPORA",
            "vectorDistanceThreshold": 0.34,
        },
        runtime_root=tmp_path,
    )
    return VertexRagPlugin(manifest)


def configure(monkeypatch):
    monkeypatch.setenv("TEST_RAG_PROJECT", "project-12345")
    monkeypatch.setenv("TEST_RAG_LOCATION", "us-central1")
    monkeypatch.setenv(
        "TEST_RAG_CORPORA",
        json.dumps({"lumio-shared-knowledge": "projects/project-12345/locations/us-central1/ragCorpora/1"}),
    )


def test_retrieval_sends_distance_filter_and_keeps_relevant_contexts(monkeypatch, tmp_path):
    configure(monkeypatch)
    plugin = make_plugin(tmp_path)
    captured = {}

    def post(url, body):
        captured["body"] = body
        return {
            "contexts": {
                "contexts": [
                    {"text": "homeostaza", "sourceDisplayName": "homeostaza.md", "score": 0.21}
                ]
            }
        }

    plugin._post = post
    result = plugin.retrieve("lumio-shared-knowledge", "Czym jest homeostaza?", 8)

    assert result[0].content == "homeostaza"
    assert captured["body"]["query"]["text"] == "homeostaza"
    assert captured["body"]["query"]["ragRetrievalConfig"]["filter"] == {
        "vectorDistanceThreshold": 0.34
    }


def test_retrieval_drops_corpus_miss(monkeypatch, tmp_path):
    configure(monkeypatch)
    plugin = make_plugin(tmp_path)
    plugin._post = lambda url, body: {
        "contexts": {
            "contexts": [
                {"text": "unrelated", "sourceDisplayName": "homeostaza.md", "score": 0.41}
            ]
        }
    }

    assert plugin.retrieve("lumio-shared-knowledge", "Kto jest prezydentem USA?", 8) == ()
