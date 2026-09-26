import { createMuensterTreeProvider } from '@openquest/adapter-de-muenster';
import {
  createOsmTreeProvider,
  resolveConfig,
  verifyTreePhoto,
  type ExpectedTree,
  type NearbyTreeProvider,
  type PlayerPosition,
  type VerificationConfig,
} from '@openquest/tree-verification';
import { trees } from '@/data/trees';
import { clientKey, createRateLimiter } from '@/lib/server/rateLimit';
import { stripImageMetadata } from '@/lib/server/imageMetadata';

export const runtime = 'nodejs';
export const dynamic = 'force-dynamic';

const MAX_IMAGE_BYTES = 5 * 1024 * 1024;
const ALLOWED_TYPES = new Set(['image/jpeg', 'image/png', 'image/webp']);

// Every accepted request costs OpenRouter credits, so keep a tight per IP budget.
const takeToken = createRateLimiter({ limit: 10, windowMs: 60_000 });

let verifier: { config: VerificationConfig; providers: NearbyTreeProvider[] } | null = null;
function getVerifier() {
  verifier ??= { config: resolveConfig(), providers: [createMuensterTreeProvider(), createOsmTreeProvider()] };
  return verifier;
}

export type VerifyErrorCode =
  | 'verification_unavailable'
  | 'rate_limited'
  | 'invalid_request'
  | 'missing_image'
  | 'unsupported_image_type'
  | 'image_too_large'
  | 'unknown_tree'
  | 'verification_failed';

function error(status: number, code: VerifyErrorCode, message: string, headers?: HeadersInit) {
  return Response.json({ error: code, message }, { status, headers });
}

function num(form: FormData, key: string): number | undefined {
  const raw = form.get(key);
  if (typeof raw !== 'string' || raw.trim() === '') return undefined;
  const value = Number(raw);
  return Number.isFinite(value) ? value : Number.NaN;
}

const validLat = (v: number | undefined): v is number => v !== undefined && v >= -90 && v <= 90;
const validLon = (v: number | undefined): v is number => v !== undefined && v >= -180 && v <= 180;

export async function POST(request: Request) {
  if (!process.env.OPENROUTER_API_KEY) {
    return error(503, 'verification_unavailable', 'Photo verification is not configured on this server (OPENROUTER_API_KEY missing).');
  }

  const limit = takeToken(clientKey(request.headers));
  if (!limit.ok) {
    return error(429, 'rate_limited', `Too many scans, retry in ${limit.retryAfterS} s.`, { 'Retry-After': String(limit.retryAfterS) });
  }

  let form: FormData;
  try {
    form = await request.formData();
  } catch {
    return error(400, 'invalid_request', 'Expected multipart/form-data.');
  }

  const image = form.get('image');
  if (!(image instanceof Blob) || image.size === 0) return error(400, 'missing_image', 'Field "image" must be a non empty file.');
  if (!ALLOWED_TYPES.has(image.type)) return error(415, 'unsupported_image_type', 'Only JPEG, PNG or WebP images are accepted.');
  if (image.size > MAX_IMAGE_BYTES) return error(413, 'image_too_large', 'Image must be at most 5 MB.');

  // Player position from browser geolocation, optional.
  const lat = num(form, 'lat');
  const lon = num(form, 'lon');
  const accuracy = num(form, 'accuracy');
  let playerPosition: PlayerPosition | undefined;
  if (lat !== undefined || lon !== undefined) {
    if (!validLat(lat) || !validLon(lon)) return error(400, 'invalid_request', 'lat/lon out of range.');
    playerPosition = { lat, lon, ...(accuracy !== undefined && accuracy >= 0 ? { accuracyM: accuracy } : {}) };
  }

  // Expected tree: a known quest tree by id, or an explicit position with optional genus.
  let expected: ExpectedTree | undefined;
  const treeId = form.get('treeId');
  if (typeof treeId === 'string' && treeId) {
    const tree = trees.find((item) => item.id === treeId);
    if (!tree) return error(404, 'unknown_tree', `No quest tree with id "${treeId}".`);
    expected = { externalId: tree.id, position: { lat: tree.lat, lon: tree.lng }, genus: tree.inventory?.genus ?? null };
  } else {
    const expectedLat = num(form, 'expectedLat');
    const expectedLon = num(form, 'expectedLon');
    if (expectedLat !== undefined || expectedLon !== undefined) {
      if (!validLat(expectedLat) || !validLon(expectedLon)) return error(400, 'invalid_request', 'expectedLat/expectedLon out of range.');
      const genus = form.get('expectedGenus');
      expected = { position: { lat: expectedLat, lon: expectedLon }, genus: typeof genus === 'string' && genus.trim() ? genus.trim() : null };
    }
  }

  const capturedRaw = form.get('capturedAt');
  const capturedAt = typeof capturedRaw === 'string' && capturedRaw ? new Date(capturedRaw) : undefined;

  try {
    const data = stripImageMetadata(new Uint8Array(await image.arrayBuffer()));
    const result = await verifyTreePhoto(
      {
        image: { data },
        ...(playerPosition ? { playerPosition } : {}),
        ...(expected ? { expected } : {}),
        ...(capturedAt && !Number.isNaN(capturedAt.getTime()) ? { capturedAt } : {}),
      },
      getVerifier(),
    );
    return Response.json({ expected: expected ? { treeId: expected.externalId ?? null, genus: expected.genus } : null, result });
  } catch (cause) {
    console.error('[api/verify] verification failed', cause);
    return error(500, 'verification_failed', 'Verification failed unexpectedly.');
  }
}
