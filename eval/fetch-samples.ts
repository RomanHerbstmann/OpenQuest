/**
 * Downloads the freely licensed sample photos listed in eval/samples.json from Wikimedia Commons
 * into eval/images (gitignored). samples.json is curated by hand (labels checked visually), so it
 * is committed and only the images are fetched.
 *
 *   pnpm eval:fetch              download images listed in samples.json
 *   pnpm eval:fetch --discover   search Commons for new candidates -> samples.candidates.json (review before merging)
 */
import { mkdir, readFile, writeFile } from "node:fs/promises";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const here = dirname(fileURLToPath(import.meta.url));
const UA = "OpenQuest-eval/0.1 (https://github.com/RomanHerbstmann/OpenQuest)";

export interface SampleSpec {
  id: string;
  query: string;
  /** Ground truth: does the photo show a tree as a main subject? */
  tree: boolean;
  /** Ground truth genus for tree photos. */
  genus?: string;
  /** What a correct pipeline should say without any target mismatch. */
  expect: "approve" | "review" | "reject" | "not_approve";
  count: number;
}

const SPECS: SampleSpec[] = [
  ...["Tilia", "Quercus", "Acer", "Platanus", "Betula", "Carpinus", "Aesculus", "Fraxinus"].map((g) => ({
    id: g.toLowerCase(),
    query: `${g} tree street OR park -leaf -leaves -flower -bark`,
    tree: true,
    genus: g,
    expect: "approve" as const,
    count: 2,
  })),
  { id: "winter", query: "bare tree winter street avenue", tree: true, expect: "approve", count: 2 },
  { id: "hedge", query: "hedge garden trimmed", tree: false, expect: "reject", count: 2 },
  { id: "shrub", query: "shrub bush flowering garden", tree: false, expect: "not_approve", count: 2 },
  { id: "potted", query: "potted plant indoor", tree: false, expect: "reject", count: 2 },
  { id: "street", query: "empty street asphalt houses no trees", tree: false, expect: "reject", count: 2 },
  { id: "facade", query: "building facade Münster", tree: false, expect: "reject", count: 1 },
  { id: "lawn", query: "lawn grass field closeup", tree: false, expect: "reject", count: 1 },
  { id: "stump", query: "tree stump cut", tree: false, expect: "not_approve", count: 2 },
  { id: "painting", query: "painting of a tree oil canvas", tree: false, expect: "not_approve", count: 2 },
];

interface CommonsPage {
  title: string;
  imageinfo?: { thumburl?: string; mime?: string; extmetadata?: Record<string, { value?: string }> }[];
}

async function search(query: string, limit: number): Promise<CommonsPage[]> {
  const url = new URL("https://commons.wikimedia.org/w/api.php");
  url.search = new URLSearchParams({
    action: "query",
    format: "json",
    generator: "search",
    gsrnamespace: "6",
    gsrsearch: `${query} filetype:bitmap`,
    gsrlimit: String(limit * 3),
    prop: "imageinfo",
    iiprop: "url|mime|extmetadata",
    iiurlwidth: "1024",
  }).toString();
  const res = await fetch(url, { headers: { "User-Agent": UA } });
  const json = (await res.json()) as { query?: { pages?: Record<string, CommonsPage & { index: number }> } };
  return Object.values(json.query?.pages ?? {})
    .sort((a, b) => a.index - b.index)
    .filter((p) => p.imageinfo?.[0]?.mime === "image/jpeg");
}

const strip = (html?: string) => html?.replace(/<[^>]+>/g, "").trim() ?? "";

async function download(url: string, file: string): Promise<boolean> {
  const res = await fetch(url, { headers: { "User-Agent": UA } });
  if (!res.ok) return false;
  await writeFile(join(here, file), Buffer.from(await res.arrayBuffer()));
  return true;
}

async function fetchListed() {
  const samples = JSON.parse(await readFile(join(here, "samples.json"), "utf8")) as { file: string; title: string }[];
  for (const s of samples) {
    const name = encodeURIComponent(s.title.replace(/^File:/, "").replace(/ /g, "_"));
    const ok = await download(`https://commons.wikimedia.org/wiki/Special:FilePath/${name}?width=1024`, s.file);
    console.log(`${ok ? "ok  " : "FAIL"} ${s.file}`);
    await new Promise((r) => setTimeout(r, 300));
  }
}

async function discover() {
  const samples = [];
  for (const spec of SPECS) {
    const pages = (await search(spec.query, spec.count)).slice(0, spec.count);
    for (const [i, page] of pages.entries()) {
      const info = page.imageinfo![0]!;
      const file = `images/${spec.id}-${i + 1}.jpg`;
      if (!(await download(info.thumburl!, file))) continue;
      const meta = info.extmetadata ?? {};
      samples.push({
        file,
        title: page.title,
        source: `https://commons.wikimedia.org/wiki/${encodeURIComponent(page.title.replace(/ /g, "_"))}`,
        author: strip(meta.Artist?.value),
        license: strip(meta.LicenseShortName?.value),
        tree: spec.tree,
        genus: spec.genus ?? null,
        expect: spec.expect,
        group: spec.id,
      });
      console.log(`${file}  ${page.title}`);
      await new Promise((r) => setTimeout(r, 300));
    }
  }
  await writeFile(join(here, "samples.candidates.json"), JSON.stringify(samples, null, 2) + "\n");
  console.log(`\n${samples.length} candidates written to eval/samples.candidates.json, check labels visually before merging`);
}

await mkdir(join(here, "images"), { recursive: true });
await (process.argv.includes("--discover") ? discover() : fetchListed());
