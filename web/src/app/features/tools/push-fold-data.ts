import { HAND_NAMES } from '../../shared/ui/range-selector/range-selector';

export type Position = 'SB' | 'BTN' | 'CO' | 'HJ' | 'LJ';

export const POSITIONS: readonly Position[] = ['SB', 'BTN', 'CO', 'HJ', 'LJ'];

export const STACK_SIZES: readonly number[] = [3, 4, 5, 6, 7, 8, 9, 10, 12, 15, 18, 20];

const handIndex = new Map<string, number>();
HAND_NAMES.forEach((name, i) => handIndex.set(name, i));

function idx(hand: string): number {
  const i = handIndex.get(hand);
  if (i === undefined) throw new Error(`Unknown hand: ${hand}`);
  return i;
}

function toSet(hands: readonly string[]): Set<number> {
  return new Set(hands.map(idx));
}

// Sklansky-Chubukov adjusted Nash push ranges per position and effective stack (BB).
// Approximations suitable for low-stakes MTT; not solver output.
// Each key is `${position}_${bb}`.

const pairs = (from: string, to: string): string[] => {
  const ranks = 'AKQJT98765432';
  const a = ranks.indexOf(from);
  const b = ranks.indexOf(to);
  const result: string[] = [];
  for (let i = a; i <= b; i++) result.push(ranks[i] + ranks[i]);
  return result;
};

const suited = (high: string, from: string, to: string): string[] => {
  const ranks = 'AKQJT98765432';
  const a = ranks.indexOf(from);
  const b = ranks.indexOf(to);
  const result: string[] = [];
  for (let i = a; i <= b; i++) result.push(high + ranks[i] + 's');
  return result;
};

const offsuit = (high: string, from: string, to: string): string[] => {
  const ranks = 'AKQJT98765432';
  const a = ranks.indexOf(from);
  const b = ranks.indexOf(to);
  const result: string[] = [];
  for (let i = a; i <= b; i++) result.push(high + ranks[i] + 'o');
  return result;
};

// SB ranges (heads-up vs BB — widest)
const SB_3: readonly string[] = [
  ...pairs('A', '2'),
  ...suited('A', 'K', '2'),
  ...suited('K', 'Q', '2'),
  ...suited('Q', 'J', '2'),
  ...suited('J', 'T', '3'),
  ...suited('T', '9', '4'),
  ...suited('9', '8', '5'),
  ...suited('8', '7', '6'),
  ...offsuit('A', 'K', '2'),
  ...offsuit('K', 'Q', '2'),
  ...offsuit('Q', 'J', '3'),
  ...offsuit('J', 'T', '5'),
  ...offsuit('T', '9', '6'),
  ...offsuit('9', '8', '7'),
];

const SB_5: readonly string[] = [
  ...pairs('A', '2'),
  ...suited('A', 'K', '2'),
  ...suited('K', 'Q', '2'),
  ...suited('Q', 'J', '3'),
  ...suited('J', 'T', '5'),
  ...suited('T', '9', '6'),
  ...suited('9', '8', '7'),
  ...suited('8', '7', '7'),
  ...offsuit('A', 'K', '2'),
  ...offsuit('K', 'Q', '3'),
  ...offsuit('Q', 'J', '6'),
  ...offsuit('J', 'T', '7'),
  ...offsuit('T', '9', '8'),
];

const SB_7: readonly string[] = [
  ...pairs('A', '2'),
  ...suited('A', 'K', '2'),
  ...suited('K', 'Q', '3'),
  ...suited('Q', 'J', '5'),
  ...suited('J', 'T', '7'),
  ...suited('T', '9', '8'),
  ...suited('9', '8', '8'),
  ...offsuit('A', 'K', '2'),
  ...offsuit('K', 'Q', '5'),
  ...offsuit('Q', 'J', '8'),
  ...offsuit('J', 'T', '8'),
];

const SB_10: readonly string[] = [
  ...pairs('A', '2'),
  ...suited('A', 'K', '2'),
  ...suited('K', 'Q', '5'),
  ...suited('Q', 'J', '8'),
  ...suited('J', 'T', '9'),
  ...offsuit('A', 'K', '3'),
  ...offsuit('K', 'Q', '7'),
  ...offsuit('Q', 'J', '9'),
  ...offsuit('J', 'T', 'T'),
];

const SB_15: readonly string[] = [
  ...pairs('A', '3'),
  ...suited('A', 'K', '2'),
  ...suited('K', 'Q', '8'),
  ...suited('Q', 'J', 'T'),
  ...offsuit('A', 'K', '5'),
  ...offsuit('K', 'Q', '9'),
  ...offsuit('Q', 'J', 'T'),
];

const SB_20: readonly string[] = [
  ...pairs('A', '5'),
  ...suited('A', 'K', '3'),
  ...suited('K', 'Q', 'T'),
  ...suited('Q', 'J', 'J'),
  ...offsuit('A', 'K', '7'),
  ...offsuit('K', 'Q', 'T'),
];

