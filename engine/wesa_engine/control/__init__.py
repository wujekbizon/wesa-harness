"""Authenticated control-plane adapters."""

from .headless import HeadlessCommand, HeadlessCommandAuthenticator

__all__ = ["HeadlessCommand", "HeadlessCommandAuthenticator"]
