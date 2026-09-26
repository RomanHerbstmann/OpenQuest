import { createJev, loadTreeData, searchTrees, type TreeData } from '@openquest/tree-search';
import type { TreeSearchError, TreeSearchResponse } from '@/types/treeSearch';

// The search reads the tree data from disk and calls OpenRouter; both need Node, not the edge runtime.
export const runtime = 'nodejs';
export const dynamic = 'force-dynamic';

const MAX_QUERY_LENGTH = 200;
const MAX_POSITIONS = 5000;
const MAX_CACHE_ENTRIES = 500;

let data: TreeData | null = null;
let jev: ReturnType<typeof createJev> | null = null;
/** Same query, same answer: keeps repeats fast and free. */
const cache = new Map<string, TreeSearchResponse>();

function getData(): TreeData {
  // TREE_DATA_PATH overrides the bundled file, as in apps/dashboard.
  data ??= loadTreeData(process.env.TREE_DATA_PATH || undefined);
  return data;
}

function getJev() {
  // Reads OPENROUTER_API_KEY and JEV_MODEL. Without a key the search falls back to rules.
  jev ??= createJev();
  return jev;
}

/** Five decimals are about one meter, plenty for map dots and a third smaller on the wire. */
const round = (value: number) => Math.round(value * 1e5) / 1e5;

function toResponse(result: Awaited<ReturnType<typeof searchTrees>>, trees: TreeData['trees']): TreeSearchResponse {
  const { matches, ...rest } = result;
  const step = Math.max(1, matches.length / MAX_POSITIONS);
  const positions: Array<[number, number]> = [];
  for (let k = 0; k < matches.length && positions.length < MAX_POSITIONS; k += step) {
    const i = matches[Math.floor(k)]!;
    positions.push([round(trees.lat[i]!), round(trees.lon[i]!)]);
  }
  return { ...rest, positions, positionsSampled: positions.length < matches.length };
}

const error = (status: number, message: string) => Response.json({ error: message } satisfies TreeSearchError, { status });

export async function POST(request: Request) {
  let q: unknown;
  try {
    ({ q } = (await request.json()) as { q?: unknown });
  } catch {
    return error(400, 'invalid JSON body');
  }
  if (typeof q !== 'string') return error(400, 'missing "q"');
  const query = q.trim().slice(0, MAX_QUERY_LENGTH);
  if (!query) return error(400, 'empty query');

  const key = query.toLowerCase();
  const cached = cache.get(key);
  if (cached) return Response.json(cached);

  try {
    const trees = getData();
    const result = toResponse(await searchTrees(query, trees, getJev()), trees.trees);
    cache.set(key, result);
    if (cache.size > MAX_CACHE_ENTRIES) cache.delete(cache.keys().next().value!);
    return Response.json(result);
  } catch (err) {
    console.error('tree search failed', err);
    return error(500, 'search failed');
  }
}
