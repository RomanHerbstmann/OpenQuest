// Access to the OpenQuest API (same origin). The token lives in sessionStorage: it is gone when the tab is closed.

const TOKEN_KEY = 'openquest.panel.token';

export const session = { token: sessionStorage.getItem(TOKEN_KEY), me: null };

export class ApiError extends Error {
  constructor(status, body) {
    const validation = body?.errors ? Object.values(body.errors).flat().join(' ') : null;
    super(body?.message ?? validation ?? body?.title ?? body?.error ?? `HTTP ${status}`);
    this.status = status;
    this.body = body;
    this.code = body?.error;
    this.details = body?.details;
  }
}

async function request(method, path, body) {
  const headers = {};
  if (session.token) headers.Authorization = `Bearer ${session.token}`;
  if (body !== undefined) headers['Content-Type'] = 'application/json';
  const response = await fetch(path, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) });
  if (response.status === 401 && session.token) window.dispatchEvent(new Event('session-expired'));
  if (response.status === 204) return null;
  const text = await response.text();
  let data = null;
  try { data = text ? JSON.parse(text) : null; } catch { data = { message: text }; }
  if (!response.ok) throw new ApiError(response.status, data);
  return data;
}

export const get = (path) => request('GET', path);
export const post = (path, body) => request('POST', path, body ?? {});
export const put = (path, body) => request('PUT', path, body ?? {});
export const del = (path) => request('DELETE', path);

export async function login(username, password) {
  const auth = await request('POST', '/auth/login', { username, password });
  session.token = auth.token;
  sessionStorage.setItem(TOKEN_KEY, auth.token);
  session.me = await get('/me');
  return session.me;
}

export async function restore() {
  if (!session.token) return null;
  try { session.me = await get('/me'); } catch { logout(); }
  return session.me;
}

export function logout() {
  session.token = null;
  session.me = null;
  sessionStorage.removeItem(TOKEN_KEY);
}

/** Fetches a protected image and returns an object URL. */
export async function imageUrl(path) {
  const response = await fetch(path, { headers: { Authorization: `Bearer ${session.token}` } });
  if (!response.ok) throw new ApiError(response.status, null);
  return URL.createObjectURL(await response.blob());
}
