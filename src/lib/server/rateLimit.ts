// Fixed window rate limit per client key, kept in process memory.
// Good enough for a single Next.js instance; a shared store is needed once the app scales out.

type Bucket = { windowStart: number; count: number };

export function createRateLimiter({ limit, windowMs }: { limit: number; windowMs: number }) {
  const buckets = new Map<string, Bucket>();

  return function take(key: string, now = Date.now()): { ok: true } | { ok: false; retryAfterS: number } {
    // Drop stale entries now and then so the map does not grow without bound.
    if (buckets.size > 5_000) {
      for (const [k, b] of buckets) if (now - b.windowStart >= windowMs) buckets.delete(k);
    }
    const bucket = buckets.get(key);
    if (!bucket || now - bucket.windowStart >= windowMs) {
      buckets.set(key, { windowStart: now, count: 1 });
      return { ok: true };
    }
    if (bucket.count >= limit) {
      return { ok: false, retryAfterS: Math.max(1, Math.ceil((bucket.windowStart + windowMs - now) / 1000)) };
    }
    bucket.count += 1;
    return { ok: true };
  };
}

export function clientKey(headers: Headers): string {
  const forwarded = headers.get('x-forwarded-for')?.split(',')[0]?.trim();
  return forwarded || headers.get('x-real-ip') || 'unknown';
}
