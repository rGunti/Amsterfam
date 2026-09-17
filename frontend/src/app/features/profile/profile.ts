import {
  Component,
  DestroyRef,
  ElementRef,
  OnInit,
  effect,
  inject,
  signal,
  viewChild,
} from '@angular/core';
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
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatDialog } from '@angular/material/dialog';

import { ConfirmDialog, ConfirmDialogData } from '../../shared/confirm-dialog/confirm-dialog';
import { PaymentMethodDialog, PaymentMethodDialogData } from './payment-method-dialog';

import { AuthService } from '../../core/auth/auth.service';
import { UserApi } from '../../core/api/user.api';
import { CurrentUserService } from '../../core/api/current-user.service';
import { PaymentMethodApi } from '../../core/api/payment-method.api';
import { CurrentEventService } from '../../core/event/current-event.service';
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
    MatTooltipModule,
  ],
  templateUrl: './profile.html',
  styleUrl: './profile.scss',
})
export class Profile implements OnInit {
  private readonly authService = inject(AuthService);
  private readonly userApi = inject(UserApi);
  private readonly currentUserService = inject(CurrentUserService);
  private readonly paymentMethodApi = inject(PaymentMethodApi);
  private readonly currentEventService = inject(CurrentEventService);
  private readonly snackBar = inject(MatSnackBar);
  private readonly dialog = inject(MatDialog);
  private readonly destroyRef = inject(DestroyRef);

  readonly user = this.currentUserService.user;
  readonly saving = signal(false);
  readonly editingName = signal(false);
  readonly form: FormGroup<{ displayName: FormControl<string> }>;
  readonly paymentMethods = signal<PaymentMethod[]>([]);
  readonly paymentMethodsLoading = signal(true);

  readonly headerText = viewChild<ElementRef<HTMLElement>>('headerText');
  readonly avatarSize = signal(40);

  constructor() {
    this.form = inject(FormBuilder).nonNullable.group({
      displayName: ['', [Validators.maxLength(100)]],
    });

    let resizeObserver: ResizeObserver | undefined;
    effect(() => {
      resizeObserver?.disconnect();
      const el = this.headerText()?.nativeElement;
      if (!el) {
        return;
      }
      resizeObserver = new ResizeObserver(([entry]) => {
        if (entry) {
          this.avatarSize.set(entry.contentRect.height);
        }
      });
      resizeObserver.observe(el);
    });
    this.destroyRef.onDestroy(() => resizeObserver?.disconnect());

    effect(() => {
      const currentUser = this.user();
      if (currentUser && !this.editingName()) {
        this.form.setValue({ displayName: currentUser.displayName ?? '' });
      }
    });
  }

  ngOnInit(): void {
    this.loadPaymentMethods();
  }

  displayNameFor(user: User): string {
    return user.displayName ?? user.handle;
  }

  startEditingName(): void {
    const currentUser = this.user();
    if (!currentUser) {
      return;
    }
    this.form.setValue({ displayName: currentUser.displayName ?? '' });
    this.editingName.set(true);
  }

  cancelEditingName(): void {
    const currentUser = this.user();
    if (currentUser) {
      this.form.setValue({ displayName: currentUser.displayName ?? '' });
    }
    this.editingName.set(false);
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

  logout(): void {
    this.confirmAction({
      title: 'Log out',
      message: 'Are you sure you want to log out?',
      confirmLabel: 'Log out',
    }).subscribe((ok) => {
      if (!ok) {
        return;
      }
      this.currentEventService.clear();
      this.authService.logout();
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
    const trimmedDisplayName = this.form.getRawValue().displayName.trim();
    this.userApi
      .updateMe({
        displayName: trimmedDisplayName.length > 0 ? trimmedDisplayName : null,
        avatarUrl: currentUser.avatarUrl,
      })
      .subscribe({
        next: (updated) => {
          this.currentUserService.setUser(updated);
          this.saving.set(false);
          this.editingName.set(false);
          this.snackBar.open('Profile updated', 'Dismiss', { duration: 3000 });
        },
        error: () => {
          this.saving.set(false);
          this.snackBar.open('Could not update profile', 'Dismiss', { duration: 3000 });
        },
      });
  }
}
