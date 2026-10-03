import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { HttpErrorResponse } from '@angular/common/http';
import { Observable, filter, forkJoin, switchMap } from 'rxjs';

import { ExpenseApi } from '../../core/api/expense.api';
import { CurrentUserService } from '../../core/api/current-user.service';
import { CurrentEventService } from '../../core/event/current-event.service';
import {
  Balances,
  Expense,
  ExpenseList,
  ExpenseMember,
  ExpensePayment,
  RecordPaymentRequest,
  Transfer,
  UpsertExpenseRequest,
} from '../../core/models/expense';
import { ConfirmDialog, ConfirmDialogData } from '../../shared/confirm-dialog/confirm-dialog';
import {
  PaymentMethodsViewerDialog,
  PaymentMethodsViewerDialogData,
} from '../../shared/payment-methods-viewer-dialog/payment-methods-viewer-dialog';
import { APP_LOCALE } from '../../shared/app-locale';
import { formatMoney } from '../../shared/money';
import { fullTimestamp } from '../../shared/timestamp';
import { ExpenseDialog, ExpenseDialogData } from './expense-dialog';
import { PaymentDialog, PaymentDialogData } from './payment-dialog';

/** "Fri 3 Jul 2030" for a "yyyy-MM-dd" date. */
function dateLabel(iso: string): string {
  return new Date(`${iso}T00:00:00`).toLocaleDateString(APP_LOCALE, {
    weekday: 'short',
    day: 'numeric',
    month: 'short',
    year: 'numeric',
  });
}

