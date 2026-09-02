"""Kernel-owned durable memory boundary."""

from .service import FactPromotionResult, MemoryContext, MemoryService, assemble_memory_context

__all__ = ["FactPromotionResult", "MemoryContext", "MemoryService", "assemble_memory_context"]
