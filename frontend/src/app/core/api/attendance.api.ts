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

  getAttendees(eventId: number): Observable<AttendeeResponse[]> {
    return this.http.get<AttendeeResponse[]>(this.getUrl(`/api/v1/events/${eventId}/attendees`));
  }

  join(eventId: number): Observable<void> {
    return this.http.post<void>(this.getUrl(`/api/v1/events/${eventId}/attendees/join`), null);
  }

  confirm(eventId: number, userId: number): Observable<void> {
    return this.http.post<void>(
      this.getUrl(`/api/v1/events/${eventId}/attendees/${userId}/confirm`),
      null,
    );
  }

  remove(eventId: number, userId: number): Observable<void> {
    return this.http.delete<void>(this.getUrl(`/api/v1/events/${eventId}/attendees/${userId}`));
  }
}