// BTN ranges
const BTN_3: readonly string[] = [
  ...pairs('A', '2'),
  ...suited('A', 'K', '2'),
  ...suited('K', 'Q', '2'),
  ...suited('Q', 'J', '3'),
  ...suited('J', 'T', '5'),
  ...suited('T', '9', '6'),
  ...suited('9', '8', '7'),
  ...offsuit('A', 'K', '2'),
  ...offsuit('K', 'Q', '3'),
  ...offsuit('Q', 'J', '5'),
  ...offsuit('J', 'T', '7'),
  ...offsuit('T', '9', '8'),
];

const BTN_5: readonly string[] = [
  ...pairs('A', '2'),
  ...suited('A', 'K', '2'),
  ...suited('K', 'Q', '4'),
  ...suited('Q', 'J', '6'),
  ...suited('J', 'T', '8'),
  ...suited('T', '9', '9'),
  ...offsuit('A', 'K', '2'),
  ...offsuit('K', 'Q', '6'),
  ...offsuit('Q', 'J', '8'),
  ...offsuit('J', 'T', '9'),
];

const BTN_7: readonly string[] = [
  ...pairs('A', '2'),
  ...suited('A', 'K', '2'),
  ...suited('K', 'Q', '6'),
  ...suited('Q', 'J', '8'),
  ...suited('J', 'T', '9'),
  ...offsuit('A', 'K', '3'),
  ...offsuit('K', 'Q', '8'),
  ...offsuit('Q', 'J', 'T'),
];

const BTN_10: readonly string[] = [
  ...pairs('A', '2'),
  ...suited('A', 'K', '3'),
  ...suited('K', 'Q', '8'),
  ...suited('Q', 'J', 'T'),
  ...offsuit('A', 'K', '5'),
  ...offsuit('K', 'Q', 'T'),
  ...offsuit('Q', 'J', 'J'),
];

const BTN_15: readonly string[] = [
  ...pairs('A', '4'),
  ...suited('A', 'K', '5'),
  ...suited('K', 'Q', 'T'),
  ...suited('Q', 'J', 'J'),
  ...offsuit('A', 'K', '8'),
  ...offsuit('K', 'Q', 'J'),
];

const BTN_20: readonly string[] = [
  ...pairs('A', '6'),
  ...suited('A', 'K', '7'),
  ...suited('K', 'Q', 'J'),
  ...offsuit('A', 'K', 'T'),
  ...offsuit('K', 'Q', 'Q'),
];

// CO ranges
const CO_3: readonly string[] = [
  ...pairs('A', '2'),
  ...suited('A', 'K', '2'),
  ...suited('K', 'Q', '3'),
  ...suited('Q', 'J', '5'),
  ...suited('J', 'T', '7'),
  ...suited('T', '9', '8'),
  ...offsuit('A', 'K', '2'),
  ...offsuit('K', 'Q', '5'),
  ...offsuit('Q', 'J', '7'),
  ...offsuit('J', 'T', '9'),
];

const CO_5: readonly string[] = [
  ...pairs('A', '2'),
  ...suited('A', 'K', '2'),
  ...suited('K', 'Q', '6'),
  ...suited('Q', 'J', '8'),
  ...suited('J', 'T', '9'),
  ...offsuit('A', 'K', '4'),
  ...offsuit('K', 'Q', '8'),
  ...offsuit('Q', 'J', 'T'),
];

const CO_7: readonly string[] = [
  ...pairs('A', '2'),
  ...suited('A', 'K', '3'),
  ...suited('K', 'Q', '8'),
  ...suited('Q', 'J', 'T'),
  ...offsuit('A', 'K', '6'),
  ...offsuit('K', 'Q', 'T'),
  ...offsuit('Q', 'J', 'J'),
];

const CO_10: readonly string[] = [
  ...pairs('A', '3'),
  ...suited('A', 'K', '5'),
  ...suited('K', 'Q', 'T'),
  ...suited('Q', 'J', 'J'),
  ...offsuit('A', 'K', '8'),
  ...offsuit('K', 'Q', 'J'),
];

const CO_15: readonly string[] = [
  ...pairs('A', '6'),
  ...suited('A', 'K', '7'),
  ...suited('K', 'Q', 'J'),
  ...offsuit('A', 'K', 'T'),
  ...offsuit('K', 'Q', 'Q'),
];

const CO_20: readonly string[] = [
  ...pairs('A', '8'),
  ...suited('A', 'K', '9'),
  ...suited('K', 'Q', 'Q'),
  ...offsuit('A', 'K', 'J'),
];

// HJ ranges
const HJ_3: readonly string[] = [
  ...pairs('A', '2'),
  ...suited('A', 'K', '2'),
  ...suited('K', 'Q', '5'),
  ...suited('Q', 'J', '7'),
  ...suited('J', 'T', '9'),
  ...offsuit('A', 'K', '3'),
  ...offsuit('K', 'Q', '7'),
  ...offsuit('Q', 'J', '9'),
];

const HJ_5: readonly string[] = [
  ...pairs('A', '2'),
  ...suited('A', 'K', '3'),
  ...suited('K', 'Q', '8'),
  ...suited('Q', 'J', 'T'),
  ...offsuit('A', 'K', '6'),
  ...offsuit('K', 'Q', 'T'),
];

