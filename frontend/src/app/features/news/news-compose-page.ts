import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSnackBar } from '@angular/material/snack-bar';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { MatDialog } from '@angular/material/dialog';
import { Observable, debounceTime, map } from 'rxjs';

import { NewsApi } from '../../core/api/news.api';
import { CurrentEventService } from '../../core/event/current-event.service';
import { NEWS_BODY_MAX, NEWS_TITLE_MAX, NewsPost } from '../../core/models/news';
import { ConfirmDialog, ConfirmDialogData } from '../../shared/confirm-dialog/confirm-dialog';
import { Markdown } from '../../shared/markdown/markdown';
import { HasUnsavedChanges } from '../../shared/unsaved-changes.guard';

interface Draft {
  title: string;
  body: string;
}

const draftKey = (eventId: string) => `amsterfam.news-draft.${eventId}`;

// Browser storage can be missing or blocked; a lost draft is a nuisance, not an error.
function readDraft(eventId: string): Draft | null {
  try {
    const raw = localStorage.getItem(draftKey(eventId));
    return raw ? (JSON.parse(raw) as Draft) : null;
  } catch {
    return null;
  }
}

function writeDraft(eventId: string, draft: Draft | null): void {
  try {
    if (draft && (draft.title.trim() || draft.body.trim())) {
      localStorage.setItem(draftKey(eventId), JSON.stringify(draft));
    } else {
      localStorage.removeItem(draftKey(eventId));
    }
  } catch {
    // Ignored, see readDraft.
  }
}

/**
 * Writes a new news post (`news/new`) or edits one (`news/:postId/edit`). A page rather than
 * a dialog, so there's room to write and a stray click can't throw a draft away. A new post's
 * draft is also kept in the browser until it's published or discarded.
 */
@Component({
  selector: 'app-news-compose-page',
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatButtonToggleModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    RouterLink,
    Markdown,
  ],
  templateUrl: './news-compose-page.html',
  styleUrl: './news-compose-page.scss',
  host: { '(window:beforeunload)': 'onBeforeUnload($event)' },
})
export class NewsComposePage implements OnInit, HasUnsavedChanges {
  private readonly api = inject(NewsApi);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly snackBar = inject(MatSnackBar);
  private readonly dialog = inject(MatDialog);
  private readonly currentEventService = inject(CurrentEventService);

  readonly titleMax = NEWS_TITLE_MAX;
  readonly bodyMax = NEWS_BODY_MAX;

  readonly eventId = this.currentEventService.eventId() ?? '';
  /** Null when writing a new post. */
  readonly postId: number | null = this.route.snapshot.paramMap.has('postId')
    ? Number(this.route.snapshot.paramMap.get('postId'))
    : null;

  readonly form = new FormGroup({
    title: new FormControl('', {
      nonNullable: true,
      validators: [Validators.maxLength(NEWS_TITLE_MAX)],
    }),
    body: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(NEWS_BODY_MAX)],
    }),
  });

  private readonly value = toSignal(this.form.valueChanges, { initialValue: this.form.value });
  readonly bodyText = computed(() => this.value().body ?? '');
  readonly titleLength = computed(() => (this.value().title ?? '').length);

  /** On narrow screens, which of the two panes shows. */
  readonly mode = signal<'write' | 'preview'>('write');
  readonly loading = signal(false);
  readonly loadFailed = signal(false);
  readonly saving = signal(false);
  readonly restoredDraft = signal(false);

  private saved = false;

  constructor() {
    if (this.postId === null) {
      this.form.valueChanges.pipe(debounceTime(500), takeUntilDestroyed()).subscribe((value) => {
        if (!this.saved) {
          writeDraft(this.eventId, { title: value.title ?? '', body: value.body ?? '' });
        }
      });
    }
  }

  ngOnInit(): void {
    if (this.postId === null) {
      const draft = readDraft(this.eventId);
      if (draft) {
        this.form.setValue({ title: draft.title ?? '', body: draft.body ?? '' });
        this.form.markAsDirty();
        this.restoredDraft.set(true);
      }
      return;
    }

    this.loading.set(true);
    this.api.get(this.eventId, this.postId).subscribe({
      next: (post) => {
        this.form.setValue({ title: post.title ?? '', body: post.body });
        this.form.markAsPristine();
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.loadFailed.set(true);
      },
    });
  }

  hasUnsavedChanges(): boolean {
    return !this.saved && this.form.dirty;
  }

  canLeave(): boolean | Observable<boolean> {
    if (!this.hasUnsavedChanges()) {
      return true;
    }
    return this.dialog
      .open<ConfirmDialog, ConfirmDialogData, boolean>(ConfirmDialog, {
        data: {
          title: 'Discard changes?',
          message: "You haven't saved yet. Leaving this page throws your changes away.",
          confirmLabel: 'Discard',
          dismissLabel: 'Keep editing',
        },
      })
      .afterClosed()
      .pipe(
        map((discard) => {
          if (discard === true) {
            this.discardChanges();
          }
          return discard === true;
        }),
      );
  }

  private discardChanges(): void {
    this.saved = true;
    if (this.postId === null) {
      writeDraft(this.eventId, null);
    }
  }

  onBeforeUnload(event: BeforeUnloadEvent): void {
    if (this.hasUnsavedChanges()) {
      event.preventDefault();
    }
  }

  discardDraft(): void {
    writeDraft(this.eventId, null);
    this.form.reset({ title: '', body: '' });
    this.restoredDraft.set(false);
  }

  save(): void {
    if (this.form.invalid || this.saving()) {
      this.form.markAllAsTouched();
      return;
    }
    const { title, body } = this.form.getRawValue();
    const request = { title: title.trim() || null, body };
    this.saving.set(true);

    const save$ =
      this.postId === null
        ? this.api.create(this.eventId, request)
        : this.api.update(this.eventId, this.postId, request);
    save$.subscribe({
      next: (post: NewsPost) => {
        this.saved = true;
        if (this.postId === null) {
          writeDraft(this.eventId, null);
        }
        void this.router.navigate(['/events', this.eventId, 'news', post.id]);
      },
      error: () => {
        this.saving.set(false);
        this.snackBar.open(
          this.postId === null ? 'Could not publish the post.' : 'Could not save the post.',
          'Dismiss',
          { duration: 5000 },
        );
      },
    });
  }
}
