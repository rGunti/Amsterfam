import { ExpenseSplitMode } from '../core/models/expense';

export interface SplitInput {
  userId: number;
  /** Percentage or exact amount, depending on the mode; ignored for equal splits. */
  value: number | null;
}

export interface SplitPreview {
  /** Resolved amount per user id; empty while the split is invalid. */
  shares: Map<number, number>;
  /** Money not assigned to anyone; saved as such, to be assigned later. */
  unassigned: number;
  error: string | null;
}

const toCents = (value: number) => Math.round(value * 100);

/**
 * Mirrors ExpenseSplitter on the backend so the form can preview shares as you type. The
 * server re-checks everything; this only has to agree with it for valid input.
 *
 * Equal splits always cover the whole amount. Percentage and exact splits may cover less,
 * leaving the rest unassigned, but never more.
 */
export function previewSplit(
  total: number | null,
  mode: ExpenseSplitMode,
  participants: SplitInput[],
): SplitPreview {
  const fail = (error: string): SplitPreview => ({ shares: new Map(), unassigned: 0, error });
  if (total === null || !(total > 0)) {
    return fail('Enter an amount.');
  }
  const cents = toCents(total);
  const result = (shareCents: Map<number, number>): SplitPreview => {
    const assigned = [...shareCents.values()].reduce((a, b) => a + b, 0);
    return {
      shares: new Map([...shareCents].map(([id, c]) => [id, c / 100])),
      unassigned: (cents - assigned) / 100,
      error: null,
    };
  };

  if (mode === 'Equal') {
    if (participants.length === 0) {
      return fail('Choose at least one participant.');
    }
    return result(distribute(cents, participants, () => 1));
  }

  if (participants.some((p) => !(p.value !== null && p.value > 0))) {
    return fail(
      mode === 'Exact' ? 'Enter an amount for everyone.' : 'Enter a percentage for everyone.',
    );
  }

  if (mode === 'Exact') {
    const assigned = participants.reduce((sum, p) => sum + toCents(p.value!), 0);
    if (assigned > cents) {
      return fail("The amounts can't add up to more than the total.");
    }
    return result(new Map(participants.map((p) => [p.userId, toCents(p.value!)])));
  }

  const percentCents = participants.reduce((sum, p) => sum + toCents(p.value!), 0);
  if (percentCents > 10000) {
    return fail("The percentages can't add up to more than 100%.");
  }
  // Below 100% only that part of the total is shared out; the rest stays unassigned.
  const assignedCents = Math.floor((cents * percentCents) / 10000 + 1e-9);
  return result(distribute(assignedCents, participants, (p) => toCents(p.value!)));
}

/** Splits `cents` in proportion to the weights; leftover cents go to the biggest rounding losses (ties: lowest id). */
function distribute(
  cents: number,
  participants: SplitInput[],
  weight: (p: SplitInput) => number,
): Map<number, number> {
  if (participants.length === 0) {
    return new Map();
  }
  const weights = participants.map(weight);
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
  return new Map(raw.map((r) => [r.userId, r.cents]));
}
