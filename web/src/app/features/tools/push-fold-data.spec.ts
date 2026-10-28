import { HAND_NAMES } from '../../shared/ui/range-selector/range-selector';
import { getPushRange, POSITIONS, STACK_SIZES } from './push-fold-data';

describe('getPushRange', () => {
  it('returns a non-empty set for every position × stack combination', () => {
    for (const position of POSITIONS) {
      for (const bb of STACK_SIZES) {
        const range = getPushRange(position, bb);
        expect(range.size).toBeGreaterThan(0);
      }
    }
  });

  it('returns an empty set for an unknown stack size', () => {
    expect(getPushRange('SB', 99).size).toBe(0);
  });

  it('contains only valid hand indices', () => {
    const maxIndex = HAND_NAMES.length;
    for (const position of POSITIONS) {
      for (const bb of STACK_SIZES) {
        for (const idx of getPushRange(position, bb)) {
          expect(idx).toBeGreaterThanOrEqual(0);
          expect(idx).toBeLessThan(maxIndex);
        }
      }
    }
  });

  it('SB range is wider than LJ range at the same stack depth', () => {
    for (const bb of STACK_SIZES) {
      const sb = getPushRange('SB', bb);
      const lj = getPushRange('LJ', bb);
      expect(sb.size).toBeGreaterThanOrEqual(lj.size);
    }
  });

  it('ranges get tighter as stack grows (same position)', () => {
    for (const position of POSITIONS) {
      let prev = Infinity;
      for (const bb of STACK_SIZES) {
        const size = getPushRange(position, bb).size;
        expect(size).toBeLessThanOrEqual(prev);
        prev = size;
      }
    }
  });

  it('always includes pocket aces (AA is index 0)', () => {
    for (const position of POSITIONS) {
      for (const bb of STACK_SIZES) {
        expect(getPushRange(position, bb).has(0)).toBe(true);
      }
    }
  });

  it('returns the same reference on repeated calls (caching)', () => {
    const a = getPushRange('BTN', 10);
    const b = getPushRange('BTN', 10);
    expect(a).toBe(b);
  });
});
