import { describe, expect, it } from "vitest";
import { buildQuestions, execute, interpret, parseDeterministic, parseWithRules, type JevLike, type TreeData } from "../src/search.ts";

const data: TreeData = {
  generatedAt: "",
  attribution: [],
  genera: ["Tilia", "Betula", "Quercus", "Ginkgo"],
  rawGenus: ["Tilia", "Betula", "Quercus", "Ginkgo", "Baum Amt62"],
  streets: ["Weseler Straße", "Aegidiistraße", "Am Stadtgraben"],
  districts: ["Münster-Mitte", "Münster-Hiltrup"],
  quarters: ["Kreuzviertel", "Hiltrup-Mitte"],
  trees: {
    id: ["a", "b", "c", "d", "e", "f"],
    lon: [7.6, 7.61, 7.62, 7.63, 7.64, 7.65],
    lat: [51.9, 51.91, 51.92, 51.93, 51.94, 51.95],
    genus: [0, 1, 1, 2, 3, -1],
    raw: [0, 1, 1, 2, 3, 4],
    height: [250, 180, 310, -1, 120, 15],
    street: [0, 1, 0, 2, -1, 0],
    district: [0, 1, 1, 0, 0, 1],
    quarter: [0, 1, 1, 0, 0, 1],
  },
};

const base = (o: Partial<ReturnType<typeof parseWithRules>> = {}) => ({ ...parseWithRules("", data), ...o });

describe("parseDeterministic", () => {
  it("extracts height ranges, limits and streets", () => {
    expect(parseDeterministic("Linden über 20 m", data)).toMatchObject({ minHeightM: 20 });
    expect(parseDeterministic("trees under 7,5 meters", data)).toMatchObject({ maxHeightM: 7.5 });
    expect(parseDeterministic("top 10 größte Bäume", data).limit).toBe(10);
    expect(parseDeterministic("Bäume an der Weseler Str.", data).street).toBe("Weseler Straße");
    expect(parseDeterministic("birken an der weseler", data).street).toBe("Weseler Straße");
    expect(parseDeterministic("birken weseler", data).street).toBeUndefined();
    expect(parseDeterministic("Linden am Stadtgraben", data).street).toBe("Am Stadtgraben");
  });
});

describe("parseWithRules", () => {
  it("maps German names to genera and finds sort and area", () => {
    const i = parseWithRules("die größten Birken in Hiltrup", data);
    expect(i.genus?.value).toBe("Betula");
    expect(i.sort.value).toBe("tallest");
    expect(i.area?.value).toBe("Münster-Hiltrup");
    expect(parseWithRules("wie viele Ginkgos", data).intent.value).toBe("count");
  });
});

describe("execute", () => {
  it("filters by genus and area and sorts by height", () => {
    const r = execute("q", data, base({ genus: { value: "Betula", confidence: 1 }, sort: { value: "tallest", confidence: 1 } }));
    expect(r.items.map((x) => x.id)).toEqual(["c", "b"]);
    expect(r.items[0]).toMatchObject({ genusDe: "Birke", heightM: 31, street: "Weseler Straße" });
    const hiltrup = execute("q", data, base({ area: { kind: "district", value: "Münster-Hiltrup", confidence: 1 } }));
    expect(hiltrup.total).toBe(3);
  });

  it("puts trees without height last and handles unknown genus", () => {
    expect(execute("q", data, base({ sort: { value: "tallest", confidence: 1 } })).items.at(-1)!.id).toBe("d");
    expect(execute("q", data, base({ genus: { value: "unknown", confidence: 1 } })).items.map((x) => x.id)).toEqual(["f"]);
    expect(execute("q", data, base({ genus: { value: "Pinus", confidence: 1 } })).total).toBe(0);
  });

  it("marks age as height proxy and drops < 2 m for shortest", () => {
    const r = execute("q", data, base({ sort: { value: "youngest", confidence: 1 } }));
    expect(r.items.map((x) => x.id)).toEqual(["e", "b", "a", "c"]);
    expect(r.interpretation.notes).toEqual(["age_is_height_proxy", "below_2m_excluded"]);
  });

  it("filters by height and street", () => {
    expect(execute("q", data, base({ minHeightM: 20 })).items.map((x) => x.id)).toEqual(["c", "a"]);
    expect(execute("q", data, base({ street: "Weseler Straße", maxHeightM: 26 })).items.map((x) => x.id)).toEqual(["a", "f"]);
  });

  it("summarizes in both languages", () => {
    const r = execute("q", data, base({ genus: { value: "Betula", confidence: 1 }, sort: { value: "tallest", confidence: 1 }, area: { kind: "district", value: "Münster-Hiltrup", confidence: 1 } }));
    expect(r.summary.de).toBe("2 Bäume · Birke (Betula) · Hiltrup · höchste zuerst");
    expect(r.summary.en).toBe("2 trees · birch (Betula) · Hiltrup · tallest first");
  });
});

describe("interpret with Jev", () => {
  it("describes genera with common names and uses Jev answers above threshold", async () => {
    const q = buildQuestions(data);
    expect((q.genus as { criteria: Record<string, unknown> }).criteria.Betula).toContain("Birke");
    const jev: JevLike = {
      systemOne: async () => ({
        answers: {
          sort: { choice: "tallest", confidence: 0.9 },
          genus: { choice: "Betula", confidence: 0.95 },
          area: { choice: "Kreuzviertel", confidence: 0.3 },
          intent: { choice: "list", confidence: 0.9 },
        },
        usage: { cost: 0.00001 },
      }),
    };
    const { interpretation } = await interpret("größte Birken", data, jev, "m");
    expect(interpretation).toMatchObject({ source: "jev", sort: { value: "tallest" }, genus: { value: "Betula" } });
    expect(interpretation.area).toBeUndefined();
  });

  it("falls back to rules when Jev fails", async () => {
    const jev: JevLike = { systemOne: async () => Promise.reject(new Error("down")) };
    const { interpretation } = await interpret("größte Birken", data, jev, "m");
    expect(interpretation).toMatchObject({ source: "rules", genus: { value: "Betula" }, notes: ["jev_unavailable"] });
  });
});
