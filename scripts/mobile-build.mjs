// Static export for the phone app (Capacitor): `next build` with MOBILE_BUILD=1 into out/, then `cap sync`.
// A static export cannot contain server routes, so src/app/api is moved out of the way while it runs and put back
// afterwards (also when the build fails). The app then calls /api/verify and /api/tree-search on NEXT_PUBLIC_API_URL and the .NET API on NEXT_PUBLIC_BACKEND_URL.
import { spawnSync } from 'node:child_process';
import { existsSync, renameSync } from 'node:fs';

const apiDir = 'src/app/api';
const parked = '.mobile-build-api';
// The static export has no /backend proxy (next.config.ts rewrites), so the app calls the .NET API directly.
const env = { ...process.env, MOBILE_BUILD: '1', NEXT_PUBLIC_BACKEND_URL: process.env.NEXT_PUBLIC_BACKEND_URL || process.env.OPENQUEST_API_URL || 'https://openquest-api.onrender.com' };
const run = (command, args) => spawnSync(command, args, { stdio: 'inherit', env }).status ?? 1;

if (existsSync(parked)) {
  console.error(`${parked} exists: an earlier mobile build was interrupted. Move it back to ${apiDir} first.`);
  process.exit(1);
}

let status;
if (existsSync(apiDir)) renameSync(apiDir, parked);
try {
  status = run('pnpm', ['exec', 'next', 'build']);
} finally {
  if (existsSync(parked)) renameSync(parked, apiDir);
}
if (status === 0 && !process.argv.includes('--no-sync')) status = run('pnpm', ['exec', 'cap', 'sync']);
process.exit(status);
