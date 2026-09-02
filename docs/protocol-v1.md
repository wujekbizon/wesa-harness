# WESA Harness Protocol v1

Transport-neutral JSON-RPC-style messages. Local P1 transport is newline-delimited JSON over supervised stdio.

## Lifecycle

1. Client sends `initialize` once.
2. Engine returns protocol version and capabilities.
3. Client may call read methods, validate signed work, or submit execution through the .NET control plane.
4. `task/execute` runs an issuer-scoped signed task through memory, Tool Broker attachments, Ollama and Artifact Gate.

## Envelope

Request:

```json
{"id":"1","method":"initialize","params":{"client":{"name":"wesa-harness-api","version":"0.1.0"},"protocolVersion":"1.0"}}
```

Response:

```json
{"id":"1","result":{"protocolVersion":"1.0","executionEnabled":false}}
```

Error:

```json
{"id":"1","error":{"code":"INVALID_REQUEST","message":"..."}}
```

## Stable identities

- `workspaceId`
- `sessionId`
- `taskId`
- `turnId`
- `artifactId`
- `approvalId`
- `eventId`
- `pipelineRunId`

IDs are strings at the wire boundary. Implementations may use UUID/ULID internally.

## P1 methods

- `initialize`
- `health/read`
- `manifest/list`
- `task/validate`

## P2 security

- .NET derives a 256-bit task-signing key from the local capability token and sends only the verification key to the supervised Python process during initialization.
- Envelopes include issuer, agent, workspace, UTC timestamp, nonce and HMAC-SHA256 signature.
- Python verifies canonical JSON, 10-minute freshness, future skew, durable SQLite nonce uniqueness and agent capability manifests.
- Only `write` is enabled in shadow mode; `executionEnabled` is always false.
- Artifact gate checks canonical paths, protected targets, size, encoding, JSON/YAML/Python syntax, unresolved imports, risky execution, network use and credential content.
- Gate outcomes: `accept`, `review`, `proposal`, `quarantine`.
- Protected-file changes can only become patch proposals.

## Reserved P2/P3 methods

- `workspace/open`
- `session/start`, `session/resume`, `session/fork`
- `task/submit`, `task/cancel`, `task/steer`
- `approval/resolve`
- notifications: `trajectory/event`, `approval/requested`, `artifact/created`, `task/statusChanged`

## P3 execution

- `plugin/list` reports plugin lifecycle/health.
- `provider/models` discovers installed Ollama models dynamically.
- `agent/list` returns migrated declarative agent profiles.
- `task/execute` signs provider and model selection inside the envelope.
- Ollama output must pass the Artifact Gate before atomic write or user-visible acceptance.
- .NET persists queued, running and terminal trajectory events and publishes them through SignalR.

## P4 memory, attachments and issuers

- Task envelopes include an ordered `attachments` array; path, media type and optional SHA-256 are covered by the HMAC.
- Attachments are workspace-relative text references, not raw client-uploaded prompt content.
- The Tool Broker authorizes exact tool + operation + path scope before reading.
- Attachment bytes are hashed, type-checked, redacted and wrapped as `trust="untrusted"` source documents.
- Scoped memory retrieval happens before ranking and is injected within a fixed token budget.
- Post-task memory capture runs only when Artifact Gate outcome is `accept`.
- `harness-api`, `harness-pipeline` and `harness-headless` have separate HMAC keys.
- Pipeline issuer cannot submit git or skill-mutation task types.
- Headless issuer is limited to low-risk text tasks and `staging/headless/**`.
- Headless command transport uses a five-minute timestamp window and persistent nonce replay defense.
- Pipeline REST contracts: `POST /api/pipelines/text-review`, `GET /api/pipelines/{id}`.

Every new field is additive within v1. Breaking changes require a new major protocol version.
