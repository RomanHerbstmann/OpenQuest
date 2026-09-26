/**
 * OpenQuest tree dashboard: static UI + tree data + Jev powered search.
 *
 *   pnpm --filter @openquest/dashboard start      -> http://localhost:8787
 *
 * The OpenRouter key stays on the server; the browser only talks to /api/*.
 */
import { createReadStream, existsSync } from "node:fs";
import { createServer, type IncomingMessage, type ServerResponse } from "node:http";
import { createRequire } from "node:module";
import { dirname, extname, join, normalize } from "node:path";
import { fileURLToPath } from "node:url";
import { gzipSync } from "node:zlib";
import { createJev, loadTreeData, searchTrees, TREE_DATA_PATH, type SearchResult } from "@openquest/tree-search";

const here = dirname(fileURLToPath(import.meta.url));
const root = join(here, "..");
const publicDir = join(root, "public");
const port = Number(process.env.PORT ?? 8787);
const dataFile = process.env.TREE_DATA_PATH || TREE_DATA_PATH;

if (!existsSync(dataFile)) {
  console.error(`${dataFile} missing: run \`pnpm --filter @openquest/tree-search build-data\` first`);
  process.exit(1);
}
const data = loadTreeData(dataFile);
const dataGz = gzipSync(JSON.stringify(data));

const jev = createJev();
if (!jev.jev) console.warn("OPENROUTER_API_KEY not set: search falls back to rules");

const require = createRequire(import.meta.url);
const maplibreDir = dirname(require.resolve("maplibre-gl/package.json"));
const VENDOR: Record<string, string> = {
  "/vendor/maplibre-gl.js": join(maplibreDir, "dist", "maplibre-gl.js"),
  "/vendor/maplibre-gl.css": join(maplibreDir, "dist", "maplibre-gl.css"),
};
const TYPES: Record<string, string> = { ".html": "text/html; charset=utf-8", ".js": "text/javascript; charset=utf-8", ".css": "text/css; charset=utf-8", ".svg": "image/svg+xml" };

/** Same query, same answer: cache interpretations to keep the UI snappy and cost at zero for repeats. */
const cache = new Map<string, SearchResult>();

async function readBody(req: IncomingMessage, limit = 4096): Promise<string> {
  let body = "";
  for await (const chunk of req) {
    body += chunk;
    if (body.length > limit) throw new Error("body too large");
  }
  return body;
}

function json(res: ServerResponse, status: number, body: unknown, gzip = false) {
  const payload = Buffer.from(JSON.stringify(body));
  res.writeHead(status, { "Content-Type": "application/json; charset=utf-8", ...(gzip ? { "Content-Encoding": "gzip" } : {}) });
  res.end(gzip ? gzipSync(payload) : payload);
}

const server = createServer(async (req, res) => {
  const url = new URL(req.url ?? "/", "http://localhost");
  try {
    if (url.pathname === "/api/trees") {
      res.writeHead(200, { "Content-Type": "application/json", "Content-Encoding": "gzip", "Cache-Control": "public, max-age=3600" });
      return res.end(dataGz);
    }
    if (url.pathname === "/api/search" && req.method === "POST") {
      const { q } = JSON.parse(await readBody(req)) as { q?: string };
      const query = (q ?? "").trim().slice(0, 200);
      if (!query) return json(res, 400, { error: "empty query" });
      const key = query.toLowerCase();
      let result = cache.get(key);
      if (!result) {
        result = await searchTrees(query, data, jev);
        cache.set(key, result);
        if (cache.size > 500) cache.delete(cache.keys().next().value!);
        console.log(`search "${query}" -> ${result.total} (${result.interpretation.source}, ${result.jevLatencyMs ?? 0} ms)`);
      }
      return json(res, 200, result, /gzip/.test(String(req.headers["accept-encoding"])));
    }
    const file = VENDOR[url.pathname] ?? join(publicDir, normalize(url.pathname === "/" ? "/index.html" : url.pathname).replace(/^(\.\.[/\\])+/, ""));
    if (!file.startsWith(publicDir) && !Object.values(VENDOR).includes(file)) return json(res, 403, { error: "forbidden" });
    if (!existsSync(file)) return json(res, 404, { error: "not found" });
    res.writeHead(200, { "Content-Type": TYPES[extname(file)] ?? "application/octet-stream" });
    createReadStream(file).pipe(res);
  } catch (err) {
    json(res, 500, { error: err instanceof Error ? err.message : String(err) });
  }
});

server.listen(port, () => console.log(`OpenQuest dashboard on http://localhost:${port} (${data.trees.id.length} trees)`));
