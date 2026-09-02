from __future__ import annotations

import fnmatch
import json
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Mapping


@dataclass(frozen=True, slots=True)
class ProtectedRegistry:
    patterns: tuple[str, ...]
    greg_approval: tuple[str, ...]

    @classmethod
    def from_file(cls, path: Path) -> "ProtectedRegistry":
        data: Any = json.loads(path.read_text(encoding="utf-8"))
        if not isinstance(data, Mapping):
            raise ValueError("protected registry root must be an object")
        patterns = data.get("patterns", [])
        greg = data.get("gregApproval", [])
        if not isinstance(patterns, list) or not isinstance(greg, list):
            raise ValueError("protected registry lists are invalid")
        return cls(tuple(str(value) for value in patterns), tuple(str(value) for value in greg))

    def is_protected(self, relative_path: str) -> bool:
        normalized = relative_path.replace("\\", "/")
        return any(fnmatch.fnmatch(normalized, pattern) for pattern in self.patterns)

    def requires_greg(self, relative_path: str) -> bool:
        normalized = relative_path.replace("\\", "/")
        return any(fnmatch.fnmatch(normalized, pattern) for pattern in self.greg_approval)

