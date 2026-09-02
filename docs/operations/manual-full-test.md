# WESA Harness Full Manual Test

Run every command yourself from Windows PowerShell. Do not run `repair_audit_fork.py`; the audit re-chain is complete.

## 1. Start dependencies

Open PowerShell in `C:\Users\wujek\WesaTeam\wesa-harness`.

```powershell
docker info
ollama list
.\infra\k3d\bootstrap.ps1 -EnableHarness -RunLiveSmoke
```

Pass:

- Docker responds.
- Ollama lists at least one chat model.
- Bootstrap ends with `cluster=READY policy=PASS`.
- Compile, egress and timeout smoke checks pass.

## 2. Start the API

Keep this terminal open:

```powershell
dotnet run --project src/Wesa.Harness.Api --launch-profile http
```

Pass:

- Console shows `Now listening on: http://localhost:5035`.
- `http://localhost:5035/health` returns `Healthy`.

## 3. Start Avalonia

Open a second PowerShell in the same folder:

```powershell
dotnet run --project src/Wesa.Harness.Desktop
```

Do not close either terminal during UI testing.

## 4. Connect

In Avalonia:

1. Set **Control plane URL** to `http://127.0.0.1:5035`.
2. Set **Workspace root** to `C:\Users\wujek\WesaTeam\wesa-harness`.
3. Click **Connect / refresh**.

Pass:

- Status starts with `Ready`.
- At least one agent and Ollama model appear.
- Status shows `audit=OK`.
- Gate summary shows `sandbox=K3s sandbox enabled`.

## 5. Secured W1 task

1. Agent: `W1`.
2. Task type: `write`.
3. Select a chat model.
4. Prompt: `Write a five-line Markdown status report for WESA Harness. Return only the report.`
5. Output: `staging/manual-w1.md`.
6. Leave attachments empty.
7. Click **Run secured task**.

Pass:

- Final status is `completed` with `gate=accept`.
- Gate Inspector shows the full secured route through Storage.
- `staging/manual-w1.md` exists and matches the result.

## 6. Signed attachment task

Keep W1/write selected:

1. Prompt: `Summarize the attached documentation in five bullets. Return Markdown only.`
2. Output: `staging/manual-attachment.md`.
3. Attachment: `docs/README.md`.
4. Click **Run secured task**.

Pass: completed, Gate accepted, output exists, and the result describes the attached file.

## 7. D1 Python and Kubernetes sandbox

1. Agent: `D1`.
2. Task type: `code`.
3. Prompt: `Return only valid Python source. Define add(a, b) and print(add(2, 3)). No Markdown fences.`
4. Output: `staging/manual-sandbox.py`.
5. Clear attachments.
6. Click **Run secured task**.
7. Confirm `completed` and `gate=accept`.
8. Without reconnecting or restarting Avalonia, click **Sandbox compile**.

Pass:

- Sandbox finishes `completed`, exit code 0.
- Gate summary says execution completed inside K3s.
- No Windows/local execution fallback occurs.

Verify cleanup in a third PowerShell:

```powershell
$kubeconfig = (k3d kubeconfig write wesa-sandbox).Trim()
kubectl --kubeconfig $kubeconfig get job,pod,configmap -n wesa-sandbox -l app.kubernetes.io/name=wesa-sandbox
Remove-Variable kubeconfig
```

Pass: no sandbox Job, Pod or ConfigMap remains.

## 8. Durable W1 to Q1 pipeline

1. Select any chat model.
2. Prompt: `Draft a short WESA Harness release note covering security, sandboxing, and recovery.`
3. Click **Run W1 → Q1 pipeline**.

Pass:

- Pipeline reaches `completed`, stage `2/2`.
- Result lists W1 draft and Q1 review output paths.
- Gate summary confirms each stage used a signed task and Gate route.

## 9. Negative security check

1. Select W1/write.
2. Output: `..\manual-blocked.md`.
3. Prompt: `Write one sentence.`
4. Click **Run secured task**.

Pass:

- Output is rejected, blocked, reviewed or quarantined.
- No file is written outside `staging/`.
- Gate Inspector gives the path-policy reason.

Restore output to `staging/manual-w1.md` afterward.

## 10. Inspect authenticated metrics

Run without printing the capability token:

```powershell
$secretsPath = Join-Path $env:APPDATA 'Microsoft\UserSecrets\wesa-harness-api-development\secrets.json'
$secrets = Get-Content -Raw -LiteralPath $secretsPath | ConvertFrom-Json
$headers = @{ 'X-Wesa-Capability-Token' = $secrets.'Security:CapabilityToken' }
$metrics = Invoke-RestMethod 'http://localhost:5035/api/observability/metrics' -Headers $headers
$metrics | ConvertTo-Json -Depth 8
Remove-Variable secretsPath, secrets, headers, metrics
```

Pass:

- `engine.status` is `ready`.
- `engine.plugins.kubernetes-sandbox.status` is `healthy`.
- `engine.auditChainOk` is `true`.
- `pendingSandboxExecutions` is `0` after completion.

## 11. Automated full regression

Close Avalonia and stop the API with `Ctrl+C`. Keep Docker, Ollama and k3d running.

```powershell
.\scripts\release_check.ps1

$env:WESA_TEST_PYTHON = (Resolve-Path 'engine\.venv\Scripts\python.exe').Path
dotnet test tests/Wesa.Harness.Tests/Wesa.Harness.Tests.csproj --filter "Category=EngineProcess"
dotnet test tests/Wesa.Harness.Tests/Wesa.Harness.Tests.csproj --filter "Category=PostgreSQL"
dotnet test tests/Wesa.Harness.Tests/Wesa.Harness.Tests.csproj --filter "Category=Neon"
dotnet test tests/Wesa.Harness.Tests/Wesa.Harness.Tests.csproj --filter "Category=NeonPipeline"
dotnet test tests/Wesa.Harness.Tests/Wesa.Harness.Tests.csproj --filter "Category=NeonSandbox"
dotnet test tests/Wesa.Harness.Tests/Wesa.Harness.Tests.csproj --filter "Category=NeonRecovery"
Remove-Item Env:WESA_TEST_PYTHON
```

Pass:

- Release checks pass and generate both SBOMs.
- Every category passes.
- Audit verification remains valid.

## Final pass criteria

- API and Avalonia connect.
- Secured task, attachment, pipeline and sandbox compile pass.
- Unsafe output path cannot escape `staging/`.
- Audit and Kubernetes plugin remain healthy.
- Release and all live integration categories pass.

