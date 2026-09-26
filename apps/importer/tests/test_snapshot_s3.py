"""The S3 snapshot store (moto stands in for MinIO) and the configuration that selects it."""

import json

import pytest

from openquest_importer.adapters.base import Snapshot, SnapshotFile
from openquest_importer.config import ConfigError, S3SnapshotConfig, load_config
from openquest_importer.snapshots import LocalSnapshotStore, S3SnapshotStore, create_store

moto = pytest.importorskip("moto")
boto3 = pytest.importorskip("boto3")


@pytest.fixture
def s3(monkeypatch):
    for key in ("AWS_ACCESS_KEY_ID", "AWS_SECRET_ACCESS_KEY"):
        monkeypatch.setenv(key, "test")
    with moto.mock_aws():
        yield boto3.client("s3", region_name="us-east-1")


def config(prefix="") -> S3SnapshotConfig:
    return S3SnapshotConfig(bucket="snapshots", prefix=prefix, access_key="test", secret_key="test")


def test_a_snapshot_is_stored_under_its_content_hash_and_read_back(s3):
    store = S3SnapshotStore(config())
    key = store.save("trees", Snapshot(content=b"raw download", extension="geojson"))
    assert key.startswith("trees/") and key.endswith(".geojson")
    assert store.load(key) == Snapshot(content=b"raw download", extension="geojson")
    # the bucket did not exist before: the store made it
    assert s3.get_object(Bucket="snapshots", Key=key)["Body"].read() == b"raw download"


def test_identical_downloads_share_one_object(s3):
    store = S3SnapshotStore(config())
    first = store.save("trees", Snapshot(content=b"same", extension="geojson"))
    second = store.save("trees", Snapshot(content=b"same", extension="geojson"))
    assert first == second
    assert len(s3.list_objects_v2(Bucket="snapshots")["Contents"]) == 1


def test_reference_files_are_stored_with_a_manifest_that_the_api_can_read(s3):
    store = S3SnapshotStore(config(prefix="importer/"))
    key = store.save("trees", Snapshot(content=b"main", extension="geojson",
                                       extras={"streets": SnapshotFile(content=b"a,b", extension="csv")}))
    assert key.endswith(".manifest.json")
    manifest = json.loads(s3.get_object(Bucket="snapshots", Key="importer/" + key)["Body"].read())
    assert set(manifest) == {"main", "extras"} and set(manifest["extras"]) == {"streets"}
    loaded = store.load(key)
    assert loaded.content == b"main" and loaded.extras["streets"].content == b"a,b"
    assert all(o["Key"].startswith("importer/") for o in s3.list_objects_v2(Bucket="snapshots")["Contents"])


def test_local_and_s3_stores_use_the_same_keys(s3, tmp_path):
    snapshot = Snapshot(content=b"x", extension="geojson", extras={"e": SnapshotFile(content=b"y", extension="json")})
    assert LocalSnapshotStore(tmp_path).save("trees", snapshot) == S3SnapshotStore(config()).save("trees", snapshot)


def write_config(tmp_path, body: str):
    path = tmp_path / "importer.toml"
    path.write_text(body, encoding="utf-8")
    return path


def test_the_default_backend_is_the_local_directory(tmp_path, monkeypatch):
    monkeypatch.delenv("OPENQUEST_SNAPSHOT_BACKEND", raising=False)
    cfg = load_config(write_config(tmp_path, '[snapshots]\ndir = "snaps"\n'))
    assert cfg.snapshot_s3 is None and isinstance(create_store(cfg), LocalSnapshotStore)


def test_s3_is_selected_by_the_file_or_the_environment(tmp_path, monkeypatch, s3):
    monkeypatch.delenv("OPENQUEST_SNAPSHOT_BACKEND", raising=False)
    cfg = load_config(write_config(tmp_path, '[snapshots]\nbackend = "s3"\n[snapshots.s3]\nbucket = "snapshots"\nurl = "http://minio:9000"\nprefix = "p/"\n'))
    assert cfg.snapshot_s3 == S3SnapshotConfig(bucket="snapshots", endpoint_url="http://minio:9000", prefix="p/")

    monkeypatch.setenv("OPENQUEST_SNAPSHOT_BACKEND", "s3")
    monkeypatch.setenv("OPENQUEST_SNAPSHOT_S3_BUCKET", "from-env")
    monkeypatch.setenv("OPENQUEST_SNAPSHOT_S3_ACCESS_KEY", "k")
    monkeypatch.setenv("OPENQUEST_SNAPSHOT_S3_SECRET_KEY", "s")
    cfg = load_config(write_config(tmp_path, ""))
    assert cfg.snapshot_s3.bucket == "from-env" and cfg.snapshot_s3.access_key == "k"
    assert isinstance(create_store(cfg), S3SnapshotStore)


def test_a_wrong_backend_or_a_missing_bucket_is_refused(tmp_path, monkeypatch):
    monkeypatch.delenv("OPENQUEST_SNAPSHOT_BACKEND", raising=False)
    monkeypatch.delenv("OPENQUEST_SNAPSHOT_S3_BUCKET", raising=False)
    with pytest.raises(ConfigError, match="Unknown snapshot backend"):
        load_config(write_config(tmp_path, '[snapshots]\nbackend = "ftp"\n'))
    with pytest.raises(ConfigError, match="needs a bucket"):
        load_config(write_config(tmp_path, '[snapshots]\nbackend = "s3"\n'))
