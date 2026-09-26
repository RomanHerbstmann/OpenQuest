// Drops metadata (EXIF, XMP, IPTC, text chunks) from uploaded images before they reach any model.
// The browser already re-encodes photos through a canvas, this is the server side safety net.
// Pixels are left untouched; WebP is passed through as is.

const JPEG_DROP_MARKERS = new Set([0xe1, 0xed, 0xfe]); // APP1 (EXIF/XMP), APP13 (IPTC), COM
const PNG_DROP_CHUNKS = new Set(['eXIf', 'tEXt', 'zTXt', 'iTXt', 'tIME']);

export function stripImageMetadata(bytes: Uint8Array): Uint8Array {
  if (bytes[0] === 0xff && bytes[1] === 0xd8) return stripJpeg(bytes);
  if (bytes[0] === 0x89 && bytes[1] === 0x50 && bytes[2] === 0x4e && bytes[3] === 0x47) return stripPng(bytes);
  return bytes;
}

function stripJpeg(bytes: Uint8Array): Uint8Array {
  const parts: Uint8Array[] = [bytes.subarray(0, 2)];
  let offset = 2;
  while (offset + 4 <= bytes.length) {
    if (bytes[offset] !== 0xff) return bytes; // malformed, keep the original and let the verifier judge it
    const marker = bytes[offset + 1]!;
    // Start of scan: the rest is entropy coded image data.
    if (marker === 0xda) { parts.push(bytes.subarray(offset)); return concat(parts); }
    const length = (bytes[offset + 2]! << 8) | bytes[offset + 3]!;
    if (length < 2 || offset + 2 + length > bytes.length) return bytes;
    if (!JPEG_DROP_MARKERS.has(marker)) parts.push(bytes.subarray(offset, offset + 2 + length));
    offset += 2 + length;
  }
  return bytes;
}

function stripPng(bytes: Uint8Array): Uint8Array {
  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  const parts: Uint8Array[] = [bytes.subarray(0, 8)];
  let offset = 8;
  while (offset + 12 <= bytes.length) {
    const length = view.getUint32(offset);
    const type = String.fromCharCode(...bytes.subarray(offset + 4, offset + 8));
    const end = offset + 12 + length;
    if (end > bytes.length) return bytes;
    if (!PNG_DROP_CHUNKS.has(type)) parts.push(bytes.subarray(offset, end));
    offset = end;
    if (type === 'IEND') break;
  }
  return concat(parts);
}

function concat(parts: Uint8Array[]): Uint8Array {
  const out = new Uint8Array(parts.reduce((sum, part) => sum + part.length, 0));
  let offset = 0;
  for (const part of parts) { out.set(part, offset); offset += part.length; }
  return out;
}
