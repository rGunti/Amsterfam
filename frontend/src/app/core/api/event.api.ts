import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { CreateEventRequest, EventResponse, UpdateEventRequest } from '../models/event';
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

  createEvent(request: CreateEventRequest): Observable<EventResponse> {
    return this.http.post<EventResponse>(this.getUrl('/api/v1/events'), request);
  }

  getEvent(id: string): Observable<EventResponse> {
    return this.http.get<EventResponse>(this.getUrl(`/api/v1/events/${id}`));
  }

  updateEvent(id: string, request: UpdateEventRequest): Observable<EventResponse> {
    return this.http.put<EventResponse>(this.getUrl(`/api/v1/events/${id}`), request);
  }

  publishEvent(id: string): Observable<EventResponse> {
    return this.http.post<EventResponse>(this.getUrl(`/api/v1/events/${id}/publish`), null);
  }

  unpublishEvent(id: string): Observable<EventResponse> {
    return this.http.post<EventResponse>(this.getUrl(`/api/v1/events/${id}/unpublish`), null);
  }

  closeEvent(id: string): Observable<EventResponse> {
    return this.http.post<EventResponse>(this.getUrl(`/api/v1/events/${id}/close`), null);
  }

  reopenEvent(id: string): Observable<EventResponse> {
    return this.http.post<EventResponse>(this.getUrl(`/api/v1/events/${id}/reopen`), null);
  }

  deleteEvent(id: string): Observable<void> {
    return this.http.delete<void>(this.getUrl(`/api/v1/events/${id}`));
  }
}
