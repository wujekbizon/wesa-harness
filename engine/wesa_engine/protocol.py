from __future__ import annotations

from dataclasses import dataclass
from typing import Any, Mapping


PROTOCOL_VERSION = "1.0"


class ProtocolError(ValueError):
    def __init__(self, code: str, message: str):
        super().__init__(message)
        self.code = code
        self.message = message


@dataclass(frozen=True, slots=True)
class RpcRequest:
    request_id: str
    method: str
    params: Mapping[str, Any]

    @classmethod
    def from_mapping(cls, value: Mapping[str, Any]) -> "RpcRequest":
        request_id = value.get("id")
        method = value.get("method")
        params = value.get("params", {})
        if not isinstance(request_id, (str, int)):
            raise ProtocolError("INVALID_REQUEST", "id must be a string or integer")
        if not isinstance(method, str) or not method:
            raise ProtocolError("INVALID_REQUEST", "method must be a non-empty string")
        if not isinstance(params, Mapping):
            raise ProtocolError("INVALID_REQUEST", "params must be an object")
        return cls(str(request_id), method, dict(params))


def success(request_id: str, result: Mapping[str, Any]) -> dict[str, Any]:
    return {"id": request_id, "result": dict(result)}


def failure(request_id: str | None, code: str, message: str) -> dict[str, Any]:
    response: dict[str, Any] = {"error": {"code": code, "message": message}}
    if request_id is not None:
        response["id"] = request_id
    return response

