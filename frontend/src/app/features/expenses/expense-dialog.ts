import { Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import {
  FormArray,
  FormBuilder,
  FormControl,
  FormGroup,
  ReactiveFormsModule,
  Validators,
} from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { map, startWith } from 'rxjs';

import {
  Expense,
  ExpenseMember,
  ExpenseSplitMode,
  UpsertExpenseRequest,
} from '../../core/models/expense';
import { previewSplit } from '../../shared/expense-split';
import { localIsoDate } from '../../shared/local-date';
import { formatMoney } from '../../shared/money';

export interface ExpenseDialogData {
  expense: Expense | null;
  /** People who can be picked: confirmed members, plus anyone already on the expense. */
  members: ExpenseMember[];
  currency: string;
  myId: number;
  /** Opens as a read-only view of `expense`; Edit unlocks the form if `canEdit`. */
  readOnly?: boolean;
  canEdit?: boolean;
}

interface ParticipantForm {
  userId: FormControl<number>;
  included: FormControl<boolean>;
  value: FormControl<number | null>;
}

interface ExpenseForm {
  title: FormControl<string>;
  date: FormControl<string>;
  amount: FormControl<number | null>;
  paidById: FormControl<number>;
  splitMode: FormControl<ExpenseSplitMode>;
  participants: FormArray<FormGroup<ParticipantForm>>;
}

@Component({
  selector: 'app-expense-dialog',
  imports: [
    ReactiveFormsModule,
    MatDialogModule,
    MatButtonModule,
    MatButtonToggleModule,
    MatCheckboxModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatSelectModule,
  ],
  templateUrl: './expense-dialog.html',
  styleUrl: './expense-dialog.scss',
})
export class ExpenseDialog {
  readonly dialogRef = inject(MatDialogRef<ExpenseDialog, UpsertExpenseRequest | null>);
  readonly data = inject<ExpenseDialogData>(MAT_DIALOG_DATA);
  private readonly fb = inject(FormBuilder).nonNullable;
  readonly form: FormGroup<ExpenseForm> = this.buildForm();
  readonly readOnly = signal(this.data.readOnly ?? false);

  // The raw value, because `form.value` leaves out disabled controls (all of them when read-only).
  private readonly value = toSignal(
    this.form.valueChanges.pipe(
      map(() => this.form.getRawValue()),
      startWith(this.form.getRawValue()),
    ),
    { initialValue: this.form.getRawValue() },
  );

  readonly preview = computed(() => {
    const raw = this.value();
    const included = raw.participants.filter((p) => p.included);
    return previewSplit(
      raw.amount,
      raw.splitMode,
      included.map((p) => ({ userId: p.userId, value: p.value })),
    );
  });

  readonly allIncluded = computed(() => this.value().participants.every((p) => p.included));
  readonly mode = computed(() => this.value().splitMode);

  constructor() {
    if (this.readOnly()) {
      this.form.disable({ emitEvent: false });
    }
    // Percentages and amounts mean different things, so values don't carry over between
    // modes (41.25 EUR would otherwise become 41.25%). Only fires on user changes.
    this.form.controls.splitMode.valueChanges.pipe(takeUntilDestroyed()).subscribe(() => {
      for (const group of this.form.controls.participants.controls) {
        group.controls.value.setValue(null);
      }
    });
  }

  private buildForm(): FormGroup<ExpenseForm> {
    const fb = this.fb;
    const expense = this.data.expense;
    const shareOf = (userId: number) => expense?.shares.find((s) => s.userId === userId);

    return fb.group({
      title: [expense?.title ?? '', [Validators.required, Validators.maxLength(100)]],
      date: [expense?.date ?? localIsoDate(), Validators.required],
      amount: fb.control<number | null>(expense?.amount ?? null, [
        Validators.required,
        Validators.min(0.01),
      ]),
      paidById: [expense?.paidById ?? this.data.myId],
      splitMode: fb.control<ExpenseSplitMode>(expense?.splitMode ?? 'Equal'),
      participants: fb.array(
        this.data.members.map((m) => {
          const share = shareOf(m.userId);
          return fb.group({
            userId: [m.userId],
            // A new expense starts with everyone; most are shared by the whole group.
            included: [expense ? share !== undefined : true],
            value: fb.control<number | null>(
              expense?.splitMode === 'Percentage'
                ? (share?.percentage ?? null)
                : expense?.splitMode === 'Exact'
                  ? (share?.amount ?? null)
                  : null,
            ),
          });
        }),
      ),
    });
  }

  memberName(userId: number): string {
    const member = this.data.members.find((m) => m.userId === userId);
    return userId === this.data.myId
      ? `${member?.displayName ?? '?'} (you)`
      : (member?.displayName ?? '?');
  }

  shareText(userId: number): string {
    const amount = this.preview().shares.get(userId);
    return amount === undefined ? '' : formatMoney(amount, this.data.currency);
  }

  unassignedText(): string {
    return formatMoney(this.preview().unassigned, this.data.currency);
  }

  /** Unlocks the read-only view. No events, so the split-mode reset above doesn't fire. */
  startEditing(): void {
    this.readOnly.set(false);
    this.form.enable({ emitEvent: false });
  }

  toggleAll(): void {
    const include = !this.allIncluded();
    for (const group of this.form.controls.participants.controls) {
      group.controls.included.setValue(include);
    }
  }

  submit(): void {
    if (this.form.invalid || this.preview().error) {
      return;
    }
    const raw = this.form.getRawValue();
    this.dialogRef.close({
      title: raw.title.trim(),
      date: raw.date,
      amount: raw.amount!,
      paidById: raw.paidById,
      splitMode: raw.splitMode,
      shares: raw.participants
        .filter((p) => p.included)
        .map((p) => ({ userId: p.userId, value: raw.splitMode === 'Equal' ? null : p.value })),
    });
  }
}
