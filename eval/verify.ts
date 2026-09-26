/**
 * Manual check of a single photo:
 *
 *   pnpm verify <image> [--lat 51.9622 --lon 7.6255] [--genus Tilia] [--player-lat .. --player-lon ..] [--no-jev]
 *
 * Without --genus the nearest tree from the Münster tree inventory at --lat/--lon becomes the target.
 */
import { readFile } from "node:fs/promises";
import { parseArgs } from "node:util";
import { createMuensterTreeProvider } from "@openquest/adapter-de-muenster";
import { createOsmTreeProvider, resolveConfig, verifyTreePhoto, type ExpectedTree } from "@openquest/tree-verification";

const { values, positionals } = parseArgs({
  allowPositionals: true,
  options: {
    lat: { type: "string" },
    lon: { type: "string" },
    genus: { type: "string" },
    "player-lat": { type: "string" },
    "player-lon": { type: "string" },
    "no-jev": { type: "boolean", default: false },
    json: { type: "boolean", default: false },
  },
});

const file = positionals[0];
if (!file) {
  console.error("usage: pnpm verify <image> [--lat --lon] [--genus Tilia] [--player-lat --player-lon] [--no-jev] [--json]");
  process.exit(1);
}

const config = resolveConfig(values["no-jev"] ? { jevModel: null } : {});
const muenster = createMuensterTreeProvider();
const providers = [muenster, createOsmTreeProvider()];

let expected: ExpectedTree | undefined;
if (values.lat && values.lon) {
  const position = { lat: Number(values.lat), lon: Number(values.lon) };
  if (values.genus !== undefined) {
    expected = { position, genus: values.genus || null };
  } else {
    const nearest = (await muenster.findNearby(position, 25))[0];
    expected = nearest ? { position: nearest.position, genus: nearest.genus, externalId: nearest.externalId } : { position, genus: null };
    console.error(`target: ${nearest ? `${nearest.genus ?? "unknown genus"} (${nearest.externalId})` : "no inventory tree within 25 m"}`);
  }
}
const playerPosition =
  values["player-lat"] && values["player-lon"]
    ? { lat: Number(values["player-lat"]), lon: Number(values["player-lon"]), accuracyM: 8 }
    : expected
      ? { ...expected.position, accuracyM: 8 }
      : undefined;

const result = await verifyTreePhoto({ image: { data: await readFile(file) }, expected, playerPosition }, { config, providers });

if (values.json) {
  console.log(JSON.stringify(result, null, 2));
} else {
  console.log(`\nverdict:      ${result.verdict.toUpperCase()}`);
  console.log(`tree:         ${result.treePresent.value} (p=${result.treePresent.probability.toFixed(2)})`);
  if (result.targetMatch) console.log(`target:       ${result.targetMatch.value} (p=${result.targetMatch.probability.toFixed(2)})`);
  console.log(`genus:        ${result.genus.value ?? "-"} ${result.genus.alternatives.map((g) => `${g.genus} ${(g.probability * 100).toFixed(0)}%`).join(", ")}`);
  if (result.genusSuggestion) console.log(`suggestion:   genus=${result.genusSuggestion.genus}`);
  const a = result.assessment;
  if (a) {
    console.log(`inventory:    ${result.inventory}`);
    console.log(`condition:    vitality=${a.vitality.value} crown=${a.crownDensity.value} age=${a.ageClass.value} phenology=${a.phenology.value}`);
    if (a.damage.length || a.pests.length) console.log(`findings:     ${[...a.damage, ...a.pests].map((f) => `${f.value} ${(f.confidence * 100).toFixed(0)}%`).join(", ")}`);
    if (a.safetyFlags.length) console.log(`SAFETY:       ${a.safetyFlags.map((f) => `${f.value} ${(f.confidence * 100).toFixed(0)}%`).join(", ")}`);
    console.log(`tree pit:     ${a.treePit.visible ? `${a.treePit.surface.value}${a.treePit.wateringBag ? ", watering bag" : ""}${a.treePit.stakes ? ", stakes" : ""}` : "not visible"}`);
  }
  console.log(`proposals:    ${result.proposedChanges.map((c) => `${c.key}=${JSON.stringify(c.value)}${c.requiresReview ? " (review)" : ""}`).join("; ") || "-"}`);
  console.log(`reasons:      ${result.reasons.map((r) => `${r.code}${r.detail ? ` (${r.detail})` : ""}`).join("; ")}`);
  console.log(`nearby trees: ${result.signals.nearbyTrees.slice(0, 5).map((t) => `${t.genus ?? "?"}@${t.distanceM?.toFixed(0)}m[${t.source}]`).join(", ") || "-"}`);
  console.log(`models:       ${result.models.map((m) => `${m.model}${m.ok ? "" : " FAILED"} ${(m.latencyMs / 1000).toFixed(1)}s`).join(", ")}`);
  console.log(`cost:         $${result.costUsd.toFixed(5)}  latency ${(result.latencyMs / 1000).toFixed(1)}s`);
}
