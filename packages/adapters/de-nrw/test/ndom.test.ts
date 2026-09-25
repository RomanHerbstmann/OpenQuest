import { describe, expect, it } from "vitest";
import { percentile, toUtm32 } from "../src/index.ts";

describe("toUtm32", () => {
  it("matches a reference point in Münster (EPSG:25832)", () => {
    const { x, y } = toUtm32(51.96219241316168, 7.625453743968288);
    expect(x).toBeCloseTo(405559.35, 1);
    expect(y).toBeCloseTo(5757725.46, 1);
  });
});

describe("percentile", () => {
  it("ignores nodata and returns the p-quantile", () => {
    expect(percentile([1, 2, 3, 4, NaN, -9999, 10], 0.95)).toBe(10);
    expect(percentile([1, 2, 3, 4], 0.5)).toBe(3);
    expect(percentile([], 0.95)).toBeNull();
  });
});
