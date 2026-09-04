import { Component, inject } from '@angular/core';
import {
  FormBuilder,
  FormControl,
  FormGroup,
  ReactiveFormsModule,
  Validators,
} from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';

import { PaymentMethod, UpsertPaymentMethodRequest } from '../../core/models/payment-method';

export interface PaymentMethodDialogData {
  method: PaymentMethod | null;
}

interface PaymentMethodForm {
  title: FormControl<string>;
  link: FormControl<string>;
  description: FormControl<string>;
}

@Component({
  selector: 'app-payment-method-dialog',
  imports: [
    ReactiveFormsModule,
    MatDialogModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
  ],
  template: `
    <h2 mat-dialog-title>{{ data.method ? 'Edit payment method' : 'Add payment method' }}</h2>
    <form [formGroup]="form" (ngSubmit)="submit()">
      <mat-dialog-content>
        <mat-form-field appearance="outline" class="full-width">
          <mat-label>Title</mat-label>
          <input matInput formControlName="title" placeholder="Wise, PayPal, Bank transfer…" />
          @if (form.controls.title.hasError('required')) {
            <mat-error>Title is required</mat-error>
          }
        </mat-form-field>

        <mat-form-field appearance="outline" class="full-width">
          <mat-label>Payment link (optional)</mat-label>
          <input matInput formControlName="link" placeholder="https://…" />
        </mat-form-field>

        <mat-form-field appearance="outline" class="full-width">
          <mat-label>Description (optional)</mat-label>
          <textarea
            matInput
            formControlName="description"
            rows="3"
            placeholder="e.g. ask for IBAN"
          ></textarea>
        </mat-form-field>
      </mat-dialog-content>
      <mat-dialog-actions align="end">
        <button mat-button type="button" (click)="dialogRef.close(null)">Cancel</button>
        <button mat-flat-button color="primary" type="submit" [disabled]="form.invalid">
          Save
        </button>
      </mat-dialog-actions>
    </form>
  `,
  styles: `
    .full-width {
      width: 100%;
    }
  `,
})
export class PaymentMethodDialog {
  readonly dialogRef = inject(MatDialogRef<PaymentMethodDialog, UpsertPaymentMethodRequest | null>);
  readonly data = inject<PaymentMethodDialogData>(MAT_DIALOG_DATA);
  readonly form: FormGroup<PaymentMethodForm>;

  constructor() {
    const method = this.data.method;
    this.form = inject(FormBuilder).nonNullable.group({
      title: [method?.title ?? '', [Validators.required, Validators.maxLength(100)]],
      link: [method?.link ?? ''],
      description: [method?.description ?? ''],
    });
  }

  submit(): void {
    if (this.form.invalid) {
      return;
    }
    const raw = this.form.getRawValue();
    this.dialogRef.close({
      title: raw.title.trim(),
      icon: null,
      link: raw.link.trim() || null,
      description: raw.description.trim() || null,
    });
  }
}
