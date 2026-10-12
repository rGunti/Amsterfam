import { Component, computed, inject, input, linkedSignal, output, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { RouterLink } from '@angular/router';

import { NewsApi } from '../../core/api/news.api';
import { NEWS_REACTIONS, NewsPost } from '../../core/models/news';
import { AuthImage } from '../../shared/auth-image/auth-image';
import { ConfirmDialog, ConfirmDialogData } from '../../shared/confirm-dialog/confirm-dialog';
import { Markdown } from '../../shared/markdown/markdown';
import { fullTimestamp, timelineGroup } from '../../shared/timestamp';
import { NewsAcksDialog, NewsAcksDialogData } from './news-acks-dialog';
import { NewsComments } from './news-comments';
import { toggleReaction } from './reactions';

/**
 * One news post: author, time, title and the Markdown body, with reactions and a "Got it"
 * button, plus edit/pin/delete for organisers.
 */
@Component({
  selector: 'app-news-post-card',
  imports: [
    MatButtonModule,
    MatCardModule,
    MatIconModule,
    MatMenuModule,
    MatTooltipModule,
    RouterLink,
    AuthImage,
    Markdown,
    NewsComments,
  ],
  templateUrl: './news-post-card.html',
  styleUrl: './news-post-card.scss',
})
export class NewsPostCard {
  private readonly api = inject(NewsApi);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);

  readonly eventId = input.required<string>();
  readonly post = input.required<NewsPost>();
  /** Published since the viewer last opened the feed. */
  readonly isNew = input(false);
  /** Links the title to the post's own page (in the feed, not on that page itself). */
  readonly linkTitle = input(true);
  /** A short version for the event overview: clamped body, no reactions or comments. */
  readonly compact = input(false);
  /** Show the comments open, e.g. on the post's own page. */
  readonly commentsExpanded = input(false);

  readonly deleted = output<NewsPost>();
  /** The post changed here (pin, reaction, acknowledgement, comments). */
  readonly changed = output<NewsPost>();

  protected readonly reactionChoices = NEWS_REACTIONS;
  protected readonly deleting = signal(false);
  protected readonly busy = signal(false);
  /** The post as shown, updated locally after the viewer's own changes. */
  protected readonly current = linkedSignal(() => this.post());

  protected readonly counts = computed(() => {
    const counts = new Map(this.current().reactions.map((r) => [r.emoji, r.count]));
    const mine = new Set(this.current().myReactions);
    return NEWS_REACTIONS.map((emoji) => ({
      emoji,
      count: counts.get(emoji) ?? 0,
      mine: mine.has(emoji),
    }));
  });

  protected readonly when = computed(() => {
    const iso = this.current().publishedAt ?? this.current().createdAt;
    const { label, stamp } = timelineGroup(iso);
    const day = label === 'Today' || label === 'Yesterday' ? `${label}, ` : '';
    return { iso, text: `${day}${stamp}`, full: fullTimestamp(iso) };
  });

  protected readonly editedFull = computed(() => {
    const edited = this.current().editedAt;
    return edited ? fullTimestamp(edited) : null;
  });

  protected readonly imageUrl = computed(() =>
    this.api.imageUrl(this.eventId(), this.current().id),
  );

  protected readonly initial = computed(
    () => [...this.current().author.displayName.trim()][0]?.toUpperCase() ?? '?',
  );

  protected confirmDelete(): void {
    const post = this.current();
    this.dialog
      .open<ConfirmDialog, ConfirmDialogData, boolean>(ConfirmDialog, {
        data: {
          title: 'Delete this post?',
          message: post.title
            ? `“${post.title}” will be removed for everyone.`
            : 'The post will be removed for everyone.',
          confirmLabel: 'Delete',
        },
      })
      .afterClosed()
      .subscribe((confirmed) => {
        if (!confirmed) {
          return;
        }
        this.deleting.set(true);
        this.api.delete(this.eventId(), post.id).subscribe({
          next: () => this.deleted.emit(post),
          error: () => {
            this.deleting.set(false);
            this.snackBar.open('Could not delete the post.', 'Dismiss', { duration: 5000 });
          },
        });
      });
  }

  protected toggleReaction(emoji: string): void {
    const before = this.current();
    const mine = before.myReactions.includes(emoji);
    this.update(toggleReaction(before, emoji));

    const request = mine
      ? this.api.unreact(this.eventId(), before.id, emoji)
      : this.api.react(this.eventId(), before.id, emoji);
    request.subscribe({
      error: () => {
        this.update(before);
        this.snackBar.open('Could not save your reaction.', 'Dismiss', { duration: 5000 });
      },
    });
  }

  protected acknowledge(): void {
    const post = this.current();
    this.busy.set(true);
    this.api.acknowledge(this.eventId(), post.id).subscribe({
      next: () => {
        this.busy.set(false);
        this.update({
          ...post,
          acknowledgedByMe: true,
          ackCount: post.ackCount === null ? null : post.ackCount + 1,
        });
      },
      error: () => {
        this.busy.set(false);
        this.snackBar.open('Could not save that. Try again?', 'Dismiss', { duration: 5000 });
      },
    });
  }

  protected togglePin(): void {
    const post = this.current();
    const pinned = post.pinnedAt !== null;
    this.busy.set(true);
    const request = pinned
      ? this.api.unpin(this.eventId(), post.id)
      : this.api.pin(this.eventId(), post.id);
    request.subscribe({
      next: () => {
        this.busy.set(false);
        this.update({ ...post, pinnedAt: pinned ? null : new Date().toISOString() });
      },
      error: () => {
        this.busy.set(false);
        this.snackBar.open(
          pinned ? 'Could not unpin the post.' : 'Could not pin the post.',
          'Dismiss',
          {
            duration: 5000,
          },
        );
      },
    });
  }

  protected openAcks(): void {
    const post = this.current();
    this.dialog.open<NewsAcksDialog, NewsAcksDialogData>(NewsAcksDialog, {
      data: { eventId: this.eventId(), postId: post.id, title: post.title },
    });
  }

  protected onCommentCount(commentCount: number): void {
    this.update({ ...this.current(), commentCount, newCommentCount: 0 });
  }

  private update(post: NewsPost): void {
    this.current.set(post);
    this.changed.emit(post);
  }
}
