import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { RouterLink } from '@angular/router';

import { CurrentUserService } from '../../core/api/current-user.service';
import { NewsApi } from '../../core/api/news.api';
import { CurrentEventService } from '../../core/event/current-event.service';
import { NewsPost } from '../../core/models/news';
import { canPostNews } from '../../shared/event-status';
import { NewsPostCard } from './news-post-card';

const PAGE_SIZE = 20;

/** The event's news feed, newest first. Opening it marks everything as read. */
@Component({
  selector: 'app-news-page',
  imports: [MatButtonModule, MatIconModule, RouterLink, NewsPostCard],
  templateUrl: './news-page.html',
  styleUrl: './news-page.scss',
})
export class NewsPage implements OnInit {
  private readonly api = inject(NewsApi);
  private readonly currentEventService = inject(CurrentEventService);
  private readonly currentUser = inject(CurrentUserService);

  readonly event = this.currentEventService.event;
  readonly posts = signal<NewsPost[]>([]);
  /** When the viewer last opened the feed before this visit; later posts get a "New" mark. */
  readonly seenAt = signal<string | null>(null);
  readonly loading = signal(true);
  readonly error = signal(false);
  readonly hasMore = signal(false);

  readonly canPost = computed(() => {
    const ev = this.event();
    return ev !== null && canPostNews(ev);
  });

  ngOnInit(): void {
    this.load();
  }

  loadMore(): void {
    this.load(this.posts().at(-1));
  }

  /** Someone else's post, published since the viewer last opened the feed. */
  isNew(post: NewsPost): boolean {
    const seenAt = this.seenAt();
    return (
      post.publishedAt !== null &&
      post.author.id !== this.currentUser.user()?.id &&
      (seenAt === null || new Date(post.publishedAt) > new Date(seenAt))
    );
  }

  onDeleted(post: NewsPost): void {
    this.posts.update((posts) => posts.filter((p) => p.id !== post.id));
  }

  private load(before?: NewsPost): void {
    const ev = this.event();
    if (!ev) {
      return;
    }
    this.loading.set(true);
    this.error.set(false);
    this.api.list(ev.id, before, PAGE_SIZE).subscribe({
      next: (feed) => {
        if (before === undefined) {
          this.posts.set(feed.posts);
          this.seenAt.set(feed.seenAt);
          this.markSeen(ev.id);
        } else {
          this.posts.update((current) => [...current, ...feed.posts]);
        }
        this.hasMore.set(feed.posts.length === PAGE_SIZE);
        this.loading.set(false);
      },
      error: () => {
        this.error.set(true);
        this.loading.set(false);
      },
    });
  }

  private markSeen(eventId: string): void {
    this.api.markSeen(eventId).subscribe({
      next: () => this.currentEventService.setUnreadNews(0),
      // Not worth bothering anyone about; the badge just stays until the next visit.
      error: () => undefined,
    });
  }
}
