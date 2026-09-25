import { choice, type Questions } from "@typesafe-ai/sdk";
import { englishName, GENUS_NAMES, germanName } from "./genera.ts";

/** Columnar tree data as written by scripts/build-data.ts. */
export interface TreeData {
  generatedAt: string;
  attribution: string[];
  genera: string[];
  rawGenus: string[];
  streets: string[];
  districts: string[];
  quarters: string[];
  trees: {
    id: string[];
    lon: number[];
    lat: number[];
    genus: number[];
    raw: number[];
    /** decimeters, -1 = no height */
    height: number[];
    street: number[];
    district: number[];
    quarter: number[];
  };
}

export type SortKey = "none" | "tallest" | "shortest" | "oldest" | "youngest" | "rarest" | "most_common";

export interface Interpretation {
  sort: { value: SortKey; confidence: number };
  /** Latin genus, or "unknown" for trees without a usable genus. */
  genus?: { value: string; confidence: number };
  area?: { kind: "district" | "quarter"; value: string; confidence: number };
  street?: string;
  minHeightM?: number;
  maxHeightM?: number;
  limit: number;
  intent: { value: "list" | "count"; confidence: number };
  source: "jev" | "rules";
  /** Machine readable hints for the UI, e.g. "age_is_height_proxy". */
  notes: string[];
}

export interface ResultItem {
  rank: number;
  index: number;
  id: string;
  genus: string | null;
  genusDe: string | null;
  genusEn: string | null;
  rawGenus: string;
  heightM: number | null;
  street: string | null;
  district: string | null;
  quarter: string | null;
  lat: number;
  lon: number;
}

export interface SearchResult {
  query: string;
  interpretation: Interpretation;
  total: number;
  items: ResultItem[];
  /** Indices of all matching trees, for highlighting on the map. */
  matches: number[];
  summary: { de: string; en: string };
  jevLatencyMs?: number;
  costUsd?: number;
}

export interface JevLike {
  systemOne(req: { state: unknown; questions: Questions; model?: string }): PromiseLike<{
    answers: Record<string, { choice?: string; confidence?: number; probabilities?: Record<string, number> }>;
    usage: { cost?: number };
  }>;
}

const DEFAULT_LIMIT = 25;
const MAX_LIMIT = 200;
const ACCEPT = { genus: 0.4, area: 0.4, sort: 0.35 };

const norm = (s: string) =>
  s
    .toLowerCase()
    .replace(/ß/g, "ss")
    .replace(/str\.(?=\s|$)/g, "strasse")
    .normalize("NFD")
    .replace(/[̀-ͯ]/g, "");

/** Numbers, height ranges, limits and street names: things Jev cannot output as typed values. */
export function parseDeterministic(query: string, data: TreeData): Pick<Interpretation, "street" | "minHeightM" | "maxHeightM" | "limit"> {
  const q = norm(query);
  const out: Pick<Interpretation, "street" | "minHeightM" | "maxHeightM" | "limit"> = { limit: DEFAULT_LIMIT };

  const num = "(\\d+(?:[.,]\\d+)?)\\s*(?:m|meter|metern|metres|meters)\\b";
  const min = q.match(new RegExp(`(?:uber|mehr als|hoher als|grosser als|ab|mindestens|>|over|above|taller than|more than|at least)\\s*${num}`));
  const max = q.match(new RegExp(`(?:unter|weniger als|kleiner als|niedriger als|bis|hochstens|<|under|below|less than|at most|up to)\\s*${num}`));
  if (min) out.minHeightM = Number(min[1]!.replace(",", "."));
  if (max) out.maxHeightM = Number(max[1]!.replace(",", "."));

  const limit = q.match(/(?:top|die|the|erste[n]?|first)\s*(\d{1,3})\b/) ?? q.match(/\b(\d{1,3})\s*(?:grosste|hochste|hoechste|kleinste|alteste|tallest|largest|biggest|oldest|smallest|baume|trees)/);
  if (limit) out.limit = Math.min(MAX_LIMIT, Math.max(1, Number(limit[1])));

  // Longest street name contained in the query. Without the suffix ("an der Weseler") only after a
  // street preposition, otherwise "Birken" would match "Birkenweg".
  let best: string | undefined;
  for (const street of data.streets) {
    const s = norm(street);
    const stem = s.replace(/\s*(strasse|weg|allee|platz|ring|gasse|damm|ufer)$/, "").trim();
    const escaped = stem.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
    const hit =
      (s.length >= 4 && new RegExp(`\\b${s.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")}\\b`).test(q)) ||
      (stem.length >= 5 && new RegExp(`\\b(an der|an dem|am|an|entlang der|auf der|in der|on|along)\\s+${escaped}\\b`).test(q));
    if (hit && (!best || street.length > best.length)) best = street;
  }
  if (best) out.street = best;
  return out;
}

