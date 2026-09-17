import { Injectable, computed, inject, signal } from '@angular/core';
import { Observable, tap } from 'rxjs';

import { EventApi } from '../api/event.api';
import { EventResponse } from '../models/event';

@Injectable({ providedIn: 'root' })
export class CurrentEventService {
  private readonly eventApi = inject(EventApi);

  private readonly _eventId = signal<string | null>(null);
  private readonly _event = signal<EventResponse | null>(null);
  private readonly _loading = signal(false);

  readonly eventId = this._eventId.asReadonly();
  readonly event = this._event.asReadonly();
  readonly loading = this._loading.asReadonly();

  readonly role = computed(() => this._event()?.currentUserRole ?? null);
  readonly isMember = computed(() => this._event()?.isMember ?? false);
  readonly isOrganiser = computed(() => this._event()?.currentUserRole === 'Organiser');

  // Imperative rather than an effect() off route params (unlike CurrentUserService):
  // the event guard needs to await the load and branch on success/failure.
  loadEvent(id: string): Observable<EventResponse> {
    this._loading.set(true);
    return this.eventApi.getEvent(id).pipe(
      tap({
        next: (event) => {
          this._eventId.set(event.id);
          this._event.set(event);
          this._loading.set(false);
        },
        error: () => {
          this._loading.set(false);
        },
      }),
    );
  }

  setEvent(event: EventResponse): void {
    this._eventId.set(event.id);
    this._event.set(event);
  }

  clear(): void {
    this._eventId.set(null);
    this._event.set(null);
  }
}
