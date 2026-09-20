import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import {
  CreateJoinLinkRequest,
  JoinLinkPreviewResponse,
  JoinLinkResponse,
} from '../models/join-link';
import { ENVIRONMENT } from '../../../environments/environment.model';

@Injectable({ providedIn: 'root' })
export class JoinLinkApi {
  private readonly http = inject(HttpClient);
  private readonly env = inject(ENVIRONMENT);

  private getUrl(route: string): string {
    return `${this.env.apiAddress}${route}`;
  }

  list(eventId: string): Observable<JoinLinkResponse[]> {
    return this.http.get<JoinLinkResponse[]>(this.getUrl(`/api/v1/events/${eventId}/join-links`));
  }

  create(eventId: string, request: CreateJoinLinkRequest): Observable<JoinLinkResponse> {
    return this.http.post<JoinLinkResponse>(
      this.getUrl(`/api/v1/events/${eventId}/join-links`),
      request,
    );
  }

  revoke(eventId: string, linkId: number): Observable<void> {
    return this.http.delete<void>(this.getUrl(`/api/v1/events/${eventId}/join-links/${linkId}`));
  }

  /** Revokes the link and returns a new one with the same kind, label, expiry and use limit. */
  regenerate(eventId: string, linkId: number): Observable<JoinLinkResponse> {
    return this.http.post<JoinLinkResponse>(
      this.getUrl(`/api/v1/events/${eventId}/join-links/${linkId}/regenerate`),
      null,
    );
  }

  preview(token: string): Observable<JoinLinkPreviewResponse> {
    return this.http.get<JoinLinkPreviewResponse>(
      this.getUrl(`/api/v1/join-links/${encodeURIComponent(token)}`),
    );
  }

  bannerUrl(token: string): string {
    return this.getUrl(`/api/v1/join-links/${encodeURIComponent(token)}/banner`);
  }

  join(token: string): Observable<{ eventId: string }> {
    return this.http.post<{ eventId: string }>(
      this.getUrl(`/api/v1/join-links/${encodeURIComponent(token)}/join`),
      null,
    );
  }
}
