from __future__ import annotations

import os
from pathlib import Path

from wesa_engine.sandbox import KubernetesSandboxClient, SandboxExecutionRequest, SandboxPolicy

from .contracts import PluginError, PluginHealth, PluginManifest


class KubernetesSandboxPlugin:
    def __init__(self, manifest: PluginManifest):
        self.manifest = manifest
        root = Path(os.getenv("WESA_HARNESS_ROOT", "")) if os.getenv("WESA_HARNESS_ROOT") else manifest.runtime_root
        if root is None:
            raise PluginError("sandbox runtime root is unavailable")
        policy_file = root / str(manifest.config.get("policyFile", "config/sandbox-policy.json"))
        self.policy = SandboxPolicy.from_file(policy_file)
        self.enabled = os.getenv(
            str(manifest.config.get("enabledEnvironmentVariable", "WESA_SANDBOX_ENABLED")),
            "false",
        ).lower() == "true"
        self.kubeconfig = os.getenv(
            str(manifest.config.get("kubeconfigEnvironmentVariable", "WESA_KUBECONFIG")),
            "",
        )
        self.kubectl = os.getenv("WESA_KUBECTL", "kubectl")
        self._started = False

    @property
    def plugin_id(self) -> str:
        return self.manifest.plugin_id

    def start(self) -> None:
        self._started = True

    def stop(self) -> None:
        self._started = False

    def health(self) -> PluginHealth:
        if not self._started:
            return PluginHealth("stopped")
        if not self.enabled:
            return PluginHealth("healthy", "disabled until K3s context is configured")
        if not self.kubeconfig or not Path(self.kubeconfig).is_file():
            return PluginHealth("unhealthy", "configured kubeconfig does not exist")
        return PluginHealth("healthy", "K3s sandbox enabled")

    def execute(self, request: SandboxExecutionRequest):
        if not self.enabled:
            raise PluginError("K3s sandbox execution is disabled")
        if not self.kubeconfig or not Path(self.kubeconfig).is_file():
            raise PluginError("K3s sandbox kubeconfig is unavailable")
        return KubernetesSandboxClient(self.policy, self.kubeconfig, self.kubectl).execute(request)


def create_plugin(manifest: PluginManifest) -> KubernetesSandboxPlugin:
    return KubernetesSandboxPlugin(manifest)
