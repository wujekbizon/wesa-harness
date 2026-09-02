param(
    [Parameter(Mandatory = $true)]
    [string]$DestinationDirectory
)

$ErrorActionPreference = "Stop"
$harnessRoot = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
$destination = [IO.Path]::GetFullPath($DestinationDirectory)
if ($destination.StartsWith($harnessRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Backup destination must be outside the Harness workspace"
}
New-Item -ItemType Directory -Path $destination -Force | Out-Null
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$temporary = Join-Path ([IO.Path]::GetTempPath()) "wesa-harness-backup-$stamp"
$archive = Join-Path $destination "wesa-harness-state-$stamp.zip"
New-Item -ItemType Directory -Path $temporary -Force | Out-Null
try {
    $relativeFiles = @(
        "state/security.db",
        "state/memory-retention.last",
        "state/plugins.json",
        "logs/AUDIT.jsonl",
        "config/protected.json",
        "config/memory-seed.json",
        "config/sandbox-policy.json",
        "config/secret-inventory.json",
        "dotnet-tools.json",
        "engine/uv.lock"
    )
    foreach ($relative in $relativeFiles) {
        $source = Join-Path $harnessRoot $relative
        if (-not (Test-Path -LiteralPath $source)) { continue }
        $target = Join-Path $temporary $relative
        New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force | Out-Null
        Copy-Item -LiteralPath $source -Destination $target
    }
    Copy-Item -LiteralPath (Join-Path $harnessRoot "config/manifests") -Destination (Join-Path $temporary "config/manifests") -Recurse
    Copy-Item -LiteralPath (Join-Path $harnessRoot "config/profiles") -Destination (Join-Path $temporary "config/profiles") -Recurse
    Copy-Item -LiteralPath (Join-Path $harnessRoot "config/workflows") -Destination (Join-Path $temporary "config/workflows") -Recurse
    Copy-Item -LiteralPath (Join-Path $harnessRoot "plugins") -Destination (Join-Path $temporary "plugins") -Recurse

    Push-Location (Join-Path $harnessRoot "engine")
    try {
        $auditSummary = uv run --locked python -c "from pathlib import Path; import json; from wesa_engine.kernel.audit import AuditLog; v=AuditLog(Path('../logs/AUDIT.jsonl')).verify(); print(json.dumps({'ok':v.ok,'records':v.records,'head':v.head}))"
        if ($LASTEXITCODE -ne 0) { throw "Audit verification failed" }
    }
    finally {
        Pop-Location
    }
    Set-Content -LiteralPath (Join-Path $temporary "audit-head.json") -Value $auditSummary -Encoding utf8
    Compress-Archive -Path (Join-Path $temporary "*") -DestinationPath $archive -CompressionLevel Optimal
}
finally {
    if (Test-Path -LiteralPath $temporary) {
        Remove-Item -LiteralPath $temporary -Recurse -Force
    }
}

Write-Output "Backup created: $archive"
