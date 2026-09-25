import { describe, expect, it } from "vitest";
import { aggregateGenus, normalizeGenus, parseObservation, runVisionEnsemble, treeProbability } from "../src/vision/ensemble.ts";
import type { OpenRouterClient } from "../src/vision/openrouter.ts";
import { obs } from "./helpers.ts";

describe("normalizeGenus", () => {
  it.each([
    ["Tilia cordata", "Tilia"],
    ["tilia", "Tilia"],
    ["Linden", "Tilia"],
    ["Malus-Hybride", "Malus"],
    ["unknown", null],
    ["", null],
  ])("%s -> %s", (raw, expected) => expect(normalizeGenus(raw)).toBe(expected));
});

describe("treeProbability", () => {
  it("maps answers to probabilities", () => {
    expect(treeProbability(obs({ tree_present: "yes", tree_present_confidence: 0.9 }))).toBe(0.9);
    expect(treeProbability(obs({ tree_present: "no", tree_present_confidence: 0.9 }))).toBeCloseTo(0.1);
    expect(treeProbability(obs({ tree_present: "uncertain", tree_present_confidence: 0.9 }))).toBe(0.5);
  });
});

describe("parseObservation", () => {
  it("strips code fences, normalizes genus and renormalizes probabilities", () => {
    const raw = { ...obs(), genus_candidates: [{ genus: "quercus robur", probability: 0.9, evidence: "lobed" }, { genus: "Fagus", probability: 0.6, evidence: "bark" }] };
    const o = parseObservation("x", "```json\n" + JSON.stringify(raw) + "\n```");
    expect(o.model).toBe("x");
    expect(o.genus_candidates.map((c) => c.genus)).toEqual(["Quercus", "Fagus"]);
    expect(o.genus_candidates[0]!.probability).toBeCloseTo(0.6);
  });

  it("rejects malformed answers", () => {
    expect(() => parseObservation("x", JSON.stringify({ ...obs(), tree_present: "maybe" }))).toThrow();
  });
});

describe("aggregateGenus", () => {
  it("averages candidate probabilities over voting models", () => {
    const g = aggregateGenus([
      obs({ genus_candidates: [{ genus: "Tilia", probability: 0.8, evidence: "" }] }),
      obs({ genus_candidates: [{ genus: "Tilia", probability: 0.4, evidence: "" }, { genus: "Populus", probability: 0.4, evidence: "" }] }),
    ]);
    expect(g[0]).toEqual({ genus: "Tilia", probability: 0.6000000000000001 });
    expect(g[1]).toEqual({ genus: "Populus", probability: 0.2 });
  });
});

function fakeClient(answers: Record<string, unknown | Error>): OpenRouterClient & { calls: string[] } {
  const calls: string[] = [];
  return {
    calls,
    async chat(req) {
      calls.push(req.model);
      const a = answers[req.model];
      if (a instanceof Error || a === undefined) throw a ?? new Error("no answer");
      return { content: JSON.stringify(a), costUsd: 0.001, latencyMs: 5 };
    },
  };
}

const image = { base64: "AAAA", mimeType: "image/jpeg" };
const opts = { models: ["a", "b"], escalationModel: "c", timeoutMs: 1000, unsureBand: [0.2, 0.85] as [number, number], maxSpread: 0.4 };

describe("runVisionEnsemble", () => {
  it("does not escalate when models agree", async () => {
    const client = fakeClient({ a: obs(), b: obs() });
    const r = await runVisionEnsemble(client, image, opts);
    expect(client.calls).toEqual(["a", "b"]);
    expect(r.escalated).toBe(false);
    expect(r.treeProbability).toBeCloseTo(0.95);
  });

  it("escalates on disagreement", async () => {
    const client = fakeClient({ a: obs(), b: obs({ tree_present: "no", tree_present_confidence: 0.9 }), c: obs() });
    const r = await runVisionEnsemble(client, image, opts);
    expect(client.calls).toEqual(["a", "b", "c"]);
    expect(r.escalated).toBe(true);
    expect(r.observations).toHaveLength(3);
  });

  it("escalates when a model fails and survives total failure", async () => {
    const partial = await runVisionEnsemble(fakeClient({ a: obs(), b: new Error("boom"), c: obs() }), image, opts);
    expect(partial.escalated).toBe(true);
    expect(partial.calls.find((c) => c.model === "b")?.ok).toBe(false);

    const none = await runVisionEnsemble(fakeClient({}), image, opts);
    expect(none.treeProbability).toBeNull();
  });

  it("escalates on conflicting top genus", async () => {
    const client = fakeClient({
      a: obs(),
      b: obs({ genus_candidates: [{ genus: "Populus", probability: 0.7, evidence: "" }] }),
      c: obs(),
    });
    await runVisionEnsemble(client, image, opts);
    expect(client.calls).toContain("c");
  });
});
