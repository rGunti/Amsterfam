import { Component, inject } from '@angular/core';
import {
  AbstractControl,
  FormBuilder,
  FormControl,
  FormGroup,
  ReactiveFormsModule,
  ValidationErrors,
  Validators,
} from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';

import { ExpenseMember, RecordPaymentRequest } from '../../core/models/expense';

export interface PaymentDialogData {
  members: ExpenseMember[];
  currency: string;
  myId: number;
  /** Organisers may record repayments between anyone; others only their own. */
  isOrganiser: boolean;
  /** Prefilled from a suggested transfer. */
  fromUserId: number | null;
  toUserId: number | null;
  amount: number | null;
}

interface PaymentForm {
  fromUserId: FormControl<number | null>;
  toUserId: FormControl<number | null>;
  amount: FormControl<number | null>;
  note: FormControl<string>;
}

@Component({
  selector: 'app-payment-dialog',
  imports: [
    ReactiveFormsModule,
    MatDialogModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
  ],
  template: `
    <h2 mat-dialog-title>Record a repayment</h2>
    <form [formGroup]="form" (ngSubmit)="submit()">
      <mat-dialog-content>
        <div class="row">
          <mat-form-field appearance="outline">
            <mat-label>Paid by</mat-label>
            <mat-select formControlName="fromUserId">
              @for (m of data.members; track m.userId) {
                <mat-option [value]="m.userId">{{ name(m) }}</mat-option>
              }
            </mat-select>
          </mat-form-field>
          <mat-form-field appearance="outline">
            <mat-label>Paid to</mat-label>
            <mat-select formControlName="toUserId">
              @for (m of data.members; track m.userId) {
                <mat-option [value]="m.userId">{{ name(m) }}</mat-option>
              }
            </mat-select>
          </mat-form-field>
        </div>
        <mat-form-field appearance="outline" class="full-width">
          <mat-label>Amount ({{ data.currency }})</mat-label>
          <input
            matInput
            type="number"
            inputmode="decimal"
            min="0.01"
            step="0.01"
            formControlName="amount"
          />
        </mat-form-field>
        <mat-form-field appearance="outline" class="full-width">
          <mat-label>Note (optional)</mat-label>
          <input matInput formControlName="note" maxlength="200" placeholder="e.g. via Tikkie" />
        </mat-form-field>
        @if (form.hasError('samePerson')) {
          <p class="hint">Choose two different people.</p>
        } @else if (form.hasError('notInvolved')) {
          <p class="hint">You can only record repayments you made or received.</p>
        }
      </mat-dialog-content>
      <mat-dialog-actions align="end">
        <button mat-button type="button" (click)="dialogRef.close(null)">Cancel</button>
        <button mat-flat-button color="primary" type="submit" [disabled]="form.invalid">
          Record
        </button>
      </mat-dialog-actions>
    </form>
  `,
  styles: `
    .full-width {
      width: 100%;
    }
    .row {
      display: flex;
      flex-wrap: wrap;
      gap: 0 12px;

      mat-form-field {
        flex: 1 1 160px;
      }
    }
    .hint {
      margin: 0;
      color: var(--mat-sys-error);
    }
  `,
})
export class PaymentDialog {
  readonly dialogRef = inject(MatDialogRef<PaymentDialog, RecordPaymentRequest | null>);
  readonly data = inject<PaymentDialogData>(MAT_DIALOG_DATA);

  readonly form: FormGroup<PaymentForm> = inject(FormBuilder).nonNullable.group(
    {
      fromUserId: new FormControl<number | null>(
        this.data.fromUserId ?? this.data.myId,
        Validators.required,
      ),
      toUserId: new FormControl<number | null>(this.data.toUserId, Validators.required),
      amount: new FormControl<number | null>(this.data.amount, [
        Validators.required,
        Validators.min(0.01),
      ]),
      note: new FormControl('', { nonNullable: true }),
    },
    { validators: (group) => this.validatePeople(group) },
  );

  name(m: ExpenseMember): string {
    return m.userId === this.data.myId ? `${m.displayName} (you)` : m.displayName;
  }

  private validatePeople(group: AbstractControl): ValidationErrors | null {
    const { fromUserId, toUserId } = group.value as {
      fromUserId: number | null;
      toUserId: number | null;
    };
    if (fromUserId === null || toUserId === null) {
      return null;
    }
    if (fromUserId === toUserId) {
      return { samePerson: true };
    }
    if (!this.data.isOrganiser && fromUserId !== this.data.myId && toUserId !== this.data.myId) {
      return { notInvolved: true };
    }
    return null;
  }

  submit(): void {
    if (this.form.invalid) {
      return;
    }
    const raw = this.form.getRawValue();
    this.dialogRef.close({
      fromUserId: raw.fromUserId!,
      toUserId: raw.toUserId!,
      amount: raw.amount!,
      note: raw.note.trim() || null,
    });
  }
}
