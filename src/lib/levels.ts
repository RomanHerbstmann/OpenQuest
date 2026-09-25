const thresholds = [0, 100, 250, 500, 800];

export function levelForXp(xp: number): number {
  for (let index = thresholds.length - 1; index >= 0; index -= 1) {
    if (xp >= thresholds[index]) return index + 1;
  }
  return 1;
}

export function levelProgress(xp: number) {
  const level = levelForXp(xp);
  const start = thresholds[level - 1];
  const next = thresholds[level] ?? start + 400;
  return { level, current: xp - start, required: next - start, percent: Math.min(100, ((xp - start) / (next - start)) * 100) };
}
