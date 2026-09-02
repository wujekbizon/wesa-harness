# Artifact Gate Mechanics

## Purpose

The Artifact Gate is deterministic security code. It is not another AI model and does not execute generated code. It applies explicit rules to every model-produced artifact before anything can reach its requested output path.

## End-to-end flow

```text
Signed task envelope
  ↓
HMAC, issuer, timestamp and nonce verification
  ↓
Agent capability, task type, provider and path authorization
  ↓
Ollama generates an artifact
  ↓
Artifact Gate
  ├─ size, empty, UTF-8 and NUL checks
  ├─ credential detection and redaction
  ├─ file-specific parsing and risk checks
  └─ outcome classification
       ├─ Accept
       ├─ Review
       ├─ Proposal
       └─ Quarantine
  ↓
Atomic accepted write or isolated review storage
```

Authorization happens before model execution. Artifact inspection happens after generation but before writing to the requested path.

## Why an unresolved Python import becomes Review

Given:

```python
import module_that_does_not_exist_anywhere
```

The Gate performs these steps:

1. The `.py` output suffix classifies the artifact as Python.
2. Python's `ast.parse()` builds a syntax tree without executing the code.
3. The Gate walks the tree and finds the `import` statement.
4. It extracts the top-level module name.
5. `importlib.util.find_spec()` checks whether the module is discoverable in the Secure Engine's Python environment.
6. No module is found, so the Gate records `unresolved import: module_that_does_not_exist_anywhere`.
7. An unknown import is uncertain, not automatically malicious. The outcome is therefore Review.

This is a heuristic. A valid project-local module may not exist on the Engine's import path and can produce a false positive. That is why the Gate asks for human review instead of quarantining it.

## Current deterministic checks

### Universal checks

- Maximum artifact size from the agent capability manifest.
- Authorized workspace-relative output path.
- No parent traversal or absolute-path escape.
- Valid UTF-8 text.
- No NUL bytes.
- Non-empty content.
- Known and pattern-matched credential detection.

### Python

- Syntax validation with `ast.parse()`.
- Unknown imports become Review.
- Network-library imports become Review when the manifest denies network access.
- Walrus operators become Review because legacy Gemma output produced malformed uses.
- `eval()`, `exec()` and `system()` become Quarantine.
- Subprocess calls using `shell=True` become Quarantine.

### JSON and YAML

- JSON must parse successfully.
- YAML must pass `yaml.safe_load()`.
- Invalid structure becomes Quarantine.

### JavaScript and TypeScript

- `eval`, `child_process` and `execSync` patterns become Quarantine.
- Network calls become Review when the manifest denies network access.
- JS/TS currently requires compiler review, so valid output is routed to Review until compiler validation is integrated.

## Outcome priority

| Outcome | Meaning | Requested path written? |
|---|---|---|
| Accept | All enforced checks passed. | Yes, atomically. |
| Review | Output may be valid but needs human judgment. | No. |
| Proposal | Protected-file change is represented as a reviewable patch. | No. |
| Quarantine | Invalid, dangerous or policy-denied artifact. | No. |
| Reject | Processing cannot continue safely. | No. |

Quarantine findings take priority over Review findings. For example, a Python file containing both an unresolved import and `eval()` is quarantined.

## Credential behavior

The Gate scans for known secret values and common credential patterns. Detected values are replaced before storage or display. The original credential-bearing output is never written to its requested location.

Example:

```python
password = "super-secret-value"
```

Stored review content:

```python
password = "[REDACTED:CREDENTIAL]"
```

## Storage behavior

- Accept: atomic write under the authorized workspace path.
- Review: `artifacts/review/<task-id>/<artifact-id>/`.
- Proposal: `artifacts/proposals/<task-id>/<artifact-id>/`.
- Quarantine: `artifacts/quarantine/<task-id>/<artifact-id>/`.

Non-accepted folders contain:

- `artifact.bin`: isolated, redacted content when applicable.
- `gate.json`: outcome, reasons, media type and requested path.

## Security boundary

The Gate does not prove that accepted code is logically correct or bug-free. It enforces deterministic structural and security rules. Testing, compilation, sandbox execution and human approval are separate layers.

Current implementation:

- `engine/wesa_engine/kernel/gate.py`
- `engine/wesa_engine/kernel/artifact_store.py`
- `engine/wesa_engine/runtime/server.py`

