import { Component, OnInit, inject, signal } from '@angular/core';
import {
  FormBuilder,
  FormControl,
  FormGroup,
  ReactiveFormsModule,
  Validators,
} from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatListModule } from '@angular/material/list';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatDialog } from '@angular/material/dialog';

import { ConfirmDialog, ConfirmDialogData } from '../../shared/confirm-dialog/confirm-dialog';
import { PaymentMethodDialog, PaymentMethodDialogData } from './payment-method-dialog';

import { UserApi } from '../../core/api/user.api';
import { PaymentMethodApi } from '../../core/api/payment-method.api';
import { User } from '../../core/models/user';
import { PaymentMethod } from '../../core/models/payment-method';

@Component({
  selector: 'app-profile',
  imports: [
    ReactiveFormsModule,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatIconModule,
    MatListModule,
  ],
  templateUrl: './profile.html',
  styleUrl: './profile.scss',
})
export class Profile implements OnInit {
  private readonly userApi = inject(UserApi);
  private readonly paymentMethodApi = inject(PaymentMethodApi);
  private readonly snackBar = inject(MatSnackBar);
  private readonly dialog = inject(MatDialog);

  readonly user = signal<User | null>(null);
  readonly saving = signal(false);
  readonly form: FormGroup<{ displayName: FormControl<string> }>;
  readonly paymentMethods = signal<PaymentMethod[]>([]);
  readonly paymentMethodsLoading = signal(true);

  constructor() {
    this.form = inject(FormBuilder).nonNullable.group({
      displayName: ['', [Validators.required, Validators.maxLength(100)]],
    });
  }

  ngOnInit(): void {
    this.userApi.getMe().subscribe((user) => {
      this.user.set(user);
      this.form.setValue({ displayName: user.displayName });
    });
    this.loadPaymentMethods();
  }

  private loadPaymentMethods(): void {
    this.paymentMethodsLoading.set(true);
    this.paymentMethodApi.getMine().subscribe({
      next: (methods) => {
        this.paymentMethods.set(methods);
        this.paymentMethodsLoading.set(false);
      },
      error: () => this.paymentMethodsLoading.set(false),
    });
  }

  addPaymentMethod(): void {
    this.openPaymentMethodDialog(null).subscribe((request) => {
      if (!request) {
        return;
      }
      this.paymentMethodApi.create(request).subscribe({
        next: () => {
          this.loadPaymentMethods();
          this.snackBar.open('Payment method added', 'Dismiss', { duration: 3000 });
        },
        error: () =>
          this.snackBar.open('Could not add payment method', 'Dismiss', { duration: 3000 }),
      });
    });
  }

  editPaymentMethod(method: PaymentMethod): void {
    this.openPaymentMethodDialog(method).subscribe((request) => {
      if (!request) {
        return;
      }
      this.paymentMethodApi.update(method.id, request).subscribe({
        next: () => {
          this.loadPaymentMethods();
          this.snackBar.open('Payment method updated', 'Dismiss', { duration: 3000 });
        },
        error: () =>
          this.snackBar.open('Could not update payment method', 'Dismiss', { duration: 3000 }),
      });
    });
  }

  deletePaymentMethod(method: PaymentMethod): void {
    this.confirmAction({
      title: 'Remove payment method',
      message: `Remove “${method.title}” from your payment methods?`,
      confirmLabel: 'Remove',
    }).subscribe((ok) => {
      if (!ok) {
        return;
      }
      this.paymentMethodApi.delete(method.id).subscribe({
        next: () => {
          this.loadPaymentMethods();
          this.snackBar.open('Payment method removed', 'Dismiss', { duration: 3000 });
        },
        error: () =>
          this.snackBar.open('Could not remove payment method', 'Dismiss', { duration: 3000 }),
      });
    });
  }

  private openPaymentMethodDialog(method: PaymentMethod | null) {
    return this.dialog
      .open<PaymentMethodDialog, PaymentMethodDialogData>(PaymentMethodDialog, {
        data: { method },
      })
      .afterClosed();
  }

  private confirmAction(data: ConfirmDialogData) {
    return this.dialog
      .open<ConfirmDialog, ConfirmDialogData, boolean>(ConfirmDialog, { data })
      .afterClosed();
  }

  save(): void {
    const currentUser = this.user();
    if (!currentUser || this.form.invalid) {
      return;
    }

    this.saving.set(true);
    this.userApi
      .updateMe({
        displayName: this.form.getRawValue().displayName.trim(),
        avatarUrl: currentUser.avatarUrl,
      })
      .subscribe({
        next: (updated) => {
          this.user.set(updated);
          this.saving.set(false);
          this.snackBar.open('Profile updated', 'Dismiss', { duration: 3000 });
        },
        error: () => {
          this.saving.set(false);
          this.snackBar.open('Could not update profile', 'Dismiss', { duration: 3000 });
        },
      });
  }
}
