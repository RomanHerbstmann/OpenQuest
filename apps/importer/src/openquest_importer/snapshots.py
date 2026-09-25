"""Storage for raw downloads. Every sync keeps the files exactly as downloaded.

Files are content-addressed, so identical downloads share one file. A snapshot
with extra files (e.g. reference data) is stored as its files plus a small JSON
manifest; ``sync_run.snapshot_key`` then points to the manifest.
"""

from __future__ import annotations

import hashlib
import json
from pathlib import Path
from typing import Protocol

from openquest_importer.adapters.base import Snapshot, SnapshotFile

MANIFEST_EXTENSION = "manifest.json"


class SnapshotStore(Protocol):
    def save(self, source_key: str, snapshot: Snapshot) -> str:
        """Store the snapshot and return its key (saved as ``sync_run.snapshot_key``)."""

    def load(self, key: str) -> Snapshot: ...


def file_key(source_key: str, content: bytes, extension: str) -> str:
    return f"{source_key}/{hashlib.sha256(content).hexdigest()}.{extension}"


class LocalSnapshotStore:
    """Stores snapshots on the local file system. An S3 / MinIO store can
    implement the same protocol later."""

    def __init__(self, root: Path) -> None:
        self.root = root

    def save(self, source_key: str, snapshot: Snapshot) -> str:
        main_key = self._put(source_key, snapshot.content, snapshot.extension)
        if not snapshot.extras:
            return main_key
        manifest = {
            "main": main_key,
            "extras": {
                name: self._put(source_key, extra.content, extra.extension)
                for name, extra in sorted(snapshot.extras.items())
            },
        }
        return self._put(source_key, json.dumps(manifest, indent=2).encode(), MANIFEST_EXTENSION)

    def load(self, key: str) -> Snapshot:
        if not key.endswith("." + MANIFEST_EXTENSION):
            return Snapshot(content=self.read_bytes(key), extension=_extension(key))
        manifest = json.loads(self.read_bytes(key))
        return Snapshot(
            content=self.read_bytes(manifest["main"]),
            extension=_extension(manifest["main"]),
            extras={
                name: SnapshotFile(content=self.read_bytes(extra_key), extension=_extension(extra_key))
                for name, extra_key in manifest["extras"].items()
            },
        )

    def read_bytes(self, key: str) -> bytes:
        return (self.root / key).read_bytes()

    def _put(self, source_key: str, content: bytes, extension: str) -> str:
        key = file_key(source_key, content, extension)
        path = self.root / key
        if not path.exists():
            path.parent.mkdir(parents=True, exist_ok=True)
            tmp = path.with_name(path.name + ".tmp")
            tmp.write_bytes(content)
            tmp.replace(path)
        return key


def _extension(key: str) -> str:
    return key.rsplit("/", 1)[-1].split(".", 1)[1]
