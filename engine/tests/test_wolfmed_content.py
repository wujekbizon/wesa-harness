import json
from pathlib import Path
from uuid import UUID

import pymupdf

from wesa_engine.plugins.registry import PluginRegistry
from wesa_engine.kernel.auth import sign_for_test
from wesa_engine.kernel.contracts import TaskEnvelopeV2
from wesa_engine.runtime.server import HarnessProtocolServer
from test_protocol_server import KEY, initialize, runtime_root, signed_task


ROOT = Path(__file__).resolve().parents[2]


def plugin():
    registry = PluginRegistry(ROOT / "plugins")
    registry.load_all()
    return registry, registry.workflow_extension("wolfmed-content")


def question(text: str, correct: int = 0):
    return {
        "id": "bad-id",
        "meta": {"course": "wrong", "category": "wrong"},
        "data": {
            "question": text,
            "answers": [
                {"option": f"Answer {index}", "isCorrect": index == correct}
                for index in range(4)
            ],
        },
        "createdAt": "2026-01-01 00:00:00.000000",
        "updatedAt": None,
    }


def test_prepare_txt_is_scoped_hashed_and_deterministic(tmp_path):
    source = tmp_path / "intake" / "wolfmed" / "lecture.txt"
    source.parent.mkdir(parents=True)
    source.write_text("Fakt medyczny. " * 20, encoding="utf-8")
    registry, extension = plugin()
    try:
        first = extension.execute_stage(
            "prepare",
            {
                "workspaceRoot": str(tmp_path),
                "sourcePath": "intake/wolfmed/lecture.txt",
                "category": "fizjologia",
            },
        )
        second = extension.execute_stage(
            "prepare",
            {
                "workspaceRoot": str(tmp_path),
                "sourcePath": "intake/wolfmed/lecture.txt",
                "category": "fizjologia",
            },
        )
    finally:
        registry.stop_all()

    assert first == second
    assert first["chunkCount"] == 1
    assert first["chunks"][0]["quality"]["usable"] is True
    assert len(first["sourceSha256"]) == 64


def test_prepare_pdf_extracts_page_chunks(tmp_path):
    source = tmp_path / "intake" / "wolfmed" / "book.pdf"
    source.parent.mkdir(parents=True)
    document = pymupdf.open()
    for index in range(3):
        page = document.new_page()
        page.insert_text((72, 72), f"Rozdzial {index + 1}. " + "Tresc medyczna. " * 20)
    document.save(source)
    document.close()
    registry, extension = plugin()
    try:
        result = extension.execute_stage(
            "prepare",
            {
                "workspaceRoot": str(tmp_path),
                "sourcePath": "intake/wolfmed/book.pdf",
                "category": "fizjologia",
                "chunkPages": 2,
                "autoTrim": False,
            },
        )
    finally:
        registry.stop_all()

    assert result["totalPages"] == 3
    assert [value["pages"] for value in result["chunks"]] == ["1-2", "3-3"]
    assert all(value["text"] for value in result["chunks"])


def test_release_normalizes_deduplicates_and_assigns_stable_uuid():
    registry, extension = plugin()
    batch = [
        question("Jak działa serce?"),
        question("JAK DZIAŁA SERCE?"),
        question("Co przedstawiono w tabeli 2?"),
        {"not": "a question"},
    ]
    try:
        first = extension.execute_stage(
            "release",
            {"category": "fizjologia", "validatedBatches": [json.dumps(batch)]},
        )
        second = extension.execute_stage(
            "release",
            {"category": "fizjologia", "validatedBatches": [batch]},
        )
    finally:
        registry.stop_all()

    assert first["questionCount"] == 1
    assert first["droppedDuplicate"] == 1
    assert first["droppedVisualReference"] == 1
    assert first["droppedInvalid"] == 1
    assert first["questions"] == second["questions"]
    assert UUID(first["questions"][0]["id"]).version == 4
    assert first["questions"][0]["meta"] == {
        "course": "pielegniarstwo",
        "category": "fizjologia",
    }


def test_signed_workflow_extension_routes_output_through_artifact_gate(tmp_path):
    root = runtime_root(tmp_path)
    source = root / "intake" / "wolfmed" / "lecture.txt"
    source.parent.mkdir(parents=True)
    source.write_text("Fakt medyczny. " * 20, encoding="utf-8")
    server = HarnessProtocolServer(root)
    initialize(server)
    envelope = signed_task(root, "wolfmed-extension")
    envelope["task"]["agentId"] = "EX1"
    envelope["task"]["type"] = "extract_pdf"
    envelope["task"]["outputFile"] = "staging/wolfmed/prepared.json"
    envelope["signature"] = sign_for_test(KEY, TaskEnvelopeV2.from_mapping(envelope))

    response = server.handle(
        {
            "id": "2",
            "method": "workflow/extension",
            "params": {
                "envelope": envelope,
                "pluginId": "wolfmed-content",
                "operation": "prepare",
                "inputs": {
                    "sourcePath": "intake/wolfmed/lecture.txt",
                    "category": "fizjologia",
                },
            },
        }
    )

    assert response["result"]["gateOutcome"] == "accept"
    assert response["result"]["output"]["chunkCount"] == 1
    assert (root / "staging" / "wolfmed" / "prepared.json").exists()
