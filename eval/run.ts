/**
 * Runs the verification pipeline over the curated samples and compares configurations.
 * Real Münster trees are used as quest targets; every genus photo is also tested against
 * a target of a *different* genus to measure "wrong tree" detection.
 *
 *   pnpm eval                 all configurations
 *   pnpm eval --only full     one configuration
 */
import { createHash } from "node:crypto";
import { existsSync } from "node:fs";
import { mkdir, readFile, writeFile } from "node:fs/promises";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { createMuensterTreeProvider, MUENSTER_TREES_WFS, normalizeBaumgruppe } from "@openquest/adapter-de-muenster";
import {
  createOpenRouterClient,
  createOsmTreeProvider,
  resolveConfig,
  verifyTreePhoto,
  type ModelCall,
  type OpenRouterClient,
  type TreeVerificationResult,
  type VerificationConfig,
} from "@openquest/tree-verification";

const here = dirname(fileURLToPath(import.meta.url));
const outDir = join(here, "out");

interface Sample {
  file: string;
  title: string;
  tree: boolean;
  genus: string | null;
  expect: "approve" | "review" | "reject" | "not_approve";
  group: string;
}

interface Case {
  id: string;
  sample: Sample;
  kind: "match" | "mismatch";
  expected: { position: { lat: number; lon: number }; genus: string | null };
  player: { lat: number; lon: number; accuracyM: number };
}

const base = resolveConfig();
const CONFIGS: Record<string, VerificationConfig> = {
  single: { ...base, visionModels: [base.visionModels[0]!], escalationModel: null, jevModel: null },
  ensemble: { ...base, jevModel: null },
  full: base,
};

/** Deterministic PRNG so the same samples get the same Münster targets on every run. */
function rng(seed: string) {
  let h = createHash("sha256").update(seed).digest().readUInt32LE(0);
  return () => ((h = (h * 1664525 + 1013904223) >>> 0) / 2 ** 32);
}

async function loadMuensterTrees() {
  const cache = join(outDir, "muenster-trees.geojson");
  if (!existsSync(cache)) {
    const url = `${MUENSTER_TREES_WFS}?SERVICE=WFS&VERSION=1.1.0&REQUEST=GetFeature&TYPENAME=Baeume&OUTPUTFORMAT=geojson`;
    const res = await fetch(url);
    await writeFile(cache, await res.text());
  }
  const gj = JSON.parse(await readFile(cache, "utf8")) as { features: { geometry: { coordinates: [number, number] }; properties: { baumgruppe?: string } }[] };
  return gj.features.map((f) => ({ position: { lon: f.geometry.coordinates[0], lat: f.geometry.coordinates[1] }, genus: normalizeBaumgruppe(f.properties.baumgruppe).genus }));
}

/** Player stands 3 to 10 m from the tree, with realistic phone GPS accuracy. */
function nearby(pos: { lat: number; lon: number }, rand: () => number) {
  const d = 3 + rand() * 7;
  const a = rand() * 2 * Math.PI;
  return { lat: pos.lat + (d * Math.cos(a)) / 111_195, lon: pos.lon + (d * Math.sin(a)) / (111_195 * Math.cos((pos.lat * Math.PI) / 180)), accuracyM: 5 + Math.round(rand() * 10) };
}

function buildCases(samples: Sample[], trees: Awaited<ReturnType<typeof loadMuensterTrees>>): Case[] {
  const cases: Case[] = [];
  const withGenus = trees.filter((t) => t.genus);
  for (const s of samples) {
    const rand = rng(s.file);
    const pick = (pred: (g: string | null) => boolean) => {
      const pool = withGenus.filter((t) => pred(t.genus));
      return pool[Math.floor(rand() * pool.length)]!;
    };
    const target = s.genus ? pick((g) => g === s.genus) : pick(() => true);
    cases.push({ id: `${s.file}#match`, sample: s, kind: "match", expected: s.genus ? target : { ...target, genus: s.tree ? null : target.genus }, player: nearby(target.position, rand) });
    if (s.genus) {
      const other = pick((g) => g !== s.genus && ["Tilia", "Quercus", "Acer", "Platanus", "Betula", "Carpinus"].includes(g!));
      cases.push({ id: `${s.file}#mismatch`, sample: s, kind: "mismatch", expected: other, player: nearby(other.position, rand) });
    }
  }
  return cases;
}

/**
 * Vision answers only depend on (model, image): cache them in memory and on disk so configurations
 * share calls and rule changes can be re-evaluated without paying for the models again.
 * Delete eval/out/vision-cache.json to force fresh model calls.
 */
async function cachingClient(inner: OpenRouterClient) {
  const file = join(outDir, "vision-cache.json");
  const disk: Record<string, Awaited<ReturnType<OpenRouterClient["chat"]>>> = existsSync(file) ? JSON.parse(await readFile(file, "utf8")) : {};
  const pending = new Map<string, ReturnType<OpenRouterClient["chat"]>>();
  const client: OpenRouterClient = {
    chat(req, signal) {
      const key = createHash("sha1").update(JSON.stringify(req)).digest("hex");
      if (disk[key]) return Promise.resolve(disk[key]);
      if (!pending.has(key)) {
        const p = inner.chat(req, signal).then((r) => ((disk[key] = r), r));
        p.catch(() => pending.delete(key));
        pending.set(key, p);
      }
      return pending.get(key)!;
    },
  };
  return { client, save: () => writeFile(file, JSON.stringify(disk)) };
}

