"""Minimal reader for the GeoTIFFs returned by the Geobasis NRW WCS.

Supports exactly what the WCS delivers: one band, float32, uncompressed,
stored in strips, north-up with ModelPixelScale + ModelTiepoint. Anything else
raises, so a change on the server side fails loudly instead of producing
wrong heights.
"""

from __future__ import annotations

import struct
from dataclasses import dataclass

TAG_WIDTH, TAG_HEIGHT = 256, 257
TAG_BITS, TAG_COMPRESSION = 258, 259
TAG_STRIP_OFFSETS, TAG_SAMPLES, TAG_STRIP_BYTES = 273, 277, 279
TAG_PLANAR, TAG_SAMPLE_FORMAT = 284, 339
TAG_PIXEL_SCALE, TAG_TIEPOINT = 33550, 33922

_TYPES = {1: "B", 3: "H", 4: "I", 11: "f", 12: "d", 16: "Q"}


class GeoTiffError(ValueError):
    pass


@dataclass(frozen=True)
class Raster:
    width: int
    height: int
    origin_x: float  # upper left corner
    origin_y: float
    res_x: float  # > 0
    res_y: float  # < 0 (north-up)
    values: tuple[float, ...]  # row-major

    def value(self, row: int, col: int) -> float:
        return self.values[row * self.width + col]


def read_geotiff(data: bytes) -> Raster:
    if data[:4] == b"II*\x00":
        bo = "<"
    elif data[:4] == b"MM\x00*":
        bo = ">"
    else:
        raise GeoTiffError("Not a TIFF file (maybe an error message from the server)")

    (ifd,) = struct.unpack_from(bo + "I", data, 4)
    (count,) = struct.unpack_from(bo + "H", data, ifd)
    tags: dict[int, tuple] = {}
    for i in range(count):
        tag, typ, n = struct.unpack_from(bo + "HHI", data, ifd + 2 + 12 * i)
        fmt = _TYPES.get(typ)
        if fmt is None:
            continue  # ASCII and other tags we don't need
        size = struct.calcsize(fmt) * n
        offset = ifd + 2 + 12 * i + 8
        if size > 4:
            (offset,) = struct.unpack_from(bo + "I", data, offset)
        tags[tag] = struct.unpack_from(bo + fmt * n, data, offset)

    def one(tag: int, default: int | None = None) -> int:
        if tag not in tags:
            if default is None:
                raise GeoTiffError(f"TIFF tag {tag} missing")
            return default
        return tags[tag][0]

    width, height = one(TAG_WIDTH), one(TAG_HEIGHT)
    if (one(TAG_BITS), one(TAG_SAMPLE_FORMAT, 1), one(TAG_SAMPLES, 1)) != (32, 3, 1):
        raise GeoTiffError("Expected a single float32 band")
    if one(TAG_COMPRESSION, 1) != 1:
        raise GeoTiffError(f"Unsupported compression {one(TAG_COMPRESSION)}")
    if one(TAG_PLANAR, 1) != 1:
        raise GeoTiffError("Unsupported planar configuration")
    if TAG_PIXEL_SCALE not in tags or TAG_TIEPOINT not in tags:
        raise GeoTiffError("GeoTIFF georeferencing (pixel scale / tiepoint) missing")

    pixels = b"".join(
        data[offset:offset + length]
        for offset, length in zip(tags[TAG_STRIP_OFFSETS], tags[TAG_STRIP_BYTES])
    )
    if len(pixels) != width * height * 4:
        raise GeoTiffError("Pixel data has the wrong size")
    scale_x, scale_y = tags[TAG_PIXEL_SCALE][:2]
    tie_i, tie_j, _, tie_x, tie_y, _ = tags[TAG_TIEPOINT][:6]
    return Raster(
        width=width,
        height=height,
        origin_x=tie_x - tie_i * scale_x,
        origin_y=tie_y + tie_j * scale_y,
        res_x=scale_x,
        res_y=-scale_y,
        values=struct.unpack(f"{bo}{width * height}f", pixels),
    )
