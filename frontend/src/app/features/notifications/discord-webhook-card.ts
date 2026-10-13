import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, inject, input, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSnackBar } from '@angular/material/snack-bar';

import { DiscordWebhookApi } from '../../core/api/discord-webhook.api';
import { DiscordWebhookStatus } from '../../core/models/discord-webhook';
import { DiscordLogo } from '../../shared/discord-logo/discord-logo';

/**
 * Organisers connect a Discord channel here; published news is mirrored to it (ADR-015).
 * The webhook URL is a secret, so once saved only its last characters are shown.
 */
@Component({
  selector: 'app-discord-webhook-card',
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatCardModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    DiscordLogo,
  ],
  template: `
    <mat-card>
      <mat-card-header>
        <mat-card-title class="title"><app-discord-logo class="logo" /> Discord</mat-card-title>
        <mat-card-subtitle>Post news to a Discord channel too.</mat-card-subtitle>
      </mat-card-header>
      <mat-card-content>
        @if (status(); as s) {
          @if (s.configured && !replacing()) {
            <p class="connected">
              <mat-icon>check_circle</mat-icon>
              Connected (webhook ending in “{{ s.hint }}”). New posts with “Notify channels” ticked
              show up there.
            </p>
            <div class="actions">
              <button mat-stroked-button (click)="test()" [disabled]="busy()">
                <mat-icon>send</mat-icon> Send test
              </button>
              <button mat-button (click)="replacing.set(true)" [disabled]="busy()">Replace</button>
              <button mat-button color="warn" (click)="remove()" [disabled]="busy()">
                Disconnect
              </button>
            </div>
          } @else {
            <p class="hint">
              In Discord, open the channel's settings → Integrations → Webhooks, create one and copy
              its URL.
            </p>
            <mat-form-field appearance="outline" class="url">
              <mat-label>Webhook URL</mat-label>
              <input
                matInput
                type="url"
                autocomplete="off"
                [formControl]="url"
                placeholder="https://discord.com/api/webhooks/…"
              />
              @if (error(); as e) {
                <mat-hint class="error">{{ e }}</mat-hint>
              }
            </mat-form-field>
            <div class="actions">
              <button mat-flat-button (click)="save()" [disabled]="busy() || url.invalid">
                Connect
              </button>
              @if (s.configured) {
                <button mat-button (click)="cancelReplace()" [disabled]="busy()">Cancel</button>
              }
            </div>
          }
        }
      </mat-card-content>
    </mat-card>
  `,
  styles: `
    .title {
      display: flex;
      align-items: center;
      gap: 8px;
    }
    .logo {
      font-size: 24px;
      /* Discord's own blurple. */
      color: #5865f2;
    }
    .connected {
      display: flex;
      gap: 8px;
      align-items: flex-start;
    }
    .url {
      width: 100%;
    }
    .actions {
      display: flex;
      flex-wrap: wrap;
      gap: 8px;
    }
    .error {
      color: var(--mat-sys-error);
    }
  `,
})
export class DiscordWebhookCard implements OnInit {
  private readonly api = inject(DiscordWebhookApi);
  private readonly snackBar = inject(MatSnackBar);

  readonly eventId = input.required<string>();

  readonly status = signal<DiscordWebhookStatus | null>(null);
  readonly busy = signal(false);
  readonly replacing = signal(false);
  readonly error = signal<string | null>(null);
  readonly url = new FormControl('', { nonNullable: true, validators: [Validators.required] });

  ngOnInit(): void {
    this.api.get(this.eventId()).subscribe({
      next: (status) => this.status.set(status),
      // Not an organiser after all, or offline: the card just stays empty.
      error: () => this.status.set(null),
    });
  }

  save(): void {
    this.busy.set(true);
    this.error.set(null);
    this.api.set(this.eventId(), this.url.value).subscribe({
      next: (status) => {
        this.status.set(status);
        this.url.reset();
        this.replacing.set(false);
        this.busy.set(false);
        this.snackBar.open('Discord connected', 'Dismiss', { duration: 3000 });
      },
      error: (err: HttpErrorResponse) => {
        this.error.set(err.error?.error ?? 'Could not save the webhook');
        this.busy.set(false);
      },
    });
  }

  cancelReplace(): void {
    this.url.reset();
    this.error.set(null);
    this.replacing.set(false);
  }

  remove(): void {
    this.busy.set(true);
    this.api.remove(this.eventId()).subscribe({
      next: () => {
        this.status.set({ configured: false, hint: null });
        this.busy.set(false);
        this.snackBar.open('Discord disconnected', 'Dismiss', { duration: 3000 });
      },
      error: () => {
        this.busy.set(false);
        this.snackBar.open('Could not disconnect Discord', 'Dismiss', { duration: 3000 });
      },
    });
  }

  test(): void {
    this.busy.set(true);
    this.api.test(this.eventId()).subscribe({
      next: () => {
        this.busy.set(false);
        this.snackBar.open('Test message sent', 'Dismiss', { duration: 3000 });
      },
      error: (err: HttpErrorResponse) => {
        this.busy.set(false);
        this.snackBar.open(err.error?.error ?? 'Could not send the test message', 'Dismiss', {
          duration: 5000,
        });
      },
    });
  }
}
