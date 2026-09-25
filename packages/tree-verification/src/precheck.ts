import type { VerificationConfig } from "./config.ts";
import { haversineM } from "./geo/distance.ts";
import type { Reason, VerificationInput } from "./types.ts";

type Mime = "image/jpeg" | "image/png" | "image/webp";

export interface PrecheckResult {
  /** Normalized image, ready to send to a vision model. */
  image?: { base64: string; mimeType: Mime; bytes: number };
  distanceToExpectedM?: number;
  geofenceRadiusM: number;
  reasons: Reason[];
}

export function detectMime(bytes: Uint8Array): Mime | null {
  if (bytes[0] === 0xff && bytes[1] === 0xd8 && bytes[2] === 0xff) return "image/jpeg";
  if (bytes[0] === 0x89 && bytes[1] === 0x50 && bytes[2] === 0x4e && bytes[3] === 0x47) return "image/png";
  const riff = String.fromCharCode(...bytes.subarray(0, 4));
  const webp = String.fromCharCode(...bytes.subarray(8, 12));
  if (riff === "RIFF" && webp === "WEBP") return "image/webp";
  return null;
}

function toBytes(data: Uint8Array | string): Uint8Array {
  if (typeof data !== "string") return data;
  const b64 = data.replace(/^data:[^;]+;base64,/, "");
  return Uint8Array.from(Buffer.from(b64, "base64"));
}

/** Cheap, deterministic checks that run before any paid model call. */
export function precheck(input: VerificationInput, config: VerificationConfig, now: Date = new Date()): PrecheckResult {
  const reasons: Reason[] = [];
  const geofenceRadiusM = input.geofenceRadiusM ?? config.geofenceRadiusM;
  const result: PrecheckResult = { geofenceRadiusM, reasons };

  const bytes = toBytes(input.image.data);
  const mimeType = detectMime(bytes);
  if (bytes.length === 0 || !mimeType) {
    reasons.push({ code: "invalid_image", severity: "hard", detail: "not a JPEG, PNG or WebP image" });
  } else if (bytes.length > config.maxImageBytes) {
    reasons.push({ code: "image_too_large", severity: "hard", detail: `${bytes.length} bytes > ${config.maxImageBytes}` });
  } else {
    // The declared mimeType is ignored on purpose: the magic bytes are the truth.
    result.image = { base64: Buffer.from(bytes).toString("base64"), mimeType, bytes: bytes.length };
  }

  if (input.playerPosition && input.expected) {
    const d = haversineM(input.playerPosition, input.expected.position);
    result.distanceToExpectedM = d;
    const accuracy = input.playerPosition.accuracyM ?? 0;
    // Give the player the benefit of the GPS doubt, but never more than the radius itself.
    const tolerance = Math.min(accuracy, geofenceRadiusM);
    if (d > geofenceRadiusM + tolerance) {
      reasons.push({ code: "outside_geofence", severity: "hard", detail: `${d.toFixed(1)} m > ${geofenceRadiusM} m` });
    }
    if (accuracy > geofenceRadiusM) {
      reasons.push({ code: "gps_inaccurate", severity: "soft", detail: `accuracy ${accuracy.toFixed(0)} m` });
    }
  }

  if (input.capturedAt) {
    const ageMin = (now.getTime() - input.capturedAt.getTime()) / 60_000;
    if (ageMin > config.maxCaptureAgeMinutes || ageMin < -2) {
      reasons.push({ code: "stale_capture", severity: "soft", detail: `captured ${ageMin.toFixed(0)} min ago` });
    }
  }

  return result;
}
