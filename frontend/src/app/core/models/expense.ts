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
  amount: number;
  paidById: number;
  splitMode: ExpenseSplitMode;
  shares: ExpenseShare[];
  createdById: number;
  createdAt: string;
  updatedAt: string | null;
  canEdit: boolean;
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
}

export interface Transfer {
  fromUserId: number;
  toUserId: number;
  amount: number;
}

export interface Balances {
  currency: string;
  balances: Balance[];
  transfers: Transfer[];
}

export interface ExpenseShareRequest {
  userId: number;
  /** The percentage or exact amount, depending on the split mode; null for equal splits. */
  value: number | null;
}

export interface UpsertExpenseRequest {
  title: string;
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
