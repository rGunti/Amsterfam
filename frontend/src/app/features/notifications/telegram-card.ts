import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, computed, inject, input, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatRadioModule } from '@angular/material/radio';
import { MatSnackBar } from '@angular/material/snack-bar';

import { TelegramApi } from '../../core/api/telegram.api';
import { TelegramChatOption, TelegramStatus } from '../../core/models/telegram';
import { TelegramLogo } from '../../shared/telegram-logo/telegram-logo';

/**
 * Organisers connect a Telegram group here; published news is mirrored to it (ADR-015). Two
 * steps: save the bot token from @BotFather, then pick a group the bot is in. The token is a
 * secret, so once saved only its last characters are shown.
 */
@Component({
  selector: 'app-telegram-card',
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatCardModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatRadioModule,
    TelegramLogo,
  ],
  template: `
    <mat-card>
      <mat-card-header>
        <mat-card-title class="title"><app-telegram-logo class="logo" /> Telegram</mat-card-title>
        <mat-card-subtitle>Post news to a Telegram group too.</mat-card-subtitle>
      </mat-card-header>
      <mat-card-content>
        @if (status(); as s) {
          @if (!s.tokenSaved || replacingToken()) {
            <p class="hint">
              In Telegram, message &#64;BotFather, send <code>/newbot</code> and follow the steps.
              Paste the token it gives you here.
            </p>
            <mat-form-field appearance="outline" class="wide">
              <mat-label>Bot token</mat-label>
              <input
                matInput
                type="password"
                autocomplete="off"
                [formControl]="token"
                placeholder="123456789:AA…"
              />
              @if (error(); as e) {
                <mat-hint class="error">{{ e }}</mat-hint>
              }
            </mat-form-field>
            <div class="actions">
              <button mat-flat-button (click)="saveToken()" [disabled]="busy() || token.invalid">
                Save token
              </button>
              @if (s.tokenSaved) {
                <button mat-button (click)="cancel()" [disabled]="busy()">Cancel</button>
              }
            </div>
          } @else if (!s.configured || changingChat()) {
            <p class="hint">
              Add <strong>&#64;{{ s.botUsername }}</strong> to your group and send a message there,
              then look for the group below.
            </p>
            <div class="actions">
              <button mat-stroked-button (click)="detect()" [disabled]="busy()">
                <mat-icon>search</mat-icon> Detect groups
              </button>
            </div>
            @if (chats(); as list) {
              @if (list.length) {
                <mat-radio-group
                  class="chats"
                  aria-label="Group"
                  [value]="selectedChat()"
                  (change)="pick($event.value)"
                >
                  @for (chat of list; track chat.id) {
                    <mat-radio-button [value]="chat.id">{{ chat.title }}</mat-radio-button>
                  }
                </mat-radio-group>
              } @else {
                <p class="hint">
                  No groups yet. Make sure the bot is in the group and someone wrote there in the
                  last day, then try again.
                </p>
              }
            }
            <mat-form-field appearance="outline" class="wide">
              <mat-label>Or enter the chat id</mat-label>
              <input matInput inputmode="numeric" autocomplete="off" [formControl]="manualChat" />
            </mat-form-field>
            @if (error(); as e) {
              <p class="error">{{ e }}</p>
            }
            <div class="actions">
              <button mat-flat-button (click)="saveChat()" [disabled]="busy() || !chatToSave()">
                Connect
              </button>
              @if (s.configured) {
                <button mat-button (click)="cancel()" [disabled]="busy()">Cancel</button>
              }
              <button mat-button (click)="replacingToken.set(true)" [disabled]="busy()">
                Replace token
              </button>
              <button mat-button color="warn" (click)="remove()" [disabled]="busy()">
                Disconnect
              </button>
            </div>
          } @else {
            <p class="connected">
              <mat-icon>check_circle</mat-icon>
              Posting to “{{ s.chatTitle }}” as &#64;{{ s.botUsername }} (token ending in “{{
                s.tokenHint
              }}”). New posts with “Notify channels” ticked show up there.
            </p>
            <div class="actions">
              <button mat-stroked-button (click)="test()" [disabled]="busy()">
                <mat-icon>send</mat-icon> Send test
              </button>
              <button mat-button (click)="changingChat.set(true)" [disabled]="busy()">
                Change group
              </button>
              <button mat-button (click)="replacingToken.set(true)" [disabled]="busy()">
                Replace token
              </button>
              <button mat-button color="warn" (click)="remove()" [disabled]="busy()">
                Disconnect
              </button>
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
      /* Telegram's own blue. */
      color: #26a5e4;
    }
    .connected {
      display: flex;
      gap: 8px;
      align-items: flex-start;
    }
    .connected mat-icon {
      /* Long text wraps instead of squeezing the icon. */
      flex-shrink: 0;
    }
    .wide {
      width: 100%;
      margin-top: 8px;
    }
    .chats {
      display: flex;
      flex-direction: column;
      margin: 8px 0;
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
export class TelegramCard implements OnInit {
  private readonly api = inject(TelegramApi);
  private readonly snackBar = inject(MatSnackBar);

  readonly eventId = input.required<string>();

  readonly status = signal<TelegramStatus | null>(null);
  readonly busy = signal(false);
  readonly replacingToken = signal(false);
  readonly changingChat = signal(false);
  readonly error = signal<string | null>(null);
  readonly chats = signal<TelegramChatOption[] | null>(null);
  readonly selectedChat = signal<number | null>(null);
  readonly token = new FormControl('', { nonNullable: true, validators: [Validators.required] });
  readonly manualChat = new FormControl('', {
    nonNullable: true,
    validators: [Validators.pattern(/^-?\d+$/)],
  });
  private readonly manualChatValue = signal('');

  /** Typing an id clears the picked group and picking one clears the typed id, so only one is set. */
  readonly chatToSave = computed(() => {
    const typed = this.manualChatValue().trim();
    if (typed) return /^-?\d+$/.test(typed) ? Number(typed) : null;
    return this.selectedChat();
  });

  constructor() {
    this.manualChat.valueChanges.subscribe((v) => {
      this.manualChatValue.set(v);
      if (v.trim()) this.selectedChat.set(null);
    });
  }

  pick(chatId: number): void {
    this.manualChat.setValue('');
    this.selectedChat.set(chatId);
  }

  ngOnInit(): void {
    this.api.get(this.eventId()).subscribe({
      next: (status) => this.status.set(status),
      // Not an organiser after all, or offline: the card just stays empty.
      error: () => this.status.set(null),
    });
  }

  saveToken(): void {
    this.busy.set(true);
    this.error.set(null);
    this.api.setToken(this.eventId(), this.token.value).subscribe({
      next: (status) => {
        this.done(status);
        this.snackBar.open('Bot token saved. Now pick the group.', 'Dismiss', { duration: 3000 });
      },
      error: (err: HttpErrorResponse) => this.fail(err, 'Could not save the token'),
    });
  }

  detect(): void {
    this.busy.set(true);
    this.error.set(null);
    this.api.chats(this.eventId()).subscribe({
      next: (chats) => {
        this.chats.set(chats);
        if (chats.length === 1) this.selectedChat.set(chats[0].id);
        this.busy.set(false);
      },
      error: (err: HttpErrorResponse) => this.fail(err, 'Could not look for groups'),
    });
  }

  saveChat(): void {
    const chatId = this.chatToSave();
    if (chatId === null) return;
    this.busy.set(true);
    this.error.set(null);
    this.api.setChat(this.eventId(), chatId).subscribe({
      next: (status) => {
        this.done(status);
        this.snackBar.open('Telegram connected', 'Dismiss', { duration: 3000 });
      },
      error: (err: HttpErrorResponse) => this.fail(err, 'Could not connect the group'),
    });
  }

  cancel(): void {
    this.token.reset();
    this.manualChat.reset();
    this.error.set(null);
    this.replacingToken.set(false);
    this.changingChat.set(false);
  }

  remove(): void {
    this.busy.set(true);
    this.api.remove(this.eventId()).subscribe({
      next: () => {
        this.done({
          configured: false,
          tokenSaved: false,
          tokenHint: null,
          botUsername: null,
          chatId: null,
          chatTitle: null,
        });
        this.snackBar.open('Telegram disconnected', 'Dismiss', { duration: 3000 });
      },
      error: () => {
        this.busy.set(false);
        this.snackBar.open('Could not disconnect Telegram', 'Dismiss', { duration: 3000 });
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

  private done(status: TelegramStatus): void {
    this.status.set(status);
    this.cancel();
    this.chats.set(null);
    this.selectedChat.set(null);
    this.busy.set(false);
  }

  private fail(err: HttpErrorResponse, fallback: string): void {
    this.error.set(err.error?.error ?? fallback);
    this.busy.set(false);
  }
}
