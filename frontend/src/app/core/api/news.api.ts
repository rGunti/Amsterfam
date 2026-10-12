import { HttpClient, HttpParams, HttpUrlEncodingCodec } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import {
  NewsAcks,
  NewsComment,
  NewsFeed,
  NewsPost,
  NewsSummary,
  UpsertNewsPostRequest,
} from '../models/news';
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

  /**
   * Newest first, with pinned posts leading the first page. Pass the last unpinned post to
   * load older ones.
   */
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

  summary(eventId: string): Observable<NewsSummary> {
    return this.http.get<NewsSummary>(this.url(eventId, '/summary'));
  }

  pin(eventId: string, postId: number): Observable<void> {
    return this.http.put<void>(this.url(eventId, `/${postId}/pin`), null);
  }

  unpin(eventId: string, postId: number): Observable<void> {
    return this.http.delete<void>(this.url(eventId, `/${postId}/pin`));
  }

  acknowledge(eventId: string, postId: number): Observable<void> {
    return this.http.post<void>(this.url(eventId, `/${postId}/ack`), null);
  }

  acks(eventId: string, postId: number): Observable<NewsAcks> {
    return this.http.get<NewsAcks>(this.url(eventId, `/${postId}/acks`));
  }

  react(eventId: string, postId: number, emoji: string): Observable<void> {
    return this.http.put<void>(this.reactionUrl(eventId, postId, emoji), null);
  }

  unreact(eventId: string, postId: number, emoji: string): Observable<void> {
    return this.http.delete<void>(this.reactionUrl(eventId, postId, emoji));
  }

  /** Absolute URL of the post's image; fetch it with auth (see AuthImage). */
  imageUrl(eventId: string, postId: number): string {
    return this.url(eventId, `/${postId}/image`);
  }

  /**
   * Attaches or replaces the image. `initial` is for adding it straight after creating the
   * post, so it doesn't count as an edit.
   */
  uploadImage(
    eventId: string,
    postId: number,
    image: Blob,
    fileName: string,
    initial = false,
  ): Observable<{ imageFileId: string }> {
    const query = `?fileName=${encodeURIComponent(fileName)}${initial ? '&initial=true' : ''}`;
    return this.http.put<{ imageFileId: string }>(
      this.url(eventId, `/${postId}/image${query}`),
      image,
    );
  }

  deleteImage(eventId: string, postId: number): Observable<void> {
    return this.http.delete<void>(this.url(eventId, `/${postId}/image`));
  }

  /** Oldest first. Pass the last comment already shown to fetch only newer ones. */
  comments(eventId: string, postId: number, after?: NewsComment): Observable<NewsComment[]> {
    let params = new HttpParams({ encoder: new StrictCodec() });
    if (after) {
      params = params.set('after', after.createdAt).set('afterId', after.id);
    }
    return this.http.get<NewsComment[]>(this.url(eventId, `/${postId}/comments`), { params });
  }

  addComment(eventId: string, postId: number, body: string): Observable<NewsComment> {
    return this.http.post<NewsComment>(this.url(eventId, `/${postId}/comments`), { body });
  }

  editComment(
    eventId: string,
    postId: number,
    commentId: number,
    body: string,
  ): Observable<NewsComment> {
    return this.http.put<NewsComment>(this.url(eventId, `/${postId}/comments/${commentId}`), {
      body,
    });
  }

  deleteComment(eventId: string, postId: number, commentId: number): Observable<void> {
    return this.http.delete<void>(this.url(eventId, `/${postId}/comments/${commentId}`));
  }

  reactToComment(
    eventId: string,
    postId: number,
    commentId: number,
    emoji: string,
  ): Observable<void> {
    return this.http.put<void>(this.commentReactionUrl(eventId, postId, commentId, emoji), null);
  }

  unreactToComment(
    eventId: string,
    postId: number,
    commentId: number,
    emoji: string,
  ): Observable<void> {
    return this.http.delete<void>(this.commentReactionUrl(eventId, postId, commentId, emoji));
  }

  private commentReactionUrl(
    eventId: string,
    postId: number,
    commentId: number,
    emoji: string,
  ): string {
    return this.url(
      eventId,
      `/${postId}/comments/${commentId}/reactions/${encodeURIComponent(emoji)}`,
    );
  }

  private reactionUrl(eventId: string, postId: number, emoji: string): string {
    return this.url(eventId, `/${postId}/reactions/${encodeURIComponent(emoji)}`);
  }
}
