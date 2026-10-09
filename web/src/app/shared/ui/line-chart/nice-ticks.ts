/** Round axis ticks ("nice numbers") covering [min, max], always including zero. */
export function niceTicks(min: number, max: number, targetCount = 5): number[] {
  const low = Math.min(min, 0);
  const high = Math.max(max, 0);
  if (low === high) {
    return [0, 1];
  }
  const rawStep = (high - low) / targetCount;
  const magnitude = 10 ** Math.floor(Math.log10(rawStep));
  const step =
    [1, 2, 2.5, 5, 10].map((m) => m * magnitude).find((s) => s >= rawStep) ?? 10 * magnitude;
  const ticks: number[] = [];
  for (let value = Math.floor(low / step) * step; value <= high + step / 2; value += step) {
    ticks.push(Math.round(value / step) * step);
    if (value >= high) {
      break;
    }
  }
  return ticks;
}
