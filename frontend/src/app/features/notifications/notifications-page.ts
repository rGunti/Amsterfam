import { Component, inject } from '@angular/core';

import { CurrentEventService } from '../../core/event/current-event.service';
import { DiscordWebhookCard } from './discord-webhook-card';
import { TelegramCard } from './telegram-card';

/**
 * Where organisers set up the outside channels news is sent to (ADR-015): Discord and
 * Telegram groups.
 */
@Component({
  selector: 'app-notifications-page',
  imports: [DiscordWebhookCard, TelegramCard],
  template: `
    @if (event(); as ev) {
      <div class="page">
        <h1>Notifications</h1>
        <p class="note">
          Choose where published news is sent besides the app. Each post can opt out with “Notify
          channels” when you write it.
        </p>
        <app-discord-webhook-card [eventId]="ev.id" />
        <app-telegram-card [eventId]="ev.id" />
      </div>
    }
  `,
  styles: `
    .page {
      max-width: 640px;
      margin: 24px auto;
    }
    h1 {
      margin: 0 0 8px;
    }
    app-telegram-card {
      display: block;
      margin-top: 16px;
    }
    .note {
      margin: 0 0 16px;
      color: var(--mat-sys-on-surface-variant);
    }
  `,
})
export class NotificationsPage {
  readonly event = inject(CurrentEventService).event;
}
