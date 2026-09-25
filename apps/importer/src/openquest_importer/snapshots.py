"""Storage for raw downloads. Every sync keeps the file exactly as downloaded."""

from __future__ import annotations

import hashlib
from pathlib import Path
from typing import Protocol

from openquest_importer.adapters.base import Snapshot


class SnapshotStore(Protocol):
    def save(self, source_key: str, snapshot: Snapshot) -> str:
        """Store the snapshot and return its key (saved as ``sync_run.snapshot_key``)."""

    def load(self, key: str) -> bytes: ...


def snapshot_key(source_key: str, snapshot: Snapshot) -> str:
    """Keys are content-addressed: identical downloads share one file."""
    digest = hashlib.sha256(snapshot.content).hexdigest()
    return f"{source_key}/{digest}.{snapshot.extension}"


class LocalSnapshotStore:
    """Stores snapshots on the local file system. An S3 / MinIO store can
    implement the same protocol later."""

    def __init__(self, root: Path) -> None:
        self.root = root

    def save(self, source_key: str, snapshot: Snapshot) -> str:
        key = snapshot_key(source_key, snapshot)
        path = self.root / key
        if not path.exists():
            path.parent.mkdir(parents=True, exist_ok=True)
            tmp = path.with_suffix(path.suffix + ".tmp")
            tmp.write_bytes(snapshot.content)
            tmp.replace(path)
        return key

    def load(self, key: str) -> bytes:
        return (self.root / key).read_bytes()
