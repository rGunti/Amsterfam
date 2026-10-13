import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { DiscordWebhookStatus } from '../models/discord-webhook';
import { ENVIRONMENT } from '../../../environments/environment.model';

/** The event's Discord webhook for news (ADR-015). Organisers only. */
@Injectable({ providedIn: 'root' })
export class DiscordWebhookApi {
  private readonly http = inject(HttpClient);
  private readonly env = inject(ENVIRONMENT);

  private url(eventId: string, path = ''): string {
    return `${this.env.apiAddress}/api/v1/events/${eventId}/discord-webhook${path}`;
  }

  get(eventId: string): Observable<DiscordWebhookStatus> {
    return this.http.get<DiscordWebhookStatus>(this.url(eventId));
  }

  set(eventId: string, url: string): Observable<DiscordWebhookStatus> {
    return this.http.put<DiscordWebhookStatus>(this.url(eventId), { url });
  }

  remove(eventId: string): Observable<void> {
    return this.http.delete<void>(this.url(eventId));
  }

  /** Sends a short test message to the channel. */
  test(eventId: string): Observable<void> {
    return this.http.post<void>(this.url(eventId, '/test'), null);
  }
}
