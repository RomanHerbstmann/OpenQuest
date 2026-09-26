import { liveText } from '@/i18n/liveGame';

/**
 * Typed client for the OpenQuest API (apps/api). The browser always calls `/backend/...` on its own origin;
 * next.config.ts rewrites that to OPENQUEST_API_URL, so there is no CORS setup for local or preview hosts.
 */
export const API_BASE = '/backend';

const t = liveText.de;
const SESSION_KEY = 'openquest-session-v1';
export const SESSION_EVENT = 'openquest-session';
/** After this long without an answer the UI shows "server is starting" (Render free tier cold start). */
const SLOW_AFTER_MS = 4_000;
/** The cold start takes up to a minute, so give it a little more before giving up. */
const TIMEOUT_MS = 75_000;

// --- Contract (apps/api/OpenQuest.Api/Contracts/Dtos.cs; JSON camelCase, enums snake_case) ---

export type Role = 'player' | 'moderator' | 'admin';
export type TaskType = 'photo' | 'verify_attribute' | 'measure' | 'condition_report';
export type ClaimStatus = 'active' | 'submitted' | 'expired' | 'cancelled';
export type SubmissionStatus = 'pending' | 'approved' | 'rejected';

export type Session = { token: string; expiresAt: string; username: string; role: Role };
export type AuthResponse = Session;
export type RegisterResponse = Session & { recoveryCodes: string[] };

export type TreeAttributes = {
  genus?: string | null;
  species?: string | null;
  genus_raw?: string | null;
  street_key?: string | null;
  quality_flags?: string[] | null;
};

export type AssetDto = { id: string; assetType: string; externalId: string; lat: number; lon: number; attributes: TreeAttributes | null };

export type QuestDto = {
  id: string;
  title: string;
  description: string | null;
  taskType: TaskType;
  taskConfig: { attribute?: string; unit?: string } | null;
  rewardPoints: number;
  freeSlots: number;
  geofenceRadiusM: number;
  endsAt: string | null;
  distanceMeters: number | null;
  asset: AssetDto;
};

export type ClaimDto = { id: string; questId: string; status: ClaimStatus; claimedAt: string; expiresAt: string };
export type SubmissionSummaryDto = { id: string; status: SubmissionStatus; rejectionReason: string | null; submittedAt: string };
export type MyClaimDto = { id: string; status: ClaimStatus; claimedAt: string; expiresAt: string; quest: QuestDto; submission: SubmissionSummaryDto | null };
export type SubmitResult = { submissionId: string; status: SubmissionStatus; distanceMeters: number; mediaId: string | null };
export type LevelDto = { level: number; current: number; required: number; percent: number; isMaxLevel: boolean };
/** `GET /me`. `totalPoints` and `level` come with the gamification release (#15); older API versions omit them. */
export type MeDto = { id: string; username: string; role: Role; totalPoints?: number; level?: LevelDto };

/** `task_type.result_schema` of the quest types the app can complete. */
export type PhotoPayload = { note?: string };
export type VerifyAttributePayload = { value: string; confirmed?: boolean; note?: string };

// --- Errors ---

export class ApiError extends Error {
  constructor(
    /** HTTP status, 0 for network errors and timeouts. */
    readonly status: number,
    /** API error code (`no_free_slots`, `outside_geofence`, ...) or a client code (`timeout`, `offline`). */
    readonly code: string,
    message: string,
    readonly details?: unknown,
  ) {
    super(message);
    this.name = 'ApiError';
  }
}

type ErrorBody = { error?: string; message?: string; details?: unknown; errors?: Record<string, string[]> } | null;

/** Player facing German text for an API error response. */
export function errorText(status: number, body: ErrorBody): { code: string; message: string } {
  const code = body?.error ?? (status === 401 ? 'unauthorized' : status === 403 ? 'forbidden' : status === 429 ? 'rate_limited' : body?.errors ? 'validation_failed' : 'http_' + status);
  if (code === 'outside_geofence') {
    const details = body?.details as { distanceMeters?: number; allowedMeters?: number } | undefined;
    if (typeof details?.distanceMeters === 'number' && typeof details.allowedMeters === 'number') {
      return { code, message: t.outsideGeofence(details.distanceMeters, details.allowedMeters) };
    }
  }
  if (code === 'validation_failed') {
    const fields = Object.keys((body?.details ?? body?.errors ?? {}) as object);
    if (fields.includes('username')) return { code, message: `${t.errors.validation_failed} ${t.auth.username}: ${t.auth.usernameHint}.` };
    if (fields.includes('password')) return { code, message: `${t.errors.validation_failed} ${t.auth.password}: ${t.auth.passwordHint}.` };
  }
  return { code, message: t.errors[code] ?? (status >= 500 ? t.server.unexpected : t.errors.fallback) };
}

// --- Session (localStorage, may throw in private mode or when site data is blocked) ---

export function loadSession(): Session | null {
  try {
    const raw = window.localStorage.getItem(SESSION_KEY);
    if (!raw) return null;
    const value = JSON.parse(raw) as Partial<Session>;
    if (typeof value.token !== 'string' || typeof value.username !== 'string' || typeof value.expiresAt !== 'string') return null;
    if (Date.parse(value.expiresAt) <= Date.now()) { clearSession(); return null; }
    return { token: value.token, expiresAt: value.expiresAt, username: value.username, role: (value.role ?? 'player') as Role };
  } catch {
    return null;
  }
}

