import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { TypeSafeClient } from "@typesafe-ai/sdk";
import { execute, interpret, type JevLike, type TreeData } from "./search.ts";

export * from "./search.ts";
export { GENUS_NAMES, germanName, englishName } from "./genera.ts";

/** Absolute path of the bundled, prebuilt tree data (rebuild with `pnpm --filter @openquest/tree-search build-data`). */
export const TREE_DATA_PATH = fileURLToPath(new URL("../data/trees.json", import.meta.url));

export function loadTreeData(path: string = TREE_DATA_PATH): TreeData {
  return JSON.parse(readFileSync(path, "utf8")) as TreeData;
}

/** Jev through OpenRouter (same key as the vision models). Returns `null` without key: search then uses rules. */
export function createJev(apiKey = process.env.OPENROUTER_API_KEY, model = process.env.JEV_MODEL || "typesafe/jev-1.13"): { jev: JevLike | null; model: string } {
  if (!apiKey) return { jev: null, model };
  const client = new TypeSafeClient({ apiKey, baseURL: "https://openrouter.ai/api", defaultModel: model, timeout: 10_000, retry: { maxRetries: 1 }, logLevel: "off" });
  return { jev: client as unknown as JevLike, model };
}

/** One call for API routes: interpret with Jev (or rules) and execute. */
export async function searchTrees(query: string, data: TreeData, jev: { jev: JevLike | null; model: string }) {
  const { interpretation, latencyMs, costUsd } = await interpret(query, data, jev.jev, jev.model);
  return { ...execute(query, data, interpretation), jevLatencyMs: latencyMs, costUsd };
}