@Component({
  selector: 'app-expenses-page',
  imports: [MatButtonModule, MatCardModule, MatIconModule, MatMenuModule, MatTooltipModule],
  templateUrl: './expenses-page.html',
  styleUrl: './expenses-page.scss',
})
export class ExpensesPage implements OnInit {
  private readonly api = inject(ExpenseApi);
  private readonly currentUser = inject(CurrentUserService);
  private readonly currentEventService = inject(CurrentEventService);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);

  readonly event = this.currentEventService.event;
  readonly isOrganiser = this.currentEventService.isOrganiser;
  readonly list = signal<ExpenseList | null>(null);
  readonly balances = signal<Balances | null>(null);
  readonly loading = signal(true);
  readonly error = signal(false);

  readonly myId = computed(() => this.currentUser.user()?.id ?? null);
  readonly currency = computed(() => this.list()?.currency ?? this.event()?.currency ?? 'EUR');
  readonly canEdit = computed(() => this.list()?.canEdit ?? false);

  private readonly mine = computed(() =>
    this.balances()?.balances.find((b) => b.userId === this.myId()),
  );
  readonly myBalance = computed(() => this.mine()?.balance ?? 0);
  /** The part of my balance from my own unassigned expenses. */
  readonly myUnassigned = computed(() => this.mine()?.unassigned ?? 0);
  readonly unassignedTotal = computed(() => this.balances()?.unassigned ?? 0);
  /** Expenses under a heading per date, newest first (the server already sorts them). */
  readonly expenseGroups = computed(() => {
    const groups: { date: string; label: string; expenses: Expense[] }[] = [];
    for (const expense of this.list()?.expenses ?? []) {
      let group = groups.at(-1);
      if (group?.date !== expense.date) {
        group = { date: expense.date, label: dateLabel(expense.date), expenses: [] };
        groups.push(group);
      }
      group.expenses.push(expense);
    }
    return groups;
  });

  readonly unassignedCount = computed(
    () => this.list()?.expenses.filter((x) => x.unassigned > 0).length ?? 0,
  );

  /** Mine first, so what the viewer has to do is at the top. */
  readonly transfers = computed(() => {
    const me = this.myId();
    const all = this.balances()?.transfers ?? [];
    const involvesMe = (t: Transfer) => t.fromUserId === me || t.toUserId === me;
    return [...all.filter(involvesMe), ...all.filter((t) => !involvesMe(t))];
  });

  private readonly members = computed(
    () => new Map((this.list()?.members ?? []).map((m) => [m.userId, m])),
  );

  ngOnInit(): void {
    this.load();
  }

  money(amount: number): string {
    return formatMoney(amount, this.currency());
  }

  /** "you" for the viewer, otherwise their name. */
  who(userId: number, capitalise = false): string {
    if (userId === this.myId()) {
      return capitalise ? 'You' : 'you';
    }
    return this.members().get(userId)?.displayName ?? 'Someone';
  }

  member(userId: number): ExpenseMember | undefined {
    return this.members().get(userId);
  }

  /** First letter of the name, for people without a picture. */
  initial(userId: number): string {
    return [...(this.member(userId)?.displayName.trim() ?? '')][0]?.toUpperCase() ?? '?';
  }

  myShare(expense: Expense): number | null {
    return expense.shares.find((s) => s.userId === this.myId())?.amount ?? null;
  }

  shareLines(expense: Expense): string[] {
    const lines = expense.shares.map((s) => `${this.who(s.userId, true)}: ${this.money(s.amount)}`);
    return expense.unassigned > 0
      ? [...lines, `Unassigned: ${this.money(expense.unassigned)}`]
      : lines;
  }

  timestamp(iso: string): string {
    return fullTimestamp(iso);
  }

  involvesMe(t: Transfer): boolean {
    return t.fromUserId === this.myId() || t.toUserId === this.myId();
  }

  canRecord(t: Transfer): boolean {
    return this.canEdit() && (this.isOrganiser() || this.involvesMe(t));
  }

  addExpense(): void {
    this.openExpenseDialog(null)
      .pipe(switchMap((request) => this.api.create(this.eventId(), request)))
      .subscribe({
        next: () => this.done('Expense added'),
        error: (err) => this.fail(err, 'Could not add the expense'),
      });
  }

  editExpense(expense: Expense): void {
    this.openExpenseDialog(expense)
      .pipe(switchMap((request) => this.api.update(this.eventId(), expense.id, request)))
      .subscribe({
        next: () => this.done('Expense updated'),
        error: (err) => this.fail(err, 'Could not update the expense'),
      });
  }

  deleteExpense(expense: Expense): void {
    this.confirm({
      title: 'Delete expense',
      message: `Delete “${expense.title}” (${this.money(expense.amount)})? Everyone's balances will be recalculated.`,
      confirmLabel: 'Delete',
    })
      .pipe(switchMap(() => this.api.delete(this.eventId(), expense.id)))
      .subscribe({
        next: () => this.done('Expense deleted'),
        error: (err) => this.fail(err, 'Could not delete the expense'),
      });
  }

  /** Opens the repayment dialog, prefilled from a suggested transfer if given. */
  recordPayment(transfer: Transfer | null = null): void {
    const list = this.list();
    const me = this.myId();
    if (!list || me === null) {
      return;
    }
    this.dialog
      .open<PaymentDialog, PaymentDialogData, RecordPaymentRequest | null>(PaymentDialog, {
        data: {
          members: list.members,
          currency: list.currency,
          myId: me,
          isOrganiser: this.isOrganiser(),
          fromUserId: transfer?.fromUserId ?? null,
          toUserId: transfer?.toUserId ?? null,
          amount: transfer?.amount ?? null,
        },
      })
      .afterClosed()
      .pipe(
        filter((request): request is RecordPaymentRequest => !!request),
        switchMap((request) => this.api.recordPayment(this.eventId(), request)),
      )
      .subscribe({
        next: () => this.done('Repayment recorded'),
        error: (err) => this.fail(err, 'Could not record the repayment'),
      });
  }

  deletePayment(payment: ExpensePayment): void {
    this.confirm({
      title: 'Remove repayment',
      message: `Remove the repayment of ${this.money(payment.amount)} from ${this.who(payment.fromUserId)} to ${this.who(payment.toUserId)}?`,
      confirmLabel: 'Remove',
    })
      .pipe(switchMap(() => this.api.deletePayment(this.eventId(), payment.id)))
      .subscribe({
        next: () => this.done('Repayment removed'),
        error: (err) => this.fail(err, 'Could not remove the repayment'),
      });
  }

  viewPaymentMethods(userId: number): void {
    this.dialog.open<PaymentMethodsViewerDialog, PaymentMethodsViewerDialogData>(
      PaymentMethodsViewerDialog,
      { data: { userId, displayName: this.who(userId, true) } },
    );
  }

  private openExpenseDialog(expense: Expense | null): Observable<UpsertExpenseRequest> {
    const list = this.list();
    const me = this.myId();
    if (!list || me === null) {
      return new Observable<never>((s) => s.complete());
    }
    // Former members only show up when they're already on this expense.
    const onExpense = new Set([
      ...(expense ? [expense.paidById] : []),
      ...(expense?.shares.map((s) => s.userId) ?? []),
    ]);
    const members = list.members.filter((m) => m.isConfirmed || onExpense.has(m.userId));

    return this.dialog
      .open<ExpenseDialog, ExpenseDialogData, UpsertExpenseRequest | null>(ExpenseDialog, {
        data: { expense, members, currency: list.currency, myId: me },
        width: '560px',
        maxWidth: '100vw',
      })
      .afterClosed()
      .pipe(filter((request): request is UpsertExpenseRequest => !!request));
  }

  private confirm(data: ConfirmDialogData): Observable<true> {
    return this.dialog
      .open<ConfirmDialog, ConfirmDialogData, boolean>(ConfirmDialog, { data })
      .afterClosed()
      .pipe(filter((ok): ok is true => ok === true));
  }

  private eventId(): string {
    return this.event()!.id;
  }

  private done(message: string): void {
    this.snackBar.open(message, undefined, { duration: 3000 });
    this.load();
  }

  private fail(err: HttpErrorResponse, fallback: string): void {
    this.snackBar.open(err.error?.error ?? fallback, 'Dismiss', { duration: 5000 });
  }

  private load(): void {
    const ev = this.event();
    if (!ev) {
      return;
    }
    this.error.set(false);
    forkJoin({ list: this.api.list(ev.id), balances: this.api.balances(ev.id) }).subscribe({
      next: ({ list, balances }) => {
        this.list.set(list);
        this.balances.set(balances);
        this.loading.set(false);
      },
      error: () => {
        this.error.set(true);
        this.loading.set(false);
      },
    });
  }
}
