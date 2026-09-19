# Local Development

## One-time setup

From `WesaTeam/wesa-harness/`:

1. Use the existing dedicated WESA Neon project that already contains the `memory` schema.
2. In Neon **Connect**, choose its direct connection endpoint for initial EF migrations.
3. Copy the connection string to the Windows clipboard. Do not paste it into chat, source files, or `appsettings.json`.
4. Store it in .NET user-secrets without placing the value in PowerShell history:

```powershell
dotnet tool restore
$neonConnection = Get-Clipboard
dotnet user-secrets set "ConnectionStrings:HarnessDatabase" $neonConnection --project src/Wesa.Harness.Api
Remove-Variable neonConnection
Set-Clipboard -Value " "

$tokenBytes = New-Object byte[] 32
$rng = [Security.Cryptography.RandomNumberGenerator]::Create()
$rng.GetBytes($tokenBytes)
$capabilityToken = [BitConverter]::ToString($tokenBytes).Replace("-", "")
dotnet user-secrets set "Security:CapabilityToken" $capabilityToken --project src/Wesa.Harness.Api
$rng.Dispose()
Remove-Variable tokenBytes, rng, capabilityToken

dotnet tool run dotnet-ef database update --project src/Wesa.Harness.Api --startup-project src/Wesa.Harness.Api
```

P4 also uses separate HMAC identities for pipelines and reduced headless commands. Generate each with the same 32-byte pattern and store them as `Security:PipelineToken` and `Security:HeadlessToken`. Never reuse or print the interactive capability token.

Optional TypeSafe access is stored outside the workspace and forwarded to the supervised engine only as `TYPESAFE_API_KEY`:

```powershell
[string]$jevKey = (Get-Clipboard).Trim()
dotnet user-secrets set "TypeSafe:ApiKey" "$jevKey" --project src/Wesa.Harness.Api
Remove-Variable jevKey
Set-Clipboard -Value " "
```

P4 memory uses the same Neon project but needs its PostgreSQL URI form for psycopg. Store it separately under `Memory:DatabaseUrl`; it is a format-specific secret alias, not another database:

```powershell
$memoryDatabaseUrl = Get-Clipboard
dotnet user-secrets set "Memory:DatabaseUrl" $memoryDatabaseUrl --project src/Wesa.Harness.Api
Remove-Variable memoryDatabaseUrl
Set-Clipboard -Value " "
```

Secrets are stored outside the workspace under the `wesa-harness-api-development` user-secrets ID. Do not run or share `dotnet user-secrets list`, because it prints values.

## Start API

```powershell
dotnet run --project src/Wesa.Harness.Api --launch-profile http
```

Expected: `http://localhost:5035/health` returns healthy.

Local development is HTTP over enforced loopback only, so HTTPS redirection is disabled. A production deployment must configure HTTPS explicitly.

MediatR 14 permits development/testing without a key. Production startup fails unless `MediatR:LicenseKey` is configured under the applicable Lucky Penny license; never place the key in `appsettings.json`.

## Start desktop

Second terminal:

```powershell
dotnet run --project src/Wesa.Harness.Desktop
```

Click **Connect**. Expected status: `Control plane ready`.

P4 supports secured task and pipeline execution. Keep Ollama running, choose an agent/task/model, optionally provide semicolon-separated scoped text attachments, enter an allowed `staging/**` output path, then click **Run secured task**. The result is written only after the Artifact Gate accepts it. **Run W1 → Q1 pipeline** starts the durable two-stage pipeline.

## Tests without Docker

```powershell
dotnet test Wesa.Harness.slnx --filter "Category!=PostgreSQL&Category!=EngineProcess&Category!=Neon"
python -m pytest engine/tests
```

## PostgreSQL atomicity test

Start Docker Desktop manually, then:

```powershell
dotnet test tests/Wesa.Harness.Tests/Wesa.Harness.Tests.csproj --filter "Category=PostgreSQL"
```

The test creates and removes its own `postgres:16-alpine` container.

## Cross-language protocol test

```powershell
$env:WESA_TEST_PYTHON = (Get-Command python).Source
dotnet test tests/Wesa.Harness.Tests/Wesa.Harness.Tests.csproj --filter "Category=EngineProcess"
Remove-Item Env:WESA_TEST_PYTHON
```

## Neon first-slice smoke test

Uses local .NET user-secrets, writes isolated test rows, verifies task/event/outbox state, then deletes them:

```powershell
$env:WESA_TEST_PYTHON = (Get-Command python).Source
dotnet test tests/Wesa.Harness.Tests/Wesa.Harness.Tests.csproj --filter "Category=Neon"
Remove-Item Env:WESA_TEST_PYTHON
```
