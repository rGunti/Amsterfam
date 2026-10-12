import { DOCUMENT } from '@angular/common';
import {
  Component,
  DestroyRef,
  OnInit,
  computed,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatMenuModule } from '@angular/material/menu';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import {
  EMPTY,
  Observable,
  catchError,
  distinctUntilChanged,
  exhaustMap,
  expand,
  fromEvent,
  map,
  reduce,
  startWith,
  switchMap,
  timer,
} from 'rxjs';

import { CurrentUserService } from '../../core/api/current-user.service';
import { NewsApi } from '../../core/api/news.api';
import {
  CurrentEventService,
  EVENT_POLL_INTERVAL_MS,
} from '../../core/event/current-event.service';
import { NEWS_COMMENT_MAX, NewsComment, NewsPost } from '../../core/models/news';
import { ConfirmDialog, ConfirmDialogData } from '../../shared/confirm-dialog/confirm-dialog';
import { isReadOnly } from '../../shared/event-status';
import { LinkifiedText } from '../../shared/linkified-text/linkified-text';
import { fullTimestamp, timelineGroup } from '../../shared/timestamp';

/** Matches the server's default page size; a full page means there may be more. */
const PAGE_SIZE = 50;

/**
 * The comments under a news post: a collapsible, oldest-first list with a box to add one.
 * Authors edit and delete their own; organisers can delete any. While open, new comments
 * are fetched on the same interval as the event poll.
 */