const HJ_7: readonly string[] = [
  ...pairs('A', '3'),
  ...suited('A', 'K', '5'),
  ...suited('K', 'Q', 'T'),
  ...suited('Q', 'J', 'J'),
  ...offsuit('A', 'K', '8'),
  ...offsuit('K', 'Q', 'J'),
];

const HJ_10: readonly string[] = [
  ...pairs('A', '5'),
  ...suited('A', 'K', '7'),
  ...suited('K', 'Q', 'J'),
  ...offsuit('A', 'K', 'T'),
  ...offsuit('K', 'Q', 'Q'),
];

const HJ_15: readonly string[] = [
  ...pairs('A', '7'),
  ...suited('A', 'K', 'T'),
  ...suited('K', 'Q', 'Q'),
  ...offsuit('A', 'K', 'J'),
];

const HJ_20: readonly string[] = [
  ...pairs('A', '9'),
  ...suited('A', 'K', 'J'),
  ...offsuit('A', 'K', 'Q'),
  ...offsuit('K', 'Q', 'Q'),
];

// LJ ranges (tightest)
const LJ_3: readonly string[] = [
  ...pairs('A', '2'),
  ...suited('A', 'K', '3'),
  ...suited('K', 'Q', '7'),
  ...suited('Q', 'J', '9'),
  ...offsuit('A', 'K', '5'),
  ...offsuit('K', 'Q', '9'),
  ...offsuit('Q', 'J', 'T'),
];

const LJ_5: readonly string[] = [
  ...pairs('A', '3'),
  ...suited('A', 'K', '5'),
  ...suited('K', 'Q', 'T'),
  ...suited('Q', 'J', 'J'),
  ...offsuit('A', 'K', '8'),
  ...offsuit('K', 'Q', 'J'),
];

const LJ_7: readonly string[] = [
  ...pairs('A', '5'),
  ...suited('A', 'K', '7'),
  ...suited('K', 'Q', 'J'),
  ...offsuit('A', 'K', 'T'),
  ...offsuit('K', 'Q', 'Q'),
];

const LJ_10: readonly string[] = [
  ...pairs('A', '7'),
  ...suited('A', 'K', 'T'),
  ...suited('K', 'Q', 'Q'),
  ...offsuit('A', 'K', 'J'),
];

const LJ_15: readonly string[] = [
  ...pairs('A', '9'),
  ...suited('A', 'K', 'J'),
  ...offsuit('A', 'K', 'Q'),
];

const LJ_20: readonly string[] = [
  ...pairs('A', 'T'),
  ...suited('A', 'K', 'Q'),
  ...offsuit('A', 'K', 'K'),
];

type RangeKey = `${Position}_${number}`;

const RAW: Record<RangeKey, readonly string[]> = {
  SB_3: SB_3,
  SB_4: SB_3,
  SB_5: SB_5,
  SB_6: SB_5,
  SB_7: SB_7,
  SB_8: SB_7,
  SB_9: SB_7,
  SB_10: SB_10,
  SB_12: SB_10,
  SB_15: SB_15,
  SB_18: SB_15,
  SB_20: SB_20,
  BTN_3: BTN_3,
  BTN_4: BTN_3,
  BTN_5: BTN_5,
  BTN_6: BTN_5,
  BTN_7: BTN_7,
  BTN_8: BTN_7,
  BTN_9: BTN_7,
  BTN_10: BTN_10,
  BTN_12: BTN_10,
  BTN_15: BTN_15,
  BTN_18: BTN_15,
  BTN_20: BTN_20,
  CO_3: CO_3,
  CO_4: CO_3,
  CO_5: CO_5,
  CO_6: CO_5,
  CO_7: CO_7,
  CO_8: CO_7,
  CO_9: CO_7,
  CO_10: CO_10,
  CO_12: CO_10,
  CO_15: CO_15,
  CO_18: CO_15,
  CO_20: CO_20,
  HJ_3: HJ_3,
  HJ_4: HJ_3,
  HJ_5: HJ_5,
  HJ_6: HJ_5,
  HJ_7: HJ_7,
  HJ_8: HJ_7,
  HJ_9: HJ_7,
  HJ_10: HJ_10,
  HJ_12: HJ_10,
  HJ_15: HJ_15,
  HJ_18: HJ_15,
  HJ_20: HJ_20,
  LJ_3: LJ_3,
  LJ_4: LJ_3,
  LJ_5: LJ_5,
  LJ_6: LJ_5,
  LJ_7: LJ_7,
  LJ_8: LJ_7,
  LJ_9: LJ_7,
  LJ_10: LJ_10,
  LJ_12: LJ_10,
  LJ_15: LJ_15,
  LJ_18: LJ_15,
  LJ_20: LJ_20,
};

const CACHE = new Map<RangeKey, Set<number>>();

export function getPushRange(position: Position, bb: number): Set<number> {
  const key: RangeKey = `${position}_${bb}`;
  let cached = CACHE.get(key);
  if (!cached) {
    const raw = RAW[key];
    cached = raw ? toSet(raw) : new Set<number>();
    CACHE.set(key, cached);
  }
  return cached;
}
