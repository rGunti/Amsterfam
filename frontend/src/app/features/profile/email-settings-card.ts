import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSlideToggleChange, MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Observable, firstValueFrom } from 'rxjs';

import { UserApi } from '../../core/api/user.api';
import { NotificationSettings } from '../../core/models/notification-settings';

/** Same limit as the server (RFC 5321). */
export const MAX_EMAIL_LENGTH = 254;

/**
 * News by email on the profile page (ADR-018): the opt-in, and an optional address other than
 * the one from the login, which only gets news once confirmed. Hidden while the server has no
 * SMTP set up.
 */
@Component({
  selector: 'app-email-settings-card',
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatCardModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatSlideToggleModule,
  ],
  templateUrl: './email-settings-card.html',
  styleUrl: './email-settings-card.scss',
})
export class EmailSettingsCard implements OnInit {
  private readonly api = inject(UserApi);
  private readonly snackBar = inject(MatSnackBar);

  readonly maxLength = MAX_EMAIL_LENGTH;
  readonly settings = signal<NotificationSettings | null>(null);
  readonly busy = signal(false);
  readonly editing = signal(false);
  readonly emailControl = new FormControl('', {
    nonNullable: true,
    validators: [Validators.required, Validators.email, Validators.maxLength(MAX_EMAIL_LENGTH)],
  });

  readonly pending = computed(() => {
    const s = this.settings();
    return s?.notificationEmail && !s.notificationEmailConfirmed ? s.notificationEmail : null;
  });

  /** Email can be turned on with an address to send to, or one waiting for confirmation. */
  readonly canEnable = computed(() => {
    const s = this.settings();
    return !!s && (s.effectiveEmail !== null || this.pending() !== null);
  });

  async ngOnInit(): Promise<void> {
    try {
      this.settings.set(await firstValueFrom(this.api.getNotificationSettings()));
    } catch {
      // The card stays hidden; email is optional.
    }
  }

  async toggle(change: MatSlideToggleChange): Promise<void> {
    const ok = await this.run(this.api.updateNotificationSettings(change.checked), () =>
      change.checked ? 'Could not turn on email' : 'Could not turn off email',
    );
    if (!ok) {
      change.source.checked = this.settings()?.emailNotifications ?? false;
    }
  }

  startEdit(): void {
    this.emailControl.setValue(this.pending() ?? '');
    this.editing.set(true);
  }

  cancelEdit(): void {
    this.editing.set(false);
  }

  async save(): Promise<void> {
    const email = this.emailControl.value.trim();
    if (!email || this.emailControl.invalid) {
      this.emailControl.markAsTouched();
      return;
    }
    const ok = await this.run(this.api.setNotificationEmail(email), (error) =>
      error.status === 400
        ? "That isn't a valid email address."
        : error.status === 503
          ? 'The confirmation email could not be sent. Try "Resend" later.'
          : 'Could not save the address',
    );
    // A 503 still saved the address as pending, so show it.
    if (ok || this.pending() === email) {
      this.editing.set(false);
    }
    if (ok && this.pending() === email) {
      this.snackBar.open(`We sent a confirmation link to ${email}`, 'Dismiss', { duration: 5000 });
    }
  }

  async resend(): Promise<void> {
    const ok = await this.run(this.api.resendNotificationEmail(), (error) =>
      error.status === 429
        ? 'Wait a minute before asking for another link.'
        : 'Could not send the confirmation email',
    );
    if (ok) {
      this.snackBar.open(`We sent a new link to ${this.pending()}`, 'Dismiss', { duration: 5000 });
    }
  }

  /** Drops the custom address, pending or confirmed, so news goes to the login address. */
  async useLoginAddress(): Promise<void> {
    await this.run(this.api.clearNotificationEmail(), () => 'Could not remove the address');
  }

  private async run(
    request: Observable<NotificationSettings>,
    message: (error: HttpErrorResponse) => string,
  ): Promise<boolean> {
    this.busy.set(true);
    try {
      this.settings.set(await firstValueFrom(request));
      return true;
    } catch (error) {
      if (error instanceof HttpErrorResponse && error.status === 503) {
        // Saved as pending even though the mail didn't go out.
        await this.reload();
      }
      this.snackBar.open(
        message(error instanceof HttpErrorResponse ? error : new HttpErrorResponse({})),
        'Dismiss',
        { duration: 5000 },
      );
      return false;
    } finally {
      this.busy.set(false);
    }
  }

  private async reload(): Promise<void> {
    try {
      this.settings.set(await firstValueFrom(this.api.getNotificationSettings()));
    } catch {
      // Keep what we have.
    }
  }
}
