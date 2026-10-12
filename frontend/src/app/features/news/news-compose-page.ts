import { NgTemplateOutlet } from '@angular/common';
import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSnackBar } from '@angular/material/snack-bar';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { MatDialog } from '@angular/material/dialog';
import { Observable, catchError, debounceTime, map, of, switchMap } from 'rxjs';

import { NewsApi } from '../../core/api/news.api';
import { CurrentEventService } from '../../core/event/current-event.service';
import {
  NEWS_BODY_MAX,
  NEWS_TITLE_MAX,
  NewsPost,
  UpsertNewsPostRequest,
} from '../../core/models/news';
import { AuthImage } from '../../shared/auth-image/auth-image';
import { ConfirmDialog, ConfirmDialogData } from '../../shared/confirm-dialog/confirm-dialog';
import { prepareImage } from '../../shared/image-upload';
import { Markdown } from '../../shared/markdown/markdown';
import { HasUnsavedChanges } from '../../shared/unsaved-changes.guard';

interface Draft {
  title: string;
  body: string;
  requiresAck?: boolean;
  pin?: boolean;
  commentsEnabled?: boolean;
}

/** What saving does to the post's image. */
type ImageChange =
  | { kind: 'keep' }
  | { kind: 'set'; image: Blob; fileName: string; previewUrl: string }
  | { kind: 'remove' };

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
 * draft is also kept in the browser until it's published or discarded (without the image,
 * which only lives until the page is left).
 */
