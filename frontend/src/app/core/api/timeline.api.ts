import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { TimelineEntry } from '../models/timeline';
import { ENVIRONMENT } from '../../../environments/environment.model';

@Injectable({ providedIn: 'root' })
export class TimelineApi {
  private readonly http = inject(HttpClient);
  private readonly env = inject(ENVIRONMENT);

  /** Newest first. Pass the last entry's id as `before` to load older entries. */
  list(eventId: string, before?: number, limit?: number): Observable<TimelineEntry[]> {
    let params = new HttpParams();
    if (before !== undefined) {
      params = params.set('before', before);
    }
    if (limit !== undefined) {
      params = params.set('limit', limit);
    }
    return this.http.get<TimelineEntry[]>(
      `${this.env.apiAddress}/api/v1/events/${eventId}/timeline`,
      { params },
    );
  }
}
