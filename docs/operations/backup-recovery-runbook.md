# WESA Harness Backup and Recovery Runbook

## Backup boundaries

- Neon is authoritative for `harness` operational state and `memory` durable memory.
- Local backup covers replay state, audit chain, protected configuration, profiles, manifests, plugin manifests and lockfiles.
- Secret values are never exported into workspace or ZIP archives.
- The secret inventory records required names only. Values must be re-provisioned from Greg's secure source.

## Create local state backup

Stop the API first so SQLite and the audit file are stable. Choose an absolute destination outside `WesaTeam`:

```powershell
./scripts/backup_state.ps1 -DestinationDirectory "D:\WesaBackups"
```

The script refuses workspace destinations, verifies the audit chain and creates a timestamped ZIP.

## Neon recovery

1. Stop Harness API/Desktop.
2. In Neon **Backup & Restore**, inspect the target point with Time Travel before changing the active branch.
3. Prefer restoring to a new branch first.
4. Validate `harness` task/outbox state and `memory` scope/status rows.
5. Finalize restore only after validation.
6. Reapply migrations with `dotnet tool run dotnet-ef database update` if the restore point predates a migration.
7. Start API and verify `/api/observability/metrics` reports `auditChainOk=true` and no unexpected queued/running work.

The usable recovery window depends on the Neon project's configured restore window. Scheduled snapshots depend on the selected Neon plan.

## Local restore

1. Verify the ZIP's `audit-head.json` against the expected off-machine record.
2. Stop API/Desktop.
3. Restore files only into their exact Harness-relative paths.
4. Re-provision user-secret values listed in `config/secret-inventory.json`.
5. Do not restore `%LOCALAPPDATA%` secret material onto another user/machine blindly; DPAPI material is machine/user bound.
6. Run `./scripts/release_check.ps1`.
7. Start API and confirm audit, plugin and queue health.

## Recovery drills

- Queued task: terminate API after queue commit, restart, verify one terminal execution.
- Pipeline: terminate between stages, restart, verify completed stages are not repeated.
- Sandbox: terminate after sandbox record becomes running; restart must requeue it, while K3s Job name remains deterministic for the execution ID.
- Audit tamper: modify a copied audit file only; verification must fail.
- Neon: restore to a temporary branch and run read-only invariants before any production restore.

## Rollback rule

Legacy `agent-ecosystem` remains operational until Greg approves final cutover. P5 completion does not delete or retire it.
