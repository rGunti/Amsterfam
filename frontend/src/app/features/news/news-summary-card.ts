import { Component, OnInit, inject, input, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { RouterLink } from '@angular/router';

import { NewsApi } from '../../core/api/news.api';
import { NewsPost } from '../../core/models/news';
import { NewsPostCard } from './news-post-card';

const MAX_POSTS = 3;

/**
 * "Latest news" on the event overview: pinned posts, then posts still waiting for the
 * viewer's "Got it", then the newest post. Hidden while there's nothing to show.
 */
@Component({
  selector: 'app-news-summary-card',
  imports: [MatButtonModule, MatCardModule, RouterLink, NewsPostCard],
  template: `
    @if (posts().length > 0) {
      <mat-card class="event-card">
        <mat-card-header>
          <mat-card-title>Latest news</mat-card-title>
        </mat-card-header>
        <mat-card-content>
          <ol class="posts">
            @for (post of posts(); track post.id) {
              <li>
                <app-news-post-card [eventId]="eventId()" [post]="post" [compact]="true" />
              </li>
            }
          </ol>
        </mat-card-content>
        <mat-card-actions>
          <a mat-button [routerLink]="['/events', eventId(), 'news']">All news</a>
        </mat-card-actions>
      </mat-card>
    }
  `,
  styles: `
    /* Matches the other cards on the event overview. */
    .event-card {
      max-width: 640px;
      margin: 24px auto;
    }

    .posts {
      list-style: none;
      margin: 0;
      padding: 0;
      display: flex;
      flex-direction: column;
      gap: 12px;
    }
  `,
})
export class NewsSummaryCard implements OnInit {
  private readonly api = inject(NewsApi);

  readonly eventId = input.required<string>();

  protected readonly posts = signal<NewsPost[]>([]);

  ngOnInit(): void {
    this.api.summary(this.eventId()).subscribe({
      next: (summary) => {
        const all = [
          ...summary.pinned,
          ...summary.needsAck,
          ...(summary.latest ? [summary.latest] : []),
        ];
        const unique = all.filter((p, i) => all.findIndex((q) => q.id === p.id) === i);
        this.posts.set(unique.slice(0, MAX_POSTS));
      },
      // The overview works without it; the News page shows its own error.
      error: () => this.posts.set([]),
    });
  }
}