@Component({
  selector: 'app-news-compose-page',
  imports: [
    NgTemplateOutlet,
    ReactiveFormsModule,
    MatButtonModule,
    MatButtonToggleModule,
    MatCheckboxModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    RouterLink,
    AuthImage,
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
    requiresAck: new FormControl(false, { nonNullable: true }),
    /** New posts only; existing posts are pinned from their menu. */
    pin: new FormControl(false, { nonNullable: true }),
    /** Edits only: ask everyone to acknowledge again. */
    resetAcks: new FormControl(false, { nonNullable: true }),
    commentsEnabled: new FormControl(true, { nonNullable: true }),
  });

  /** Whether the post being edited already asked for acknowledgement when it was loaded. */
  readonly hadAck = signal(false);
  readonly requiresAck = computed(() => this.value().requiresAck ?? false);

  private readonly value = toSignal(this.form.valueChanges, { initialValue: this.form.value });
  readonly bodyText = computed(() => this.value().body ?? '');
  readonly titleLength = computed(() => (this.value().title ?? '').length);

  /** On narrow screens, which of the two panes shows. */
  readonly mode = signal<'write' | 'preview'>('write');
  readonly loading = signal(false);
  readonly loadFailed = signal(false);
  readonly saving = signal(false);
  readonly restoredDraft = signal(false);

  /** The image the post had when loaded for editing. */
  readonly existingImageId = signal<string | null>(null);
  readonly imageChange = signal<ImageChange>({ kind: 'keep' });
  readonly imageBusy = signal(false);
  /** A freshly picked image (not uploaded yet) as an object URL. */
  readonly newImageUrl = computed(() => {
    const change = this.imageChange();
    return change.kind === 'set' ? change.previewUrl : null;
  });
  /** The stored image, while it's being kept. */
  readonly keptImageId = computed(() =>
    this.imageChange().kind === 'keep' ? this.existingImageId() : null,
  );
  readonly hasImage = computed(() => !!this.newImageUrl() || !!this.keptImageId());
  readonly existingImageUrl = computed(() =>
    this.postId === null ? '' : this.api.imageUrl(this.eventId, this.postId),
  );

  private saved = false;

  constructor() {
    inject(DestroyRef).onDestroy(() => this.revokePreview());

    if (this.postId === null) {
      this.form.valueChanges.pipe(debounceTime(500), takeUntilDestroyed()).subscribe((value) => {
        if (!this.saved) {
          writeDraft(this.eventId, {
            title: value.title ?? '',
            body: value.body ?? '',
            requiresAck: value.requiresAck ?? false,
            pin: value.pin ?? false,
            commentsEnabled: value.commentsEnabled ?? true,
          });
        }
      });
    }
  }

  ngOnInit(): void {
    if (this.postId === null) {
      const draft = readDraft(this.eventId);
      if (draft) {
        this.form.patchValue({
          title: draft.title ?? '',
          body: draft.body ?? '',
          requiresAck: draft.requiresAck ?? false,
          pin: draft.pin ?? false,
          commentsEnabled: draft.commentsEnabled ?? true,
        });
        this.form.markAsDirty();
        this.restoredDraft.set(true);
      }
      return;
    }

    this.loading.set(true);
    this.api.get(this.eventId, this.postId).subscribe({
      next: (post) => {
        this.form.patchValue({
          title: post.title ?? '',
          body: post.body,
          requiresAck: post.requiresAck,
          commentsEnabled: post.commentsEnabled,
        });
        this.hadAck.set(post.requiresAck);
        this.existingImageId.set(post.imageFileId ?? null);
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
    return !this.saved && (this.form.dirty || this.imageChange().kind !== 'keep');
  }

  onImageSelected(input: Event): void {
    const el = input.target as HTMLInputElement;
    const file = el.files?.[0];
    // Clear so picking the same file again after a failure still fires (change).
    el.value = '';
    if (!file) {
      return;
    }
    this.imageBusy.set(true);
    prepareImage(file)
      .then((image) => {
        this.imageBusy.set(false);
        this.setImageChange({
          kind: 'set',
          image,
          fileName: file.name,
          previewUrl: URL.createObjectURL(image),
        });
      })
      .catch(() => {
        this.imageBusy.set(false);
        this.snackBar.open('That file could not be read as an image.', 'Dismiss', {
          duration: 5000,
        });
      });
  }

  removeImage(): void {
    this.setImageChange(this.existingImageId() ? { kind: 'remove' } : { kind: 'keep' });
  }

  private setImageChange(change: ImageChange): void {
    this.revokePreview();
    this.imageChange.set(change);
  }

  private revokePreview(): void {
    const url = this.newImageUrl();
    if (url) {
      URL.revokeObjectURL(url);
    }
  }

  /**
   * Applies the image change to the saved post. A new post's first image isn't an edit.
   * Failing here doesn't undo the post, so the error only becomes a warning.
   */
  private saveImage(post: NewsPost): Observable<NewsPost> {
    const change = this.imageChange();
    let request: Observable<unknown> | null = null;
    if (change.kind === 'set') {
      request = this.api.uploadImage(
        this.eventId,
        post.id,
        change.image,
        change.fileName,
        this.postId === null,
      );
    } else if (change.kind === 'remove') {
      request = this.api.deleteImage(this.eventId, post.id);
    }
    if (!request) {
      return of(post);
    }
    return request.pipe(
      map(() => post),
      catchError(() => {
        this.snackBar.open(
          change.kind === 'set'
            ? "The post is saved, but the image couldn't be uploaded."
            : "The post is saved, but the image couldn't be removed.",
          'Dismiss',
          { duration: 8000 },
        );
        return of(post);
      }),
    );
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
    this.form.reset();
    this.setImageChange({ kind: 'keep' });
    this.restoredDraft.set(false);
  }

  save(): void {
    if (this.form.invalid || this.saving()) {
      this.form.markAllAsTouched();
      return;
    }
    const { title, body, requiresAck, pin, resetAcks, commentsEnabled } = this.form.getRawValue();
    const request: UpsertNewsPostRequest =
      this.postId === null
        ? { title: title.trim() || null, body, requiresAck, pin, commentsEnabled }
        : {
            title: title.trim() || null,
            body,
            requiresAck,
            commentsEnabled,
            resetAcks: requiresAck && this.hadAck() && resetAcks,
          };
    this.saving.set(true);

    const save$ =
      this.postId === null
        ? this.api.create(this.eventId, request)
        : this.api.update(this.eventId, this.postId, request);
    save$.pipe(switchMap((post) => this.saveImage(post))).subscribe({
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
