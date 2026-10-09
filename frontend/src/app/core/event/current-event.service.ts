import { DOCUMENT, Injectable, computed, inject, signal } from '@angular/core';
import { toObservable } from '@angular/core/rxjs-interop';
import {
  EMPTY,
  Observable,
  catchError,
  distinctUntilChanged,
  exhaustMap,
  fromEvent,
  map,
  startWith,
  switchMap,
  tap,
  timer,
} from 'rxjs';

import { EventApi } from '../api/event.api';
import { EventResponse } from '../models/event';

/** How often the open event is re-fetched for the nav badges (ADR-006 allows 30–60 s). */
export const EVENT_POLL_INTERVAL_MS = 60_000;

@Injectable({ providedIn: 'root' })
export class CurrentEventService {
  private readonly eventApi = inject(EventApi);
  private readonly document = inject(DOCUMENT);

  private readonly _eventId = signal<string | null>(null);
  private readonly _event = signal<EventResponse | null>(null);
  private readonly _loading = signal(false);

  readonly eventId = this._eventId.asReadonly();
  readonly event = this._event.asReadonly();
  readonly loading = this._loading.asReadonly();

  readonly role = computed(() => this._event()?.currentUserRole ?? null);
  readonly isMember = computed(() => this._event()?.isMember ?? false);
  readonly isOrganiser = computed(() => this._event()?.currentUserRole === 'Organiser');

  constructor() {
    this.pollBadges();
  }

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

  /** Keeps the nav badge in step after a page (re)loads or changes the attendee list. */
  setPendingCount(count: number): void {
    const event = this._event();
    if (event && event.pendingAttendeeCount !== null && event.pendingAttendeeCount !== count) {
      this._event.set({ ...event, pendingAttendeeCount: count });
    }
  }

  /** Keeps the news badge in step after the feed is opened. */
  setUnreadNews(count: number): void {
    const event = this._event();
    if (event && event.unreadNewsCount != null && event.unreadNewsCount !== count) {
      this._event.set({ ...event, unreadNewsCount: count });
    }
  }

  /**
   * While an event is open and the tab is visible, re-fetches it every minute so the nav
   * badges notice new posts. Only the badge count is taken over: replacing the whole event
   * would disturb pages that are editing it.
   */
  private pollBadges(): void {
    const visible$ = fromEvent(this.document, 'visibilitychange').pipe(
      startWith(null),
      map(() => this.document.visibilityState === 'visible'),
      distinctUntilChanged(),
    );

    toObservable(this._eventId)
      .pipe(
        switchMap((id) =>
          id === null
            ? EMPTY
            : visible$.pipe(
                switchMap((visible) =>
                  visible ? timer(EVENT_POLL_INTERVAL_MS, EVENT_POLL_INTERVAL_MS) : EMPTY,
                ),
                exhaustMap(() => this.eventApi.getEvent(id).pipe(catchError(() => EMPTY))),
              ),
        ),
      )
      .subscribe((fresh) => {
        const event = this._event();
        if (event?.id === fresh.id && event.unreadNewsCount !== fresh.unreadNewsCount) {
          this._event.set({ ...event, unreadNewsCount: fresh.unreadNewsCount });
        }
      });
  }

  clear(): void {
    this._eventId.set(null);
    this._event.set(null);
  }
}
