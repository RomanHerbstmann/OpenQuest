# @openquest/tree-search

Natural language search over all Münster trees, interpreted by Jev (TypeSafe AI via OpenRouter), plus the prebuilt tree data (`data/trees.json`: position, genus, street, district, quarter, nDOM50 height).

```ts
import { createJev, loadTreeData, searchTrees } from "@openquest/tree-search";

const data = loadTreeData();            // bundled data/trees.json
const jev = createJev();                // OPENROUTER_API_KEY from env, null => rule based fallback
const result = await searchTrees("die größten Birken in Hiltrup", data, jev);
result.summary.de;                      // "116 Bäume · Birke (Betula) · Hiltrup · höchste zuerst"
result.items;                           // ranked top N with genus, height, street, lat/lon
result.matches;                         // indices of all matching trees
```

Server side only (API key). Rebuild the data with `pnpm --filter @openquest/tree-search build-data` (~20 min first run, resumable cache in `data/cache/`). Details: [apps/dashboard/README.md](../../apps/dashboard/README.md).
