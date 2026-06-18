import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { EventResponse, UpdateEventRequest } from '../models/event';
import { ENVIRONMENT } from '../../../environments/environment.model';

@Injectable({ providedIn: 'root' })
export class EventApi {
  private readonly http = inject(HttpClient);
  private readonly env = inject(ENVIRONMENT);

  private getUrl(route: string): string {
    return `${this.env.apiAddress}${route}`;
  }

  getEvents(): Observable<EventResponse[]> {
    return this.http.get<EventResponse[]>(this.getUrl('/api/v1/events'));
  }

  getEvent(id: number): Observable<EventResponse> {
    return this.http.get<EventResponse>(this.getUrl(`/api/v1/events/${id}`));
  }

  updateEvent(id: number, request: UpdateEventRequest): Observable<EventResponse> {
    return this.http.put<EventResponse>(this.getUrl(`/api/v1/events/${id}`), request);
  }

  publishEvent(id: number): Observable<EventResponse> {
    return this.http.post<EventResponse>(this.getUrl(`/api/v1/events/${id}/publish`), null);
  }

  closeEvent(id: number): Observable<EventResponse> {
    return this.http.post<EventResponse>(this.getUrl(`/api/v1/events/${id}/close`), null);
  }

  reopenEvent(id: number): Observable<EventResponse> {
    return this.http.post<EventResponse>(this.getUrl(`/api/v1/events/${id}/reopen`), null);
  }
}
