# WESA Harness

Greenfield WESA agent harness. The live `../agent-ecosystem/` remains independent.

Current version: `0.6.0` — P6 orchestration/plugin platform: Ollama + optional Vertex Gemini/ADC, provider policy, trusted plugin lifecycle, versioned DAG workflows, bounded repeats/retries, conditions, approvals, sandbox stages, cancellation/resume and disabled-by-default schedules.

## Components

- `engine/` — Python Secure Engine and versioned JSON-RPC/JSONL protocol.
- `src/Wesa.Harness.Api/` — .NET 10 CQRS control plane.
- `src/Wesa.Harness.Contracts/` — client-facing DTO and event contracts.
- `src/Wesa.Harness.Desktop/` — Avalonia desktop client.
- `tests/` — .NET unit/integration tests.
- `config/manifests/` — default-deny agent capability manifests.
- `staging/` — review-only output from the live ecosystem.

No component imports runtime code from `agent-ecosystem/`.

## Development

```powershell
dotnet restore Wesa.Harness.slnx
dotnet build Wesa.Harness.slnx
python -m pytest engine/tests
```

PostgreSQL integration tests require Docker; run them with the `Category=PostgreSQL` filter.

## Documentation

Start at [`docs/README.md`](docs/README.md). Current flow: [Markdown/Mermaid](docs/architecture/current-flow.md) or [editable Excalidraw](docs/architecture/current-flow.excalidraw).

## Start locally

See `docs/development.md`. The API requires PostgreSQL and user-secrets configuration.
