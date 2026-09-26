import type { TreeVerificationResult } from '@openquest/tree-verification';
import type { Tree } from '@/types/tree';
import { speciesForGenus, type SpeciesName } from '@/lib/verificationText';

export type ScanPosition = { lat: number; lng: number; accuracyMeters: number | null };

export type ScanResult =
  | {
      source: 'demo';
      species: string;
      /** Why the demo answer was used instead of the real verification. */
      notice?: string;
    }
  | {
      source: 'verified';
      /** Card species derived from the detected genus, `null` if there is no card for it. */
      species: SpeciesName | null;
      genus: string | null;
      genusProbability: number;
      expectedGenus: string | null;
      position: ScanPosition | null;
      verification: TreeVerificationResult;
      /** The downscaled JPEG that was verified; a live quest submits exactly this photo. */
      photo: Blob;
    };

export type ScanOptions = {
  tree?: Tree | null;
  /** When the photo was taken, sent to the server to flag old gallery pictures. */
  capturedAt?: Date;
};

const MAX_EDGE_PX = 1024;
const JPEG_QUALITY = 0.85;

/**
 * Sends the photo to /api/verify and maps the verification onto a card result.
 * Same call shape as the former prototype mock; falls back to the demo answer when the server has no verification configured.
 */
export async function recognizeTreeForPrototype(image: Blob, expectedSpecies?: string, options: ScanOptions = {}): Promise<ScanResult> {
  if (!image.type.startsWith('image/') || image.size === 0) {
    throw new Error('Bitte verwende ein gültiges Bild.');
  }
  const { tree } = options;

  // The stage prop is not a city tree; it keeps the scripted demo answer.
  if (tree?.presentation) return demoResult(expectedSpecies);

  const [jpeg, position] = await Promise.all([downscaleToJpeg(image), currentPosition()]);

  const form = new FormData();
  form.set('image', jpeg, 'scan.jpg');
  if (position) {
    form.set('lat', String(position.lat));
    form.set('lon', String(position.lng));
    if (position.accuracyMeters !== null) form.set('accuracy', String(Math.round(position.accuracyMeters)));
  }
  if (tree?.quest) {
    // Live quest: the expected tree is the quest asset. A placeholder genus is sent as "unknown".
    form.set('expectedLat', String(tree.lat));
    form.set('expectedLon', String(tree.lng));
    if (tree.quest.genus) form.set('expectedGenus', tree.quest.genus);
  } else if (tree) {
    form.set('treeId', tree.id);
  }
  if (options.capturedAt) form.set('capturedAt', options.capturedAt.toISOString());

  let response: Response;
  try {
    response = await fetch('/api/verify', { method: 'POST', body: form });
  } catch {
    throw new Error('Keine Verbindung zur Prüfung. Bitte versuche es gleich noch einmal.');
  }

  if (response.status === 503) {
    return demoResult(expectedSpecies, 'Die Bilderkennung ist auf diesem Server nicht eingerichtet. Du siehst die Demo-Antwort, dein Foto wurde nicht geprüft.');
  }
  if (response.status === 429) {
    const retry = response.headers.get('Retry-After');
    throw new Error(`Zu viele Scans in kurzer Zeit. Bitte warte ${retry ? `${retry} Sekunden` : 'einen Moment'}.`);
  }
  if (!response.ok) {
    const body = (await response.json().catch(() => null)) as { error?: string } | null;
    throw new Error(errorMessage(body?.error));
  }

  const { result, expected } = (await response.json()) as { result: TreeVerificationResult; expected: { genus: string | null } | null };
  return {
    source: 'verified',
    species: speciesForGenus(result.genus.value, expectedSpecies),
    genus: result.genus.value,
    genusProbability: result.genus.probability,
    expectedGenus: expected?.genus ?? null,
    position,
    verification: result,
    photo: jpeg,
  };
}

function demoResult(expectedSpecies?: string, notice?: string): Promise<ScanResult> {
  return new Promise((resolve) => window.setTimeout(() => resolve({ species: expectedSpecies ?? 'Birke', source: 'demo', ...(notice ? { notice } : {}) }), 1800));
}

function errorMessage(code?: string) {
  switch (code) {
    case 'unsupported_image_type': return 'Dieses Bildformat wird nicht unterstützt. Bitte nimm ein JPEG, PNG oder WebP.';
    case 'image_too_large': return 'Das Bild ist zu groß.';
    case 'unknown_tree': return 'Dieser Baum ist der Prüfung nicht bekannt.';
    default: return 'Die Prüfung ist fehlgeschlagen. Bitte versuche es noch einmal.';
  }
}

/** Redraws the photo on a canvas (longest edge ~1024 px) and encodes it as JPEG; this also drops EXIF and GPS tags. */
async function downscaleToJpeg(image: Blob): Promise<Blob> {
  const bitmap = await createImageBitmap(image, { imageOrientation: 'from-image' }).catch(() => null);
  if (!bitmap) throw new Error('Das Bild konnte nicht gelesen werden. Bitte wähle ein anderes Foto.');
  const scale = Math.min(1, MAX_EDGE_PX / Math.max(bitmap.width, bitmap.height));
  const canvas = document.createElement('canvas');
  canvas.width = Math.max(1, Math.round(bitmap.width * scale));
  canvas.height = Math.max(1, Math.round(bitmap.height * scale));
  const context = canvas.getContext('2d');
  if (!context) { bitmap.close(); throw new Error('Das Bild konnte nicht verarbeitet werden.'); }
  context.drawImage(bitmap, 0, 0, canvas.width, canvas.height);
  bitmap.close();
  const blob = await new Promise<Blob | null>((resolve) => canvas.toBlob(resolve, 'image/jpeg', JPEG_QUALITY));
  if (!blob) throw new Error('Das Bild konnte nicht verarbeitet werden.');
  return blob;
}

/** Browser position for the geofence check; `null` if unavailable or denied (the server then skips the geofence). */
function currentPosition(): Promise<ScanPosition | null> {
  if (typeof navigator === 'undefined' || !navigator.geolocation) return Promise.resolve(null);
  return new Promise((resolve) => {
    navigator.geolocation.getCurrentPosition(
      ({ coords }) => resolve({ lat: coords.latitude, lng: coords.longitude, accuracyMeters: Number.isFinite(coords.accuracy) ? coords.accuracy : null }),
      () => resolve(null),
      { enableHighAccuracy: true, timeout: 8000, maximumAge: 30_000 },
    );
  });
}