const SORT_CRITERIA = {
  none: "no ordering requested, just a filter or a count",
  tallest: "largest, tallest, highest or biggest trees first (groß, größte, höchste, riesig)",
  shortest: "smallest or lowest trees first (klein, kleinste, niedrig)",
  oldest: "oldest trees first (alt, älteste, uralt)",
  youngest: "youngest or recently planted trees first (jung, jüngste, neu gepflanzt)",
  rarest: "rare or unusual tree types first (selten, besondere, exotisch)",
  most_common: "most common tree types first (häufig, häufigste, typisch)",
} as const;

export function buildQuestions(data: TreeData): Questions {
  const genusCriteria: Record<string, string | null> = { none: "no specific tree type or genus is mentioned" };
  for (const g of data.genera) {
    const n = GENUS_NAMES[g];
    genusCriteria[g] = n ? `German: ${n.de.join(", ")}; English: ${n.en.join(", ")}${n.hint ? `; ${n.hint}` : ""}` : null;
  }
  genusCriteria.unknown = "trees whose type or genus is unknown or not recorded (unbekannt, ohne Gattung)";

  const areaCriteria: Record<string, string | null> = { none: "no district, quarter or part of the city is mentioned" };
  for (const d of data.districts) areaCriteria[d] = "Stadtbezirk (city district) of Münster";
  for (const q of data.quarters) areaCriteria[q] ??= "Stadtteil (quarter) of Münster";

  return {
    sort: choice("How should the trees in the search be ordered?", SORT_CRITERIA),
    genus: choice("Which tree genus does the search ask for?", genusCriteria),
    area: choice("Which part of Münster does the search ask for?", areaCriteria),
    intent: choice("What does the user want?", {
      list: "see matching trees on the map or in a list",
      count: "know how many trees match (wie viele, Anzahl, how many)",
    }),
  };
}

/** Rule based interpretation, used when Jev is unavailable. */
export function parseWithRules(query: string, data: TreeData): Interpretation {
  const q = norm(query);
  const has = (...words: string[]) => words.some((w) => new RegExp(`\\b${norm(w)}`).test(q));
  const sort: SortKey = has("gross", "hoch", "hochste", "riesig", "tall", "big", "large", "high")
    ? "tallest"
    : has("klein", "niedrig", "small", "short", "low")
      ? "shortest"
      : has("alt", "old")
        ? "oldest"
        : has("jung", "young", "neu gepflanzt")
          ? "youngest"
          : has("selten", "rare", "exot")
            ? "rarest"
            : has("haufig", "common")
              ? "most_common"
              : "none";

  let genus: Interpretation["genus"];
  for (const g of data.genera) {
    const names = [g, ...(GENUS_NAMES[g]?.de ?? []), ...(GENUS_NAMES[g]?.en ?? [])];
    if (names.some((n) => new RegExp(`\\b${norm(n)}(n|en|e|s|es)?\\b`).test(q))) {
      genus = { value: g, confidence: 1 };
      break;
    }
  }
  if (!genus && has("unbekannt", "ohne gattung", "unknown")) genus = { value: "unknown", confidence: 1 };

  let area: Interpretation["area"];
  const areas = [...data.districts.map((v) => ({ kind: "district" as const, v })), ...data.quarters.map((v) => ({ kind: "quarter" as const, v }))];
  for (const a of areas.sort((x, y) => y.v.length - x.v.length)) {
    const n = norm(a.v).replace(/^munster-/, "");
    if (n.length >= 4 && q.includes(n)) {
      area = { kind: a.kind, value: a.v, confidence: 1 };
      break;
    }
  }

  return {
    sort: { value: sort, confidence: sort === "none" ? 0 : 1 },
    genus,
    area,
    intent: { value: has("wie viele", "anzahl", "how many") ? "count" : "list", confidence: 1 },
    source: "rules",
    notes: [],
    ...parseDeterministic(query, data),
  };
}

