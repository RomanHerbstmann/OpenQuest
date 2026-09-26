"""Adapter registry.

Adapters are looked up by their key (``data_source.adapter_key``) through the
``openquest.adapters`` entry point group. Adapters in this package are
registered in ``pyproject.toml``; other cities can publish their own package
with an entry in the same group, without touching this code.
"""

from __future__ import annotations

from importlib.metadata import entry_points
from typing import Any, Mapping

from openquest_importer.adapters.base import BaseAdapter

ENTRY_POINT_GROUP = "openquest.adapters"


class UnknownAdapterError(LookupError):
    pass


def available_adapters() -> dict[str, type[BaseAdapter]]:
    return {ep.name: ep.load() for ep in entry_points(group=ENTRY_POINT_GROUP)}


def create_adapter(key: str, options: Mapping[str, Any] | None = None) -> BaseAdapter:
    matches = entry_points(group=ENTRY_POINT_GROUP, name=key)
    if not matches:
        known = ", ".join(sorted(available_adapters())) or "none"
        raise UnknownAdapterError(f"No adapter '{key}' installed (available: {known})")
    adapter_cls = next(iter(matches)).load()
    if not issubclass(adapter_cls, BaseAdapter):
        raise UnknownAdapterError(f"Entry point '{key}' is not an adapter")
    return adapter_cls(options)
