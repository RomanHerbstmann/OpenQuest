import type { NextConfig } from 'next';

/** Base URL of the OpenQuest .NET API, server side only. The browser always talks to `/backend/...` on this origin. */
const apiUrl = (process.env.OPENQUEST_API_URL || 'https://openquest-api.onrender.com').replace(/\/+$/, '');

const nextConfig: NextConfig = {
  // Workspace packages ship TypeScript sources (`exports` points at `src/index.ts`), so Next compiles them.
  // Their relative imports end in `.ts`; tsconfig.json sets `allowImportingTsExtensions` (fine with `noEmit`)
  // and Turbopack resolves them as is, including `new URL('../data/trees.json', import.meta.url)`.
  transpilePackages: ['@openquest/tree-search', '@openquest/tree-verification', '@openquest/adapter-de-muenster'],
  // The free Render instance sleeps and needs up to a minute for the first request; the default proxy timeout is 30 s.
  experimental: { proxyTimeout: 90_000 },
  // Same origin proxy to the API: no CORS setup needed for local or preview hosts of the player app.
  // Rewrites are resolved at build time, so set OPENQUEST_API_URL before `next build`.
  async rewrites() {
    return [{ source: '/backend/:path*', destination: `${apiUrl}/:path*` }];
  },
};

export default nextConfig;
