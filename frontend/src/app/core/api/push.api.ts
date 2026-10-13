import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { PushConfig, SavePushSubscriptionRequest } from '../models/push';
import { ENVIRONMENT } from '../../../environments/environment.model';

/** Web push for news (ADR-017). Everything 404s while push isn't set up on the server. */
@Injectable({ providedIn: 'root' })
export class PushApi {
  private readonly http = inject(HttpClient);
  private readonly env = inject(ENVIRONMENT);

  private get subscriptions(): string {
    return `${this.env.apiAddress}/api/v1/me/push-subscriptions`;
  }

  getConfig(): Observable<PushConfig> {
    return this.http.get<PushConfig>(`${this.env.apiAddress}/api/v1/push/config`);
  }

  save(subscription: SavePushSubscriptionRequest): Observable<void> {
    return this.http.put<void>(this.subscriptions, subscription);
  }

  remove(endpoint: string): Observable<void> {
    return this.http.delete<void>(this.subscriptions, { body: { endpoint } });
  }
}
