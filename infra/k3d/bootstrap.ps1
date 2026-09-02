[CmdletBinding()]
param(
    [switch]$EnableHarness,
    [switch]$RunLiveSmoke
)

$ErrorActionPreference = "Stop"
$clusterName = "wesa-sandbox"
$serverName = "k3d-wesa-sandbox-server-0"
$pythonTag = "python:3.13-alpine"
$pythonDigest = "docker.io/library/python@sha256:42825e7ec3437b3bce923c237484eb23d32128476e18307d2f48951bf86f1db2"
$harnessRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path

foreach ($command in "docker", "k3d", "kubectl") {
    if (-not (Get-Command $command -ErrorAction SilentlyContinue)) {
        throw "$command is not installed or not on PATH"
    }
}

Push-Location $harnessRoot
try {
    & docker info | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Docker Desktop Linux engine is unavailable"
    }

    $clusterExists = (& k3d cluster list --no-headers) -match "^$clusterName\s"
    if (-not $clusterExists) {
        & k3d cluster create --config infra/k3d/wesa-sandbox.yaml
        if ($LASTEXITCODE -ne 0) { throw "k3d cluster creation failed" }
    }
    else {
        & k3d cluster start $clusterName
        if ($LASTEXITCODE -ne 0) { throw "k3d cluster start failed" }
    }

    & docker image inspect $pythonDigest | Out-Null
    if ($LASTEXITCODE -ne 0) {
        & docker pull $pythonDigest
        if ($LASTEXITCODE -ne 0) { throw "pinned Python image pull failed" }
    }
    & docker tag $pythonDigest $pythonTag
    & k3d image import $pythonTag --cluster $clusterName
    if ($LASTEXITCODE -ne 0) { throw "Python image import failed" }

    & docker exec $serverName ctr -n k8s.io images check "name==$pythonDigest" | Out-Null
    if ($LASTEXITCODE -ne 0) {
        & docker exec $serverName ctr -n k8s.io images tag "docker.io/library/$pythonTag" $pythonDigest
        if ($LASTEXITCODE -ne 0) { throw "immutable image alias creation failed" }
    }

    $kubeconfigPath = (& k3d kubeconfig write $clusterName).Trim()
    & kubectl --kubeconfig $kubeconfigPath wait --for=condition=Ready "node/$serverName" --timeout=60s
    if ($LASTEXITCODE -ne 0) { throw "K3s node did not become ready" }
    & kubectl --kubeconfig $kubeconfigPath apply -f infra/sandbox/namespace.yaml
    & kubectl --kubeconfig $kubeconfigPath apply -f infra/sandbox/network-policy.yaml
    & kubectl --kubeconfig $kubeconfigPath apply -f infra/sandbox/resource-policy.yaml

    & kubectl --kubeconfig $kubeconfigPath delete pod/wesa-network-drill -n wesa-sandbox --ignore-not-found=true --wait=true | Out-Null
    & kubectl --kubeconfig $kubeconfigPath apply -f infra/sandbox/network-drill.yaml
    & kubectl --kubeconfig $kubeconfigPath wait --for=jsonpath='{.status.phase}'=Succeeded pod/wesa-network-drill -n wesa-sandbox --timeout=60s
    if ($LASTEXITCODE -ne 0) { throw "network drill did not succeed" }
    $drillOutput = (& kubectl --kubeconfig $kubeconfigPath logs pod/wesa-network-drill -n wesa-sandbox -c check).Trim()
    & kubectl --kubeconfig $kubeconfigPath delete pod/wesa-network-drill -n wesa-sandbox --wait=true | Out-Null
    if ($drillOutput -ne "EGRESS_BLOCKED") { throw "network drill failed: $drillOutput" }

    if ($RunLiveSmoke) {
        Push-Location (Join-Path $harnessRoot "engine")
        try {
            & uv run --locked python scripts/live_sandbox_smoke.py --kubeconfig $kubeconfigPath
            if ($LASTEXITCODE -ne 0) { throw "live sandbox smoke failed" }
        }
        finally {
            Pop-Location
        }
    }

    if ($EnableHarness) {
        & dotnet user-secrets set "Sandbox:Kubeconfig" $kubeconfigPath --project src/Wesa.Harness.Api
        & dotnet user-secrets set "Sandbox:Enabled" "true" --project src/Wesa.Harness.Api
    }

    Write-Output "cluster=READY policy=PASS kubeconfig=$kubeconfigPath"
}
finally {
    Pop-Location
}
