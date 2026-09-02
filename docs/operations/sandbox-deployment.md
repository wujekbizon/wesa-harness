# K3s Sandbox Deployment

## Current status

The sandbox code path is complete and enabled against the local k3d validation cluster. It never falls back to local Windows execution. Any new environment still requires a kubeconfig stored outside `WesaTeam` and a verified NetworkPolicy controller.

## Security controls

- Dedicated `wesa-sandbox` namespace.
- Restricted Pod Security labels.
- Default-deny ingress and egress NetworkPolicy.
- Non-root UID/GID 65532.
- `RuntimeDefault` seccomp.
- All Linux capabilities dropped.
- Privilege escalation disabled.
- Read-only container root filesystem.
- Read-only ConfigMap source mount.
- Separate size-limited `emptyDir` work and `/tmp` mounts.
- No service-account token or service links.
- Fixed commands and immutable image digest.
- CPU, memory, deadline, output and namespace quotas.
- Jobs/config maps deleted after result capture; TTL is a fallback.

## Deployment

### Local k3d validation cluster

The approved local validation cluster is defined in `infra/k3d/wesa-sandbox.yaml`. It pins k3d/K3s behavior and exposes only the Kubernetes API on loopback port 6550. It has no host bind mounts, workload ports or load balancer. k3d's internal named image volume remains enabled because k3d 5.9 requires it for tools-node bootstrap.

K3s requires a usable default route, so the cluster uses k3d's managed Docker network rather than a Docker `--internal` network. K3s embeds kube-router and applies NetworkPolicy asynchronously. Every sandbox Job therefore starts a trusted `network-policy-barrier` init container. It waits ten seconds, then verifies that both the Kubernetes API service and a public address are unreachable. Agent code starts only after both checks are blocked; otherwise the init container exits non-zero. A fixed Python supervisor launches agent code as a child and force-kills it at the profile timeout; this avoids Linux PID 1 signal semantics.

```powershell
.\infra\k3d\bootstrap.ps1 -EnableHarness -RunLiveSmoke
```

The script creates or starts the cluster, imports the pinned runtime by digest, applies all policies, runs the mandatory egress drill, optionally runs the live compile/egress/timeout suite, and stores only the kubeconfig path in .NET user-secrets. It never prints kubeconfig contents.

This same-PC cluster validates integration and policies. It is not the final production boundary for hostile code.

From a machine with an authorized Lumio kubeconfig:

```powershell
kubectl apply -f infra/sandbox/namespace.yaml
kubectl apply -f infra/sandbox/network-policy.yaml
kubectl apply -f infra/sandbox/resource-policy.yaml
```

Verify the K3s server was not started with `--disable-network-policy`. K3s normally ships an embedded kube-router NetworkPolicy controller, but a policy object existing in the API is not proof that traffic is blocked.

## Mandatory network drill

1. Create a disposable pod in `wesa-sandbox` using the allowlisted Python image and restricted security context.
2. Attempt an outbound TCP connection to a known reachable LAN/internet address.
3. The connection must time out/fail.
4. Attempt inbound connectivity to the pod; it must fail.
5. Delete the drill pod.

Do not enable Harness sandbox until this drill passes.

## Harness configuration

Store kubeconfig outside the workspace with restrictive Windows ACLs. Store only its path in user-secrets:

```powershell
dotnet user-secrets set "Sandbox:Kubeconfig" "C:\secure\wesa-k3s.yaml" --project src/Wesa.Harness.Api
dotnet user-secrets set "Sandbox:Enabled" "true" --project src/Wesa.Harness.Api
```

Restart API. `GET /api/observability/metrics` must report the `kubernetes-sandbox` plugin as healthy and enabled.

## First execution drill

1. Generate a harmless D1 Python artifact and obtain Gate outcome `accept`.
2. In Desktop click **Sandbox compile**.
3. Confirm a Job appears only in `wesa-sandbox`.
4. Confirm exit code 0, result JSON passes Artifact Gate and the Job disappears.
5. Repeat with infinite-loop code under `python-run`; deadline must terminate it.
6. Repeat with network code; egress must fail.

## Rollback

```powershell
dotnet user-secrets set "Sandbox:Enabled" "false" --project src/Wesa.Harness.Api
kubectl delete namespace wesa-sandbox
```

Disabling sandbox does not affect task generation or the legacy ecosystem.
