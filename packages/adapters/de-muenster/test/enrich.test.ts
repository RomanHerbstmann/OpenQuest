import { describe, expect, it } from "vitest";
import { areaOf, parseStreetCsv } from "../src/index.ts";

describe("parseStreetCsv", () => {
  it("parses the WFS CSV with BOM and pads keys", () => {
    const m = parseStreetCsv('﻿STR_SCHL,NAME,STR_STATUS\n00457,Am Oedingteich,S\n1005,"Weg, am Bach",S\n');
    expect(m.get("00457")).toBe("Am Oedingteich");
    expect(m.get("01005")).toBe("Weg, am Bach");
  });
});

describe("areaOf", () => {
  it("finds the containing polygon", () => {
    const square: [number, number][] = [[7, 51], [8, 51], [8, 52], [7, 52], [7, 51]];
    expect(areaOf(7.5, 51.5, [{ name: "A", rings: [square] }])).toBe("A");
    expect(areaOf(9, 51.5, [{ name: "A", rings: [square] }])).toBeNull();
  });
});
