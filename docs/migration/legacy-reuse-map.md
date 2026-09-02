# Legacy Agent Ecosystem Reuse Map

The live `agent-ecosystem/` remains operational until final cutover. Reuse means copying knowledge or deterministic logic into new Harness contracts; the new runtime never imports live modules.

| Legacy area | Decision | Target phase/location |
|---|---|---|
| `agents/skills/*.yaml` | Copy identity, role, task types and system prompts; replace provider/model/tool wiring | P3 `config/profiles/` |
| `agents/pipelines/*.yaml` | Workflow intent copied; first durable W1→Q1 pipeline reimplemented with signed stages | P4 complete for first pipeline |
| `src/memory/**` | Tested schema/retrieval/promotion behavior reimplemented behind kernel memory adapter | P4 complete |
| `secure_core/audit.py`, `protected.py` | Already reimplemented and tested in new kernel | P1/P2 complete |
| `secure_core/vault.py` | Reuse Windows DPAPI design, not legacy module | P4/P5 |
| retry/timeout/circuit-breaker behavior | Reimplement at plugin consumer seam | P3 |
| `src/router.py`, `gemini_agent.py`, `ollama_agent.py` | Do not copy; provider-coupled and bypasses new kernel | Replaced by plugin runtime |
| `daemon.py` | Do not copy; monolith and direct side effects | Replaced incrementally by Engine + CQRS |
| MCP servers | Tool Broker contracts reused; external servers remain disconnected, scoped local reads restored first | P4 bounded slice |
| React monitor/FastAPI monitor | Keep with live system; do not copy | Retire after Avalonia parity |
| logs/tasks/SQLite runtime state | Import read-only history through explicit importer | Later migration utility |

## Migration rule

Every copied asset receives:

1. New schema/version header.
2. New capability and security ownership.
3. Tests in the new Harness.
4. No path back to writable legacy state.
5. Provenance field identifying its legacy source.
