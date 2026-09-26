import type { NextConfig } from 'next';

/** Base URL of the OpenQuest .NET API, server side only. The browser always talks to `/backend/...` on this origin. */
const apiUrl = (process.env.OPENQUEST_API_URL || 'https://openquest-api.onrender.com').replace(/\/+$/, '');

// Two ways to build the frontend:
//   pnpm build          the web app (server, as before)
//   pnpm mobile:build   a static export into out/ (MOBILE_BUILD=1), which the phone app (Capacitor) packs into the APK/IPA.
//                       The export has no server: scripts/mobile-build.mjs sets src/app/api aside while it runs, and the app
//                       calls /api/verify and /api/tree-search on the deployed site (NEXT_PUBLIC_API_URL) instead.
const mobile = process.env.MOBILE_BUILD === '1';

const nextConfig: NextConfig = {
  // Workspace packages ship TypeScript sources (`exports` points at `src/index.ts`), so Next compiles them.
  // Their relative imports end in `.ts`; tsconfig.json sets `allowImportingTsExtensions` (fine with `noEmit`)
  // and Turbopack resolves them as is, including `new URL('../data/trees.json', import.meta.url)`.
  transpilePackages: ['@openquest/tree-search', '@openquest/tree-verification', '@openquest/adapter-de-muenster'],
  ...(mobile ? { output: 'export', trailingSlash: true } : {}),
  // The image optimizer needs a server; the static export has none.
  images: { unoptimized: mobile },
  // The free Render instance sleeps and needs up to a minute for the first request; the default proxy timeout is 30 s.
  experimental: { proxyTimeout: 90_000 },
  // Same origin proxy to the API: no CORS setup needed for local or preview hosts of the player app.
  // Rewrites are resolved at build time, so set OPENQUEST_API_URL before `next build`.
  // The static export has no server to proxy; the phone app calls the API directly (src/lib/api.ts, NEXT_PUBLIC_OPENQUEST_API_URL).
  async rewrites() {
    if (mobile) return [];
    return [{ source: '/backend/:path*', destination: `${apiUrl}/:path*` }];
  },
  // The phone app (origin https://localhost or capacitor://localhost) calls the two /api routes on the deployed site.
  // No cookies are involved, so any origin may read the answer; the routes rate-limit themselves.
  async headers() {
    if (mobile) return [];
    return [{
      source: '/api/:path*',
      headers: [
        { key: 'Access-Control-Allow-Origin', value: '*' },
        { key: 'Access-Control-Allow-Methods', value: 'POST, OPTIONS' },
        { key: 'Access-Control-Allow-Headers', value: 'Content-Type' },
        { key: 'Access-Control-Expose-Headers', value: 'Retry-After' },
      ],
    }];
  },
};

export default nextConfig;
