import { Component, computed, inject, input, output, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { RouterLink } from '@angular/router';

import { NewsApi } from '../../core/api/news.api';
import { NewsPost } from '../../core/models/news';
import { ConfirmDialog, ConfirmDialogData } from '../../shared/confirm-dialog/confirm-dialog';
import { Markdown } from '../../shared/markdown/markdown';
import { fullTimestamp, timelineGroup } from '../../shared/timestamp';

/** One news post: author, time, title and the Markdown body, plus edit/delete for organisers. */
@Component({
  selector: 'app-news-post-card',
  imports: [
    MatButtonModule,
    MatCardModule,
    MatIconModule,
    MatMenuModule,
    MatTooltipModule,
    RouterLink,
    Markdown,
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

  readonly deleted = output<NewsPost>();

  protected readonly deleting = signal(false);

  protected readonly when = computed(() => {
    const iso = this.post().publishedAt ?? this.post().createdAt;
    const { label, stamp } = timelineGroup(iso);
    const day = label === 'Today' || label === 'Yesterday' ? `${label}, ` : '';
    return { iso, text: `${day}${stamp}`, full: fullTimestamp(iso) };
  });

  protected readonly editedFull = computed(() => {
    const edited = this.post().editedAt;
    return edited ? fullTimestamp(edited) : null;
  });

  protected readonly initial = computed(
    () => [...this.post().author.displayName.trim()][0]?.toUpperCase() ?? '?',
  );

  protected confirmDelete(): void {
    const post = this.post();
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
}