export function saveSession(session: Session) {
  const { token, expiresAt, username, role } = session; // never persist recovery codes
  try { window.localStorage.setItem(SESSION_KEY, JSON.stringify({ token, expiresAt, username, role })); } catch { /* session then lasts for this page only */ }
  window.dispatchEvent(new Event(SESSION_EVENT));
}

export function clearSession() {
  try { window.localStorage.removeItem(SESSION_KEY); } catch { /* nothing stored */ }
  window.dispatchEvent(new Event(SESSION_EVENT));
}

// --- "Server is starting" signal for the cold start ---

type SlowListener = (slow: boolean) => void;
const slowListeners = new Set<SlowListener>();
let slowRequests = 0;

/** Called with `true` while at least one request takes longer than a few seconds. */
export function onServerSlow(listener: SlowListener) {
  slowListeners.add(listener);
  listener(slowRequests > 0);
  return () => { slowListeners.delete(listener); };
}

function setSlow(delta: 1 | -1) {
  const before = slowRequests > 0;
  slowRequests = Math.max(0, slowRequests + delta);
  if (before !== slowRequests > 0) slowListeners.forEach((listener) => listener(slowRequests > 0));
}

// --- Requests ---

type RequestOptions = { method?: string; body?: BodyInit; json?: unknown; auth?: boolean; signal?: AbortSignal };

async function request<T>(path: string, { method = 'GET', body, json, auth = true, signal }: RequestOptions = {}): Promise<T> {
  const headers = new Headers({ Accept: 'application/json' });
  if (json !== undefined) headers.set('Content-Type', 'application/json');
  if (auth) {
    const session = loadSession();
    if (!session) throw new ApiError(401, 'unauthorized', t.errors.unauthorized);
    headers.set('Authorization', `Bearer ${session.token}`);
  }

  const controller = new AbortController();
  let timedOut = false;
  let slow = false;
  const onAbort = () => controller.abort();
  signal?.addEventListener('abort', onAbort, { once: true });
  const slowTimer = window.setTimeout(() => { slow = true; setSlow(1); }, SLOW_AFTER_MS);
  const timeout = window.setTimeout(() => { timedOut = true; controller.abort(); }, TIMEOUT_MS);

  let response: Response;
  try {
    response = await fetch(`${API_BASE}${path}`, { method, headers, body: json !== undefined ? JSON.stringify(json) : body, signal: controller.signal });
  } catch (cause) {
    if (signal?.aborted && !timedOut) throw cause; // caller cancelled, not an error to show
    throw timedOut ? new ApiError(0, 'timeout', t.server.timeout) : new ApiError(0, 'offline', t.server.offline);
  } finally {
    window.clearTimeout(slowTimer);
    window.clearTimeout(timeout);
    signal?.removeEventListener('abort', onAbort);
    if (slow) setSlow(-1);
  }

  if (response.status === 204) return undefined as T;
  const text = await response.text();
  let data: unknown = null;
  try { data = text ? JSON.parse(text) : null; } catch { data = null; }

  if (!response.ok) {
    // The proxy answers 502/504 while the sleeping instance boots.
    if (response.status === 502 || response.status === 503 || response.status === 504) throw new ApiError(response.status, 'waking', t.server.timeout);
    if (response.status === 401 && auth) { clearSession(); throw new ApiError(401, 'session_expired', t.server.sessionExpired); }
    const { code, message } = errorText(response.status, data as ErrorBody);
    throw new ApiError(response.status, code, message, (data as ErrorBody)?.details);
  }
  return data as T;
}

// --- Endpoints ---

export const api = {
  register: (username: string, password: string) =>
    request<RegisterResponse>('/auth/register', { method: 'POST', json: { username, password }, auth: false }),
  login: (username: string, password: string) =>
    request<AuthResponse>('/auth/login', { method: 'POST', json: { username, password }, auth: false }),

  /** Open quests near a position, nearest first (max. 200). Full quests and quests the player holds are hidden. */
  nearbyQuests: (lat: number, lon: number, radius: number, taskType?: TaskType, signal?: AbortSignal) => {
    const query = new URLSearchParams({ lat: lat.toFixed(6), lon: lon.toFixed(6), radius: String(Math.round(radius)) });
    if (taskType) query.set('taskType', taskType);
    return request<QuestDto[]>(`/quests/nearby?${query}`, { signal });
  },

  claimQuest: (questId: string) => request<ClaimDto>(`/quests/${encodeURIComponent(questId)}/claim`, { method: 'POST' }),
  cancelClaim: (claimId: string) => request<void>(`/claims/${encodeURIComponent(claimId)}/cancel`, { method: 'POST' }),

  /** multipart/form-data: `lat`, `lon` (reported position), `payload` (JSON text per result_schema), `photo` (image). */
  submitClaim: (claimId: string, input: { lat: number; lon: number; payload: PhotoPayload | VerifyAttributePayload; photo?: Blob }) => {
    const form = new FormData();
    form.set('lat', String(input.lat));
    form.set('lon', String(input.lon));
    form.set('payload', JSON.stringify(input.payload));
    if (input.photo) form.set('photo', input.photo, 'quest.jpg');
    return request<SubmitResult>(`/claims/${encodeURIComponent(claimId)}/submit`, { method: 'POST', body: form });
  },

  myClaims: () => request<MyClaimDto[]>('/me/claims'),
  me: () => request<MeDto>('/me'),
};