@Component({
  selector: 'app-news-comments',
  imports: [
    FormsModule,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatMenuModule,
    MatTooltipModule,
    LinkifiedText,
  ],
  templateUrl: './news-comments.html',
  styleUrl: './news-comments.scss',
})
export class NewsComments implements OnInit {
  private readonly api = inject(NewsApi);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);
  private readonly document = inject(DOCUMENT);
  private readonly destroyRef = inject(DestroyRef);
  private readonly currentEventService = inject(CurrentEventService);
  private readonly navigator = this.document.defaultView?.navigator;

  readonly eventId = input.required<string>();
  readonly post = input.required<NewsPost>();
  /** Open straight away, e.g. on the post's own page. */
  readonly startExpanded = input(false);

  /** The comment count changed here (added, deleted or fetched). */
  readonly countChanged = output<number>();

  protected readonly maxLength = NEWS_COMMENT_MAX;
  protected readonly me = inject(CurrentUserService).user;
  protected readonly myInitial = computed(() => {
    const me = this.me();
    return this.initialOf(me?.displayName ?? me?.handle ?? '');
  });
  /**
   * Shown in the send button's tooltip. Ctrl+Enter and Cmd+Enter both send; a plain Enter
   * still makes a new line.
   */
  protected readonly shortcutLabel = /Mac|iPhone|iPad/.test(this.navigator?.platform ?? '')
    ? '⌘ Enter'
    : 'Ctrl+Enter';
  protected readonly expanded = signal(false);
  protected readonly comments = signal<NewsComment[] | null>(null);
  protected readonly loadFailed = signal(false);
  protected readonly draft = signal('');
  protected readonly sending = signal(false);
  protected readonly editingId = signal<number | null>(null);
  protected readonly editText = signal('');
  protected readonly busyId = signal<number | null>(null);

  /** Comments seen as new when the post was loaded; cleared once the list is opened. */
  private readonly newSeen = signal(false);

  protected readonly count = computed(() => this.comments()?.length ?? this.post().commentCount);
  protected readonly newCount = computed(() => (this.newSeen() ? 0 : this.post().newCommentCount));

  protected readonly canWrite = computed(() => {
    const ev = this.currentEventService.event();
    return this.post().commentsEnabled && (ev === null || !isReadOnly(ev.status));
  });

  protected readonly toggleLabel = computed(() => {
    const count = this.count();
    if (count === 0) {
      return this.post().commentsEnabled ? 'Comment' : 'No comments';
    }
    return count === 1 ? '1 comment' : `${count} comments`;
  });

  ngOnInit(): void {
    if (this.startExpanded()) {
      this.toggle();
    }
  }

  protected toggle(): void {
    const open = !this.expanded();
    this.expanded.set(open);
    if (open && this.comments() === null) {
      this.load();
    }
  }

  protected when(iso: string): { text: string; full: string } {
    const { label, stamp } = timelineGroup(iso);
    const day = label === 'Today' || label === 'Yesterday' ? `${label}, ` : '';
    return { text: `${day}${stamp}`, full: fullTimestamp(iso) };
  }

  protected initialOf(name: string): string {
    return [...name.trim()][0]?.toUpperCase() ?? '?';
  }

  protected send(): void {
    const body = this.draft().trim();
    if (!body || this.sending()) {
      return;
    }
    this.sending.set(true);
    this.api.addComment(this.eventId(), this.post().id, body).subscribe({
      next: (comment) => {
        this.sending.set(false);
        this.draft.set('');
        this.setComments([...(this.comments() ?? []), comment]);
      },
      error: () => {
        this.sending.set(false);
        this.snackBar.open('Could not post your comment.', 'Dismiss', { duration: 5000 });
      },
    });
  }

  protected startEdit(comment: NewsComment): void {
    this.editingId.set(comment.id);
    this.editText.set(comment.body);
  }

  protected cancelEdit(): void {
    this.editingId.set(null);
  }

  protected saveEdit(comment: NewsComment): void {
    const body = this.editText().trim();
    if (!body) {
      return;
    }
    if (body === comment.body) {
      this.editingId.set(null);
      return;
    }
    this.busyId.set(comment.id);
    this.api.editComment(this.eventId(), this.post().id, comment.id, body).subscribe({
      next: (saved) => {
        this.busyId.set(null);
        this.editingId.set(null);
        this.comments.update((list) => list?.map((c) => (c.id === saved.id ? saved : c)) ?? null);
      },
      error: () => {
        this.busyId.set(null);
        this.snackBar.open('Could not save your comment.', 'Dismiss', { duration: 5000 });
      },
    });
  }

  protected confirmDelete(comment: NewsComment): void {
    const own = comment.canEdit;
    this.dialog
      .open<ConfirmDialog, ConfirmDialogData, boolean>(ConfirmDialog, {
        data: {
          title: 'Delete this comment?',
          message: own
            ? 'Your comment will be removed for everyone.'
            : `${comment.author.displayName}’s comment will be removed for everyone. Organisers will see that you removed it.`,
          confirmLabel: 'Delete',
        },
      })
      .afterClosed()
      .subscribe((confirmed) => {
        if (!confirmed) {
          return;
        }
        this.busyId.set(comment.id);
        this.api.deleteComment(this.eventId(), this.post().id, comment.id).subscribe({
          next: () => {
            this.busyId.set(null);
            this.setComments((this.comments() ?? []).filter((c) => c.id !== comment.id));
          },
          error: () => {
            this.busyId.set(null);
            this.snackBar.open('Could not delete the comment.', 'Dismiss', { duration: 5000 });
          },
        });
      });
  }

  private load(): void {
    this.loadFailed.set(false);
    this.fetchAll().subscribe({
      next: (comments) => {
        this.newSeen.set(true);
        this.setComments(comments);
        this.poll();
      },
      error: () => this.loadFailed.set(true),
    });
  }

  /** Every comment after `after`, following pages until one comes back short. */
  private fetchAll(after?: NewsComment): Observable<NewsComment[]> {
    const page = (cursor?: NewsComment) =>
      this.api.comments(this.eventId(), this.post().id, cursor);
    return page(after).pipe(
      expand((comments) => (comments.length === PAGE_SIZE ? page(comments.at(-1)) : EMPTY)),
      reduce((all, comments) => [...all, ...comments], [] as NewsComment[]),
    );
  }

  /** Picks up other people's comments while the list is open and the tab is visible. */
  private poll(): void {
    fromEvent(this.document, 'visibilitychange')
      .pipe(
        startWith(null),
        map(() => this.document.visibilityState === 'visible'),
        distinctUntilChanged(),
        switchMap((visible) =>
          visible ? timer(EVENT_POLL_INTERVAL_MS, EVENT_POLL_INTERVAL_MS) : EMPTY,
        ),
        exhaustMap(() =>
          this.expanded()
            ? this.fetchAll(this.comments()?.at(-1)).pipe(catchError(() => EMPTY))
            : EMPTY,
        ),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((fresh) => {
        const known = new Set((this.comments() ?? []).map((c) => c.id));
        const added = fresh.filter((c) => !known.has(c.id));
        if (added.length > 0) {
          this.setComments([...(this.comments() ?? []), ...added]);
        }
      });
  }

  private setComments(comments: NewsComment[]): void {
    const before = this.count();
    this.comments.set(comments);
    if (comments.length !== before) {
      this.countChanged.emit(comments.length);
    }
  }
}