export async function interpret(query: string, data: TreeData, jev: JevLike | null, model: string): Promise<{ interpretation: Interpretation; latencyMs?: number; costUsd?: number }> {
  const det = parseDeterministic(query, data);
  if (!jev) return { interpretation: parseWithRules(query, data) };
  const started = Date.now();
  try {
    const res = await jev.systemOne({ model, state: { search_query: query, city: "Münster, Germany" }, questions: buildQuestions(data) });
    const a = res.answers;
    const c = (k: string) => ({ choice: a[k]?.choice ?? "none", confidence: a[k]?.confidence ?? 0 });
    const sort = c("sort");
    const genus = c("genus");
    const area = c("area");
    const intent = c("intent");
    const interpretation: Interpretation = {
      sort: sort.choice !== "none" && sort.confidence >= ACCEPT.sort ? { value: sort.choice as SortKey, confidence: sort.confidence } : { value: "none", confidence: sort.confidence },
      genus: genus.choice !== "none" && genus.confidence >= ACCEPT.genus ? { value: genus.choice, confidence: genus.confidence } : undefined,
      area:
        area.choice !== "none" && area.confidence >= ACCEPT.area
          ? { kind: data.districts.includes(area.choice) ? "district" : "quarter", value: area.choice, confidence: area.confidence }
          : undefined,
      intent: { value: intent.choice === "count" ? "count" : "list", confidence: intent.confidence },
      source: "jev",
      notes: [],
      ...det,
    };
    return { interpretation, latencyMs: Date.now() - started, costUsd: res.usage.cost };
  } catch {
    const interpretation = parseWithRules(query, data);
    interpretation.notes.push("jev_unavailable");
    return { interpretation };
  }
}

