import type { NextConfig } from 'next';

const nextConfig: NextConfig = {
  // Workspace packages ship TypeScript sources (`exports` points at `src/index.ts`), so Next compiles them.
  // Their relative imports end in `.ts`; tsconfig.json sets `allowImportingTsExtensions` (fine with `noEmit`)
  // and Turbopack resolves them as is, including `new URL('../data/trees.json', import.meta.url)`.
  transpilePackages: ['@openquest/tree-search'],
};

export default nextConfig;
