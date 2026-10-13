import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { PushConfig, PushDevice, SavePushSubscriptionRequest } from '../models/push';
import { ENVIRONMENT } from '../../../environments/environment.model';

/** Web push for news (ADR-017). Config and saving 404 while push isn't set up on the server. */
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

  list(): Observable<PushDevice[]> {
    return this.http.get<PushDevice[]>(this.subscriptions);
  }

  save(subscription: SavePushSubscriptionRequest): Observable<void> {
    return this.http.put<void>(this.subscriptions, subscription);
  }

  /** Removes the subscription with this endpoint: what a device does to turn itself off. */
  remove(endpoint: string): Observable<void> {
    return this.http.delete<void>(this.subscriptions, { body: { endpoint } });
  }

  rename(id: number, name: string): Observable<void> {
    return this.http.patch<void>(`${this.subscriptions}/${id}`, { name });
  }

  /** Removes any of the member's devices, e.g. a lost phone. */
  revoke(id: number): Observable<void> {
    return this.http.delete<void>(`${this.subscriptions}/${id}`);
  }
}
