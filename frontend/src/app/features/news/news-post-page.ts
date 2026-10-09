import { Component, OnInit, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';

import { NewsApi } from '../../core/api/news.api';
import { CurrentEventService } from '../../core/event/current-event.service';
import { NewsPost } from '../../core/models/news';
import { NewsPostCard } from './news-post-card';

/** A single news post, e.g. from the timeline's "Read more". */
@Component({
  selector: 'app-news-post-page',
  imports: [MatButtonModule, MatIconModule, RouterLink, NewsPostCard],
  template: `
    <div class="news-post">
      <a mat-button class="back" [routerLink]="['/events', eventId(), 'news']">
        <mat-icon>arrow_back</mat-icon>
        All news
      </a>
      @if (post(); as post) {
        <app-news-post-card
          [eventId]="eventId()"
          [post]="post"
          [linkTitle]="false"
          (deleted)="onDeleted()"
        />
      } @else if (missing()) {
        <p class="note">This post doesn't exist (any more).</p>
      } @else {
        <p class="note">Loading…</p>
      }
    </div>
  `,
  styles: `
    .news-post {
      max-width: 640px;
      margin: 24px auto;
    }
    .back {
      margin: 0 0 8px -12px;
    }
    .note {
      color: var(--mat-sys-on-surface-variant);
    }
  `,
})
export class NewsPostPage implements OnInit {
  private readonly api = inject(NewsApi);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly currentEventService = inject(CurrentEventService);

  readonly eventId = () => this.currentEventService.eventId() ?? '';
  readonly post = signal<NewsPost | null>(null);
  readonly missing = signal(false);

  ngOnInit(): void {
    this.api.get(this.eventId(), Number(this.route.snapshot.paramMap.get('postId'))).subscribe({
      next: (post) => this.post.set(post),
      error: () => this.missing.set(true),
    });
  }

  onDeleted(): void {
    void this.router.navigate(['/events', this.eventId(), 'news']);
  }
}
