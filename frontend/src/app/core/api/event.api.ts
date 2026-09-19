import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import {
  CreateEventRequest,
  EventResponse,
  EventStatus,
  UpdateEventRequest,
} from '../models/event';
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

  transitionEvent(id: string, target: EventStatus): Observable<EventResponse> {
    return this.http.post<EventResponse>(this.getUrl(`/api/v1/events/${id}/status`), { target });
  }

  deleteEvent(id: string): Observable<void> {
    return this.http.delete<void>(this.getUrl(`/api/v1/events/${id}`));
  }
}
