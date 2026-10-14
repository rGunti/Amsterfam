import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { TelegramChatOption, TelegramStatus } from '../models/telegram';
import { ENVIRONMENT } from '../../../environments/environment.model';

/** The event's Telegram bot and group for news (ADR-015). Organisers only. */
@Injectable({ providedIn: 'root' })
export class TelegramApi {
  private readonly http = inject(HttpClient);
  private readonly env = inject(ENVIRONMENT);

  private url(eventId: string, path = ''): string {
    return `${this.env.apiAddress}/api/v1/events/${eventId}/telegram${path}`;
  }

  get(eventId: string): Observable<TelegramStatus> {
    return this.http.get<TelegramStatus>(this.url(eventId));
  }

  /** Saves a new bot token; this clears the picked group. */
  setToken(eventId: string, token: string): Observable<TelegramStatus> {
    return this.http.put<TelegramStatus>(this.url(eventId), { token });
  }

  /** Picks the group news goes to, for the saved token. */
  setChat(eventId: string, chatId: number): Observable<TelegramStatus> {
    return this.http.put<TelegramStatus>(this.url(eventId), { chatId });
  }

  remove(eventId: string): Observable<void> {
    return this.http.delete<void>(this.url(eventId));
  }

  /** Groups the bot has heard from in the last day. */
  chats(eventId: string): Observable<TelegramChatOption[]> {
    return this.http.get<TelegramChatOption[]>(this.url(eventId, '/chats'));
  }

  /** Sends a short test message to the group. */
  test(eventId: string): Observable<void> {
    return this.http.post<void>(this.url(eventId, '/test'), null);
  }
}
