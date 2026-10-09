import { HttpClient, HttpParams, HttpUrlEncodingCodec } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { NewsFeed, NewsPost, UpsertNewsPostRequest } from '../models/news';
import { ENVIRONMENT } from '../../../environments/environment.model';

/**
 * Angular's default codec leaves `+` unescaped, which the server reads as a space; the
 * paging cursor is a timestamp like `…+00:00`.
 */
class StrictCodec extends HttpUrlEncodingCodec {
  override encodeKey(key: string): string {
    return encodeURIComponent(key);
  }

  override encodeValue(value: string): string {
    return encodeURIComponent(value);
  }
}

@Injectable({ providedIn: 'root' })
export class NewsApi {
  private readonly http = inject(HttpClient);
  private readonly env = inject(ENVIRONMENT);

  private url(eventId: string, path = ''): string {
    return `${this.env.apiAddress}/api/v1/events/${eventId}/news${path}`;
  }

  /** Newest first. Pass the last post to load older ones. */
  list(eventId: string, before?: NewsPost, limit?: number): Observable<NewsFeed> {
    let params = new HttpParams({ encoder: new StrictCodec() });
    if (before?.publishedAt) {
      params = params.set('before', before.publishedAt).set('beforeId', before.id);
    }
    if (limit !== undefined) {
      params = params.set('limit', limit);
    }
    return this.http.get<NewsFeed>(this.url(eventId), { params });
  }

  get(eventId: string, postId: number): Observable<NewsPost> {
    return this.http.get<NewsPost>(this.url(eventId, `/${postId}`));
  }

  create(eventId: string, request: UpsertNewsPostRequest): Observable<NewsPost> {
    return this.http.post<NewsPost>(this.url(eventId), request);
  }

  update(eventId: string, postId: number, request: UpsertNewsPostRequest): Observable<NewsPost> {
    return this.http.put<NewsPost>(this.url(eventId, `/${postId}`), request);
  }

  delete(eventId: string, postId: number): Observable<void> {
    return this.http.delete<void>(this.url(eventId, `/${postId}`));
  }

  markSeen(eventId: string): Observable<void> {
    return this.http.post<void>(this.url(eventId, '/seen'), null);
  }
}
