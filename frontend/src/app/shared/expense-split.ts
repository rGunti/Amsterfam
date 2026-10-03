import { ExpenseSplitMode } from '../core/models/expense';

export interface SplitInput {
  userId: number;
  /** Percentage or exact amount, depending on the mode; ignored for equal splits. */
  value: number | null;
}

export interface SplitPreview {
  /** Resolved amount per user id; empty while the split is incomplete. */
  shares: Map<number, number>;
  /** What's left to assign (exact: money, percentage: percent); 0 when it adds up. */
  remaining: number;
  error: string | null;
}

const toCents = (value: number) => Math.round(value * 100);

/**
 * Mirrors ExpenseSplitter on the backend so the form can preview shares as you type. The
 * server re-checks everything; this only has to agree with it for valid input.
 */
export function previewSplit(
  total: number | null,
  mode: ExpenseSplitMode,
  participants: SplitInput[],
): SplitPreview {
  const fail = (error: string, remaining = 0): SplitPreview => ({
    shares: new Map(),
    remaining,
    error,
  });
  if (total === null || !(total > 0)) {
    return fail('Enter an amount.');
  }
  if (participants.length === 0) {
    return fail('Choose at least one participant.');
  }
  const cents = toCents(total);

  if (mode === 'Exact') {
    const assigned = participants.reduce((sum, p) => sum + toCents(p.value ?? 0), 0);
    const remaining = (cents - assigned) / 100;
    if (participants.some((p) => !(p.value !== null && p.value > 0))) {
      return fail('Enter an amount for everyone.', remaining);
    }
    if (assigned !== cents) {
      return fail('The amounts must add up to the total.', remaining);
    }
    return {
      shares: new Map(participants.map((p) => [p.userId, p.value!])),
      remaining: 0,
      error: null,
    };
  }

  let weights: number[];
  if (mode === 'Percentage') {
    const assigned = participants.reduce((sum, p) => sum + toCents(p.value ?? 0), 0);
    const remaining = (10000 - assigned) / 100;
    if (participants.some((p) => !(p.value !== null && p.value > 0))) {
      return fail('Enter a percentage for everyone.', remaining);
    }
    if (assigned !== 10000) {
      return fail('The percentages must add up to 100%.', remaining);
    }
    weights = participants.map((p) => toCents(p.value!));
  } else {
    weights = participants.map(() => 1);
  }

  // Round down, then hand leftover cents to the biggest rounding losses (ties: lowest id).
  const weightSum = weights.reduce((a, b) => a + b, 0);
  const raw = participants.map((p, i) => {
    const exact = (cents * weights[i]) / weightSum;
    const floor = Math.floor(exact + 1e-9);
    return { userId: p.userId, cents: floor, lost: exact - floor };
  });
  let leftover = cents - raw.reduce((sum, r) => sum + r.cents, 0);
  const order = [...raw].sort((a, b) => b.lost - a.lost || a.userId - b.userId);
  for (const r of order) {
    if (leftover-- <= 0) {
      break;
    }
    r.cents += 1;
  }

  return {
    shares: new Map(raw.map((r) => [r.userId, r.cents / 100])),
    remaining: 0,
    error: null,
  };
}
