export type ExpenseSplitMode = 'Equal' | 'Percentage' | 'Exact';

/** Someone who can appear in expenses; former members stay listed for their history. */
export interface ExpenseMember {
  userId: number;
  displayName: string;
  avatarUrl: string | null;
  isConfirmed: boolean;
}

export interface ExpenseShare {
  userId: number;
  amount: number;
  /** The percentage entered for a percentage split. */
  percentage: number | null;
}

export interface Expense {
  id: number;
  title: string;
  /** "yyyy-MM-dd": when the money was spent. The list is sorted by it, newest first. */
  date: string;
  amount: number;
  paidById: number;
  splitMode: ExpenseSplitMode;
  shares: ExpenseShare[];
  createdById: number;
  createdAt: string;
  updatedAt: string | null;
  canEdit: boolean;
  /** Part of the amount not split with anyone yet; still owed to the payer. */
  unassigned: number;
}

export interface ExpensePayment {
  id: number;
  fromUserId: number;
  toUserId: number;
  amount: number;
  note: string | null;
  recordedById: number;
  createdAt: string;
  canDelete: boolean;
}

export interface ExpenseList {
  currency: string;
  /** False once the event is read-only. */
  canEdit: boolean;
  members: ExpenseMember[];
  expenses: Expense[];
  payments: ExpensePayment[];
}

export interface Balance {
  userId: number;
  /** Positive: owed money. Negative: owes money. */
  balance: number;
  /** The part of `balance` from unassigned expenses, which nobody owes yet. */
  unassigned: number;
}

export interface Transfer {
  fromUserId: number;
  toUserId: number;
  amount: number;
}

export interface Balances {
  currency: string;
  balances: Balance[];
  /** Settle the assigned money only. */
  transfers: Transfer[];
  /** Total not assigned to anyone, across all expenses. */
  unassigned: number;
}

export interface ExpenseShareRequest {
  userId: number;
  /** The percentage or exact amount, depending on the split mode; null for equal splits. */
  value: number | null;
}

export interface UpsertExpenseRequest {
  title: string;
  date: string;
  amount: number;
  paidById: number;
  splitMode: ExpenseSplitMode;
  shares: ExpenseShareRequest[];
}

export interface RecordPaymentRequest {
  fromUserId: number;
  toUserId: number;
  amount: number;
  note: string | null;
}
