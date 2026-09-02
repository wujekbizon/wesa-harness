from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path

from .audit import AuditLog, resolve_audit_path
from .capabilities import CapabilityError, load_manifest_directory
from .protected import ProtectedRegistry


@dataclass(frozen=True, slots=True)
class StartupResult:
    ok: bool
    checks: tuple[str, ...]
    errors: tuple[str, ...]


def run_startup_checks(root: Path) -> StartupResult:
    checks: list[str] = []
    errors: list[str] = []

    audit = AuditLog(resolve_audit_path(root)).verify()
    if audit.ok:
        checks.append(f"audit:{audit.records}")
    else:
        errors.append(f"audit:{audit.error}")

    try:
        manifests = load_manifest_directory(root / "config" / "manifests")
        checks.append(f"manifests:{len(manifests)}")
    except (CapabilityError, OSError, ValueError) as exc:
        errors.append(f"manifests:{exc}")

    try:
        registry = ProtectedRegistry.from_file(root / "config" / "protected.json")
        checks.append(f"protected:{len(registry.patterns)}")
    except (OSError, ValueError) as exc:
        errors.append(f"protected:{exc}")

    checks.append("vault:deferred-no-provider-calls")
    return StartupResult(not errors, tuple(checks), tuple(errors))
