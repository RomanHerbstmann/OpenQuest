"""Storage for raw downloads. Every sync keeps the files exactly as downloaded.

Files are content-addressed, so identical downloads share one file. A snapshot
with extra files (e.g. reference data) is stored as its files plus a small JSON
manifest; ``sync_run.snapshot_key`` then points to the manifest.
"""

from __future__ import annotations

import hashlib
import json
from pathlib import Path
from typing import TYPE_CHECKING, Protocol

from openquest_importer.adapters.base import Snapshot, SnapshotFile

if TYPE_CHECKING:
    from openquest_importer.config import Config, S3SnapshotConfig

MANIFEST_EXTENSION = "manifest.json"


class SnapshotStore(Protocol):
    def save(self, source_key: str, snapshot: Snapshot) -> str:
        """Store the snapshot and return its key (saved as ``sync_run.snapshot_key``)."""

    def load(self, key: str) -> Snapshot: ...


def file_key(source_key: str, content: bytes, extension: str) -> str:
    return f"{source_key}/{hashlib.sha256(content).hexdigest()}.{extension}"


class _ContentAddressedStore:
    """Manifest and content-addressing logic shared by the stores; a store only says how bytes are kept."""

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

    def _put(self, source_key: str, content: bytes, extension: str) -> str:
        key = file_key(source_key, content, extension)
        if not self._exists(key):
            self._write(key, content)
        return key

    def read_bytes(self, key: str) -> bytes:
        raise NotImplementedError

    def _exists(self, key: str) -> bool:
        raise NotImplementedError

    def _write(self, key: str, content: bytes) -> None:
        raise NotImplementedError


class LocalSnapshotStore(_ContentAddressedStore):
    """Stores snapshots on the local file system."""

    def __init__(self, root: Path) -> None:
        self.root = root

    def read_bytes(self, key: str) -> bytes:
        return (self.root / key).read_bytes()

    def _exists(self, key: str) -> bool:
        return (self.root / key).exists()

    def _write(self, key: str, content: bytes) -> None:
        path = self.root / key
        path.parent.mkdir(parents=True, exist_ok=True)
        tmp = path.with_name(path.name + ".tmp")
        tmp.write_bytes(content)
        tmp.replace(path)


class S3SnapshotStore(_ContentAddressedStore):
    """Stores snapshots in an S3-compatible bucket (MinIO locally), under ``prefix`` + key.

    The keys are the same as in the local store, so ``sync_run.snapshot_key`` means the same everywhere. The API reads the bucket to let
    admins download the raw data of a run. Needs boto3 (``pip install "openquest-importer[s3]"``).
    """

    def __init__(self, config: "S3SnapshotConfig") -> None:
        try:
            import boto3
            from botocore.config import Config as BotoConfig
        except ImportError as exc:  # pragma: no cover - depends on the installation
            raise RuntimeError('The s3 snapshot backend needs boto3: pip install "openquest-importer[s3]"') from exc
        self._bucket = config.bucket
        self._prefix = config.prefix
        self._s3 = boto3.client(
            "s3",
            endpoint_url=config.endpoint_url,
            # without both, boto3 looks for credentials the usual way (environment, profile, instance role)
            **({"aws_access_key_id": config.access_key, "aws_secret_access_key": config.secret_key}
               if config.access_key and config.secret_key else {}),
            region_name=config.region,
            config=BotoConfig(s3={"addressing_style": "path"}, retries={"max_attempts": 5, "mode": "standard"}),
        )
        self._bucket_ready = False

    def read_bytes(self, key: str) -> bytes:
        return self._s3.get_object(Bucket=self._bucket, Key=self._prefix + key)["Body"].read()

    def _exists(self, key: str) -> bool:
        self._ensure_bucket()
        try:
            self._s3.head_object(Bucket=self._bucket, Key=self._prefix + key)
            return True
        except self._s3.exceptions.ClientError as exc:
            if exc.response["Error"]["Code"] in ("404", "NoSuchKey", "NotFound"):
                return False
            raise

    def _write(self, key: str, content: bytes) -> None:
        self._ensure_bucket()
        self._s3.put_object(Bucket=self._bucket, Key=self._prefix + key, Body=content)

    def _ensure_bucket(self) -> None:
        if self._bucket_ready:
            return
        try:
            self._s3.head_bucket(Bucket=self._bucket)
        except self._s3.exceptions.ClientError as exc:
            if exc.response["Error"]["Code"] not in ("404", "NoSuchBucket", "NotFound"):
                raise
            self._s3.create_bucket(Bucket=self._bucket)
        self._bucket_ready = True


def create_store(config: "Config") -> SnapshotStore:
    """The store the configuration asks for: S3 if ``[snapshots] backend = "s3"``, else the local directory."""
    return S3SnapshotStore(config.snapshot_s3) if config.snapshot_s3 else LocalSnapshotStore(config.snapshot_dir)


def _extension(key: str) -> str:
    return key.rsplit("/", 1)[-1].split(".", 1)[1]
