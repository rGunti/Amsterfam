import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import {
  DatePollEntry,
  DatePollSummaryResponse,
  UpdateDatePollEntriesRequest,
} from '../models/date-poll';
import { UpdatePollRangeRequest } from '../models/event';
import { ENVIRONMENT } from '../../../environments/environment.model';

@Injectable({ providedIn: 'root' })
export class DatePollApi {
  private readonly http = inject(HttpClient);
  private readonly env = inject(ENVIRONMENT);

  private getUrl(route: string): string {
    return `${this.env.apiAddress}${route}`;
  }

  setRange(eventId: number, request: UpdatePollRangeRequest): Observable<DatePollSummaryResponse> {
    return this.http.put<DatePollSummaryResponse>(
      this.getUrl(`/api/v1/events/${eventId}/date-poll/range`),
      request,
    );
  }

  getSummary(eventId: number): Observable<DatePollSummaryResponse> {
    return this.http.get<DatePollSummaryResponse>(
      this.getUrl(`/api/v1/events/${eventId}/date-poll`),
    );
  }

  getMyEntries(eventId: number): Observable<DatePollEntry[]> {
    return this.http.get<DatePollEntry[]>(this.getUrl(`/api/v1/events/${eventId}/date-poll/me`));
  }

  updateMyEntries(
    eventId: number,
    request: UpdateDatePollEntriesRequest,
  ): Observable<DatePollEntry[]> {
    return this.http.put<DatePollEntry[]>(
      this.getUrl(`/api/v1/events/${eventId}/date-poll/me`),
      request,
    );
  }

  deleteMyEntry(eventId: number, weekStart: string): Observable<void> {
    return this.http.delete<void>(
      this.getUrl(`/api/v1/events/${eventId}/date-poll/me/${weekStart}`),
    );
  }
}
