const Z = 1.96;

/** 95 % Wilson score interval — honest at small samples and near 0 % or 100 %. */
export function wilson(made: number, n: number): { low: number; high: number } {
  if (n === 0) return { low: 0, high: 0 };
  const p = made / n;
  const z2 = Z * Z;
  const denom = 1 + z2 / n;
  const center = (p + z2 / (2 * n)) / denom;
  const half = (Z * Math.sqrt((p * (1 - p)) / n + z2 / (4 * n * n))) / denom;
  return {
    low: Math.max(0, center - half),
    high: Math.min(1, center + half),
  };
}