async function pool<T, R>(items: T[], limit: number, fn: (item: T) => Promise<R>): Promise<R[]> {
  const out: R[] = new Array(items.length);
  let next = 0;
  await Promise.all(
    Array.from({ length: limit }, async () => {
      while (next < items.length) {
        const i = next++;
        out[i] = await fn(items[i]!);
      }
    }),
  );
  return out;
}

interface Row {
  case: Case;
  result: TreeVerificationResult;
}

function metrics(rows: Row[]) {
  const pct = (n: number, d: number) => (d ? `${((100 * n) / d).toFixed(0)}% (${n}/${d})` : "n/a");
  const match = rows.filter((r) => r.case.kind === "match");
  const pos = match.filter((r) => r.case.sample.tree);
  const neg = match.filter((r) => !r.case.sample.tree);
  const mismatch = rows.filter((r) => r.case.kind === "mismatch");
  const genusRows = pos.filter((r) => r.case.sample.genus);
  const top1 = genusRows.filter((r) => r.result.genus.value === r.case.sample.genus).length;
  const top3 = genusRows.filter((r) => r.result.genus.alternatives.slice(0, 3).some((g) => g.genus === r.case.sample.genus)).length;
  const avg = (xs: number[]) => xs.reduce((s, x) => s + x, 0) / (xs.length || 1);
  return {
    "tree detection accuracy": pct(match.filter((r) => r.result.treePresent.value === r.case.sample.tree).length, match.length),
    "trees auto-approved": pct(pos.filter((r) => r.result.verdict === "approve").length, pos.length),
    "trees rejected (bad)": pct(pos.filter((r) => r.result.verdict === "reject").length, pos.length),
    "non-trees approved (bad)": pct(neg.filter((r) => r.result.verdict === "approve").length, neg.length),
    "wrong genus target not approved": pct(mismatch.filter((r) => r.result.verdict !== "approve").length, mismatch.length),
    "genus top-1": pct(top1, genusRows.length),
    "genus top-3": pct(top3, genusRows.length),
    "avg cost per photo (USD)": avg(match.map((r) => r.result.costUsd)).toFixed(5),
    "avg latency (s)": (avg(rows.map((r) => r.result.latencyMs)) / 1000).toFixed(1),
  };
}

async function main() {
  await mkdir(outDir, { recursive: true });
  const only = process.argv.includes("--only") ? process.argv[process.argv.indexOf("--only") + 1] : undefined;
  const samples = JSON.parse(await readFile(join(here, "samples.json"), "utf8")) as Sample[];
  const cases = buildCases(samples, await loadMuensterTrees());
  const cache = await cachingClient(createOpenRouterClient({ apiKey: base.openRouterApiKey, appUrl: base.appUrl, appName: base.appName }));
  const openRouter = cache.client;
  const providers = [createMuensterTreeProvider(), createOsmTreeProvider()];
  const images = new Map(await Promise.all(samples.map(async (s) => [s.file, await readFile(join(here, s.file))] as const)));

  const report: Record<string, { metrics: ReturnType<typeof metrics>; rows: { id: string; verdict: string; treeP: number; genus: string | null; reasons: string[]; costUsd: number; models: ModelCall[]; jev?: unknown; visionGenus: unknown }[] }> = {};
  for (const [name, config] of Object.entries(CONFIGS)) {
    if (only && name !== only) continue;
    console.log(`\n== ${name}: vision=${config.visionModels.join("+")} escalation=${config.escalationModel ?? "-"} jev=${config.jevModel ?? "-"}`);
    const rows = await pool(cases, 4, async (c): Promise<Row> => {
      const result = await verifyTreePhoto(
        { image: { data: images.get(c.sample.file)! }, expected: c.expected, playerPosition: c.player },
        { config, openRouter, providers },
      );
      const flag = (c.kind === "match" ? (c.sample.tree ? result.verdict === "reject" : result.verdict === "approve") : result.verdict === "approve") ? "  <-- WRONG" : "";
      console.log(`${c.id.padEnd(34)} ${result.verdict.padEnd(7)} p=${result.treePresent.probability.toFixed(2)} genus=${result.genus.value ?? "-"} [${result.reasons.filter((r) => r.severity !== "info").map((r) => r.code).join(",")}]${flag}`);
      return { case: c, result };
    });
    const m = metrics(rows);
    console.table(m);
    report[name] = {
      metrics: m,
      rows: rows.map((r) => ({ id: r.case.id, verdict: r.result.verdict, treeP: r.result.treePresent.probability, genus: r.result.genus.value, reasons: r.result.reasons.map((x) => x.code), costUsd: r.result.costUsd, models: r.result.models, jev: r.result.signals.jev, visionGenus: r.result.signals.vision.map((o) => [o.model, o.genus_candidates.map((g) => `${g.genus}:${g.probability.toFixed(2)}`)]) })),
    };
  }
  await cache.save();
  await writeFile(join(outDir, "report.json"), JSON.stringify(report, null, 2));
  console.log(`\nreport written to eval/out/report.json`);
}

await main();