/** Filters and orders the trees. Pure and deterministic, so results are reproducible. */
export function execute(query: string, data: TreeData, interp: Interpretation): SearchResult {
  const t = data.trees;
  const n = t.id.length;
  // Target index per filter: null = no filter, NaN = unknown value (matches nothing), -1 = "not recorded".
  const target = (value: string | undefined, list: string[]) => (value === undefined ? null : list.indexOf(value) >= 0 ? list.indexOf(value) : NaN);
  const genusTarget = !interp.genus ? null : interp.genus.value === "unknown" ? -1 : target(interp.genus.value, data.genera);
  const areaList = interp.area?.kind === "district" ? t.district : t.quarter;
  const areaTarget = interp.area ? target(interp.area.value, interp.area.kind === "district" ? data.districts : data.quarters) : null;
  const streetTarget = target(interp.street, data.streets);
  const minDm = interp.minHeightM !== undefined ? interp.minHeightM * 10 : undefined;
  const maxDm = interp.maxHeightM !== undefined ? interp.maxHeightM * 10 : undefined;

  const matches: number[] = [];
  for (let i = 0; i < n; i++) {
    if (genusTarget !== null && t.genus[i] !== genusTarget) continue;
    if (areaTarget !== null && areaList[i] !== areaTarget) continue;
    if (streetTarget !== null && t.street[i] !== streetTarget) continue;
    const h = t.height[i]!;
    if (minDm !== undefined && !(h >= 0 && h > minDm)) continue;
    if (maxDm !== undefined && !(h >= 0 && h < maxDm)) continue;
    matches.push(i);
  }

  const notes = [...interp.notes];
  const sort = interp.sort.value;
  const byHeight = (dir: 1 | -1) => (a: number, b: number) => {
    const ha = t.height[a]!;
    const hb = t.height[b]!;
    if (ha < 0 || hb < 0) return ha < 0 ? (hb < 0 ? 0 : 1) : -1; // no height last
    return dir * (ha - hb);
  };
  const freq = new Map<number, number>();
  for (const g of t.genus) freq.set(g, (freq.get(g) ?? 0) + 1);

  // Trees below 2 m in the surface model are mostly young, pruned or missing trees, not "small trees".
  let ordered = [...matches];
  if (sort === "tallest" || sort === "oldest" || sort === "none") ordered.sort(byHeight(-1));
  else if (sort === "shortest" || sort === "youngest") ordered = ordered.filter((i) => t.height[i]! >= 20).sort(byHeight(1));
  else if (sort === "rarest") ordered.sort((a, b) => freq.get(t.genus[a]!)! - freq.get(t.genus[b]!)! || byHeight(-1)(a, b));
  else if (sort === "most_common") ordered.sort((a, b) => freq.get(t.genus[b]!)! - freq.get(t.genus[a]!)! || byHeight(-1)(a, b));
  if (sort === "oldest" || sort === "youngest") notes.push("age_is_height_proxy");
  if (sort === "shortest" || sort === "youngest") notes.push("below_2m_excluded");

  const items: ResultItem[] = ordered.slice(0, interp.limit).map((i, k) => {
    const g = t.genus[i]! >= 0 ? data.genera[t.genus[i]!]! : null;
    return {
      rank: k + 1,
      index: i,
      id: t.id[i]!,
      genus: g,
      genusDe: g && germanName(g),
      genusEn: g && englishName(g),
      rawGenus: data.rawGenus[t.raw[i]!] ?? "",
      heightM: t.height[i]! >= 0 ? t.height[i]! / 10 : null,
      street: t.street[i]! >= 0 ? data.streets[t.street[i]!]! : null,
      district: t.district[i]! >= 0 ? data.districts[t.district[i]!]! : null,
      quarter: t.quarter[i]! >= 0 ? data.quarters[t.quarter[i]!]! : null,
      lat: t.lat[i]!,
      lon: t.lon[i]!,
    };
  });

  const interpretation = { ...interp, notes };
  return { query, interpretation, total: matches.length, items, matches, summary: summarize(interpretation, matches.length) };
}

const fmt = (n: number, locale: string) => n.toLocaleString(locale);

/** Compact "facet" style summary: avoids plural grammar for 64 genera in two languages. */
export function summarize(i: Interpretation, total: number): { de: string; en: string } {
  const sortDe: Record<SortKey, string> = {
    none: "",
    tallest: "höchste zuerst",
    shortest: "niedrigste zuerst",
    oldest: "älteste zuerst (Höhe als Näherung)",
    youngest: "jüngste zuerst (Höhe als Näherung)",
    rarest: "seltene Gattungen zuerst",
    most_common: "häufige Gattungen zuerst",
  };
  const sortEn: Record<SortKey, string> = {
    none: "",
    tallest: "tallest first",
    shortest: "lowest first",
    oldest: "oldest first (height as proxy)",
    youngest: "youngest first (height as proxy)",
    rarest: "rare genera first",
    most_common: "common genera first",
  };
  const g = i.genus;
  const parts = (l: "de" | "en") => [
    `${fmt(total, l === "de" ? "de-DE" : "en-GB")} ${l === "de" ? (total === 1 ? "Baum" : "Bäume") : total === 1 ? "tree" : "trees"}`,
    g && (g.value === "unknown" ? (l === "de" ? "ohne Gattung" : "without genus") : `${l === "de" ? germanName(g.value) : englishName(g.value)} (${g.value})`),
    i.area?.value.replace(/^Münster-/, ""),
    i.street,
    i.minHeightM !== undefined && `> ${i.minHeightM} m`,
    i.maxHeightM !== undefined && `< ${i.maxHeightM} m`,
    (l === "de" ? sortDe : sortEn)[i.sort.value],
  ].filter(Boolean).join(" · ");
  return { de: parts("de"), en: parts("en") };
}
