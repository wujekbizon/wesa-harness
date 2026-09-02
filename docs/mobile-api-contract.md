# Mobile Monitor API Contract

## Purpose

React Native/mobile clients may monitor Harness state. They cannot submit tasks, start pipelines, execute sandbox profiles, register workspaces or resolve approvals.

## Authentication

- Header: `X-Wesa-Capability-Token`.
- `Security:MobileMonitorToken` receives only the `observer` role.
- Greg's Desktop token receives `observer` + `greg`.
- Never embed Greg's token in a mobile build.

## Observer endpoints

- `GET /api/agents`
- `GET /api/providers/ollama/models`
- `GET /api/tasks/{taskId}`
- `GET /api/tasks/sessions/{sessionId}/trajectory`
- `GET /api/tasks/{taskId}/artifacts`
- `GET /api/approvals?status=pending`
- `GET /api/pipelines/{pipelineId}`
- `GET /api/sandbox/executions/{executionId}`
- `GET /api/observability/metrics`
- SignalR: `/hubs/trajectory`

## Greg-only mutations

- Workspace registration.
- Task submission.
- Pipeline start.
- Sandbox execution request.
- Future approval resolution.

Mobile-token attempts return HTTP 403. Contract tests verify observer reads and mutation denial.

## Stability

Wire DTOs live in `Wesa.Harness.Contracts`. New fields are additive within protocol/API v1. Breaking changes require a new major version.
