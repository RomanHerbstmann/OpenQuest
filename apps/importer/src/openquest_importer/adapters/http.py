"""Downloading source files, or reading local copies for offline development."""

from __future__ import annotations

import time
import urllib.request
from pathlib import Path
from typing import Any, Mapping

from openquest_importer import __version__
from openquest_importer.adapters.base import AdapterError

USER_AGENT = f"OpenQuest-Importer/{__version__} (+https://github.com/RomanHerbstmann/OpenQuest)"


def download(url: str, timeout_s: float = 120, retries: int = 2) -> bytes:
    """GET ``url``; retries transient failures with a short back-off."""
    request = urllib.request.Request(url, headers={"User-Agent": USER_AGENT})
    for attempt in range(retries + 1):
        try:
            with urllib.request.urlopen(request, timeout=timeout_s) as response:
                return response.read()
        except OSError as exc:
            if attempt == retries:
                raise AdapterError(f"Download from {url} failed: {exc}") from exc
            time.sleep(2**attempt)
    raise AssertionError("unreachable")


def read_source(options: Mapping[str, Any], file_option: str, url: str) -> bytes:
    """The local file ``options[file_option]`` if set, else the download of ``url``."""
    path = options.get(file_option)
    if path:
        return Path(path).read_bytes()
    return download(url, float(options.get("timeout_s", 120)))
