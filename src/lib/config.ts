/**
 * Base URL of the OpenQuest API. In a browser served next to the API this can stay empty (same origin); the phone app has
 * no origin of its own and needs the full address, e.g. https://api.openquest.fun. Set NEXT_PUBLIC_API_URL when building.
 */
export const API_URL = (process.env.NEXT_PUBLIC_API_URL ?? '').replace(/\/+$/, '');

export const apiUrl = (path: string) => `${API_URL}${path.startsWith('/') ? path : `/${path}`}`;
