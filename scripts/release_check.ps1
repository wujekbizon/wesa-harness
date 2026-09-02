param(
    [switch]$SkipVulnerabilityAudit
)

$ErrorActionPreference = "Stop"
$harnessRoot = Split-Path $PSScriptRoot -Parent
$artifactRoot = Join-Path $harnessRoot "artifacts/release"
New-Item -ItemType Directory -Path $artifactRoot -Force | Out-Null
Push-Location $harnessRoot
try {
    dotnet tool restore
    if ($LASTEXITCODE -ne 0) { throw "dotnet tool restore failed" }
    dotnet restore Wesa.Harness.slnx --locked-mode
    if ($LASTEXITCODE -ne 0) { throw "dotnet locked restore failed" }
    dotnet build Wesa.Harness.slnx -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw "Release build failed" }
    dotnet test tests/Wesa.Harness.Tests/Wesa.Harness.Tests.csproj -c Release --no-restore --filter "Category!=PostgreSQL&Category!=Neon&Category!=NeonPipeline&Category!=NeonSandbox&Category!=NeonRecovery&Category!=EngineProcess"
    if ($LASTEXITCODE -ne 0) { throw "Offline .NET tests failed" }
    dotnet tool run dotnet-CycloneDX -- Wesa.Harness.slnx --output $artifactRoot --filename sbom-dotnet.cdx.json --output-format Json --configuration Release --disable-package-restore
    if ($LASTEXITCODE -ne 0) { throw ".NET SBOM generation failed" }
    if (-not $SkipVulnerabilityAudit) {
        dotnet list Wesa.Harness.slnx package --vulnerable --include-transitive
        if ($LASTEXITCODE -ne 0) { throw ".NET vulnerability audit failed" }
    }

    Push-Location (Join-Path $harnessRoot "engine")
    try {
        uv sync --locked --extra dev
        if ($LASTEXITCODE -ne 0) { throw "Python locked sync failed" }
        uv run --locked --extra dev pytest tests
        if ($LASTEXITCODE -ne 0) { throw "Python tests failed" }
        uv export --quiet --locked --all-extras --format cyclonedx1.5 --output-file (Join-Path $artifactRoot "sbom-python.cdx.json")
        if ($LASTEXITCODE -ne 0) { throw "Python SBOM generation failed" }
        if (-not $SkipVulnerabilityAudit) {
            uv run --locked --extra dev pip-audit
            if ($LASTEXITCODE -ne 0) { throw "Python vulnerability audit failed" }
        }
        uv run --locked python -c "from pathlib import Path; from wesa_engine.kernel.audit import AuditLog; v=AuditLog(Path('../logs/AUDIT.jsonl')).verify(); print(f'audit_ok={v.ok} records={v.records}'); raise SystemExit(0 if v.ok else 1)"
        if ($LASTEXITCODE -ne 0) { throw "Audit chain verification failed" }
    }
    finally {
        Pop-Location
    }
}
finally {
    Pop-Location
}

Write-Output "Release checks passed. SBOMs: $artifactRoot"
