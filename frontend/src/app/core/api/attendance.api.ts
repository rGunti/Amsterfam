import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { AttendeeResponse } from '../models/attendance';
import { ENVIRONMENT } from '../../../environments/environment.model';

@Injectable({ providedIn: 'root' })
export class AttendanceApi {
  private readonly http = inject(HttpClient);
  private readonly env = inject(ENVIRONMENT);

  private getUrl(route: string): string {
    return `${this.env.apiAddress}${route}`;
  }

  getAttendees(eventId: string): Observable<AttendeeResponse[]> {
    return this.http.get<AttendeeResponse[]>(this.getUrl(`/api/v1/events/${eventId}/attendees`));
  }

  confirm(eventId: string, userId: number): Observable<void> {
    return this.http.post<void>(
      this.getUrl(`/api/v1/events/${eventId}/attendees/${userId}/confirm`),
      null,
    );
  }

  remove(eventId: string, userId: number): Observable<void> {
    return this.http.delete<void>(this.getUrl(`/api/v1/events/${eventId}/attendees/${userId}`));
  }

  promote(eventId: string, userId: number): Observable<void> {
    return this.http.post<void>(
      this.getUrl(`/api/v1/events/${eventId}/attendees/${userId}/promote`),
      null,
    );
  }

  demote(eventId: string, userId: number): Observable<void> {
    return this.http.post<void>(
      this.getUrl(`/api/v1/events/${eventId}/attendees/${userId}/demote`),
      null,
    );
  }

  transferOwnership(eventId: string, userId: number): Observable<void> {
    return this.http.post<void>(
      this.getUrl(`/api/v1/events/${eventId}/attendees/${userId}/transfer-ownership`),
      null,
    );
  }
}
