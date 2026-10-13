import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { firstValueFrom, isObservable, of, throwError } from 'rxjs';
import { vi } from 'vitest';

import { NewsApi } from '../../core/api/news.api';
import { CurrentEventService } from '../../core/event/current-event.service';
import { NewsComposePage } from './news-compose-page';

const DRAFT_KEY = 'amsterfam.news-draft.ev-1';

describe('NewsComposePage', () => {
  let dialogAnswer: boolean;
  let editedRequiresAck: boolean;
  let create: ReturnType<typeof vi.fn>;
  let update: ReturnType<typeof vi.fn>;
  let uploadImage: ReturnType<typeof vi.fn>;
  let deleteImage: ReturnType<typeof vi.fn>;
  let editedImageId: string | null;
  let editedPublishAt: string;
  let editedPublishedAt: string | null;

  function render(params: Record<string, string> = {}) {
    dialogAnswer = false;
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: convertToParamMap(params) } },
        },
        {
          provide: NewsApi,
          useValue: {
            get: () =>
              of({
                id: 5,
                title: 'Old',
                body: 'Old body',
                author: {},
                canEdit: true,
                requiresAck: editedRequiresAck,
                commentsEnabled: false,
                notify: true,
                imageFileId: editedImageId,
                publishAt: editedPublishAt,
                publishedAt: editedPublishedAt,
              }),
            create,
            update,
            uploadImage,
            deleteImage,
            imageUrl: () => 'http://api/image',
          },
        },
        { provide: CurrentEventService, useValue: { eventId: signal('ev-1') } },
        {
          provide: MatDialog,
          useValue: { open: () => ({ afterClosed: () => of(dialogAnswer) }) },
        },
      ],
    });
    // Saving navigates to the post, which has no route here.
    vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    const fixture = TestBed.createComponent(NewsComposePage);
    fixture.detectChanges();
    return fixture;
  }

  async function leave(page: NewsComposePage): Promise<boolean> {
    const answer = page.canLeave();
    return isObservable(answer) ? firstValueFrom(answer) : answer;
  }

  // Node's own (flag-gated) localStorage shadows jsdom's here, so use a plain in-memory one.
  beforeEach(() => {
    editedRequiresAck = false;
    editedImageId = null;
    editedPublishAt = '2030-01-01T10:00:00Z';
    editedPublishedAt = '2030-01-01T10:00:00Z';
    uploadImage = vi.fn(() => of({ imageFileId: 'img-2' }));
    deleteImage = vi.fn(() => of(undefined));
    create = vi.fn(() => of({ id: 9 }));
    update = vi.fn(() => of({ id: 5 }));
    const store = new Map<string, string>();
    vi.stubGlobal('localStorage', {
      getItem: (key: string) => store.get(key) ?? null,
      setItem: (key: string, value: string) => store.set(key, value),
      removeItem: (key: string) => store.delete(key),
      clear: () => store.clear(),
    });
  });

  afterEach(() => vi.unstubAllGlobals());

  it('lets you leave an untouched page without asking', async () => {
    const page = render().componentInstance;
    expect(await leave(page)).toBe(true);
  });

  it('asks before leaving with unsaved text, and drops the draft when you discard', async () => {
    vi.useFakeTimers();
    const page = render().componentInstance;
    page.form.patchValue({ title: 'Hi', body: 'Bring towels' });
    page.form.markAsDirty();
    vi.advanceTimersByTime(600);
    vi.useRealTimers();
    expect(localStorage.getItem(DRAFT_KEY)).toContain('Bring towels');

    dialogAnswer = false;
    expect(await leave(page)).toBe(false);
    expect(localStorage.getItem(DRAFT_KEY)).not.toBeNull();

    dialogAnswer = true;
    expect(await leave(page)).toBe(true);
    expect(localStorage.getItem(DRAFT_KEY)).toBeNull();
  });

  it('picks up an unfinished draft for a new post', () => {
    localStorage.setItem(
      DRAFT_KEY,
      JSON.stringify({ title: 'T', body: 'Draft body', requiresAck: true, pin: true }),
    );
    const fixture = render();
    expect(fixture.componentInstance.form.getRawValue()).toMatchObject({
      title: 'T',
      body: 'Draft body',
      requiresAck: true,
      pin: true,
    });
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('unfinished draft');
  });

  it('loads the post when editing, without touching drafts', () => {
    localStorage.setItem(DRAFT_KEY, JSON.stringify({ title: 'T', body: 'Draft body' }));
    const page = render({ postId: '5' }).componentInstance;
    expect(page.form.getRawValue()).toMatchObject({ title: 'Old', body: 'Old body' });
    expect(page.hasUnsavedChanges()).toBe(false);
  });

  it('sends the acknowledgement and pin options with a new post', () => {
    const page = render().componentInstance;
    page.form.patchValue({ body: 'Pay the deposit', requiresAck: true, pin: true });
    page.save();
    expect(create).toHaveBeenCalledWith('ev-1', {
      title: null,
      body: 'Pay the deposit',
      requiresAck: true,
      pin: true,
      commentsEnabled: true,
      notify: true,
    });
  });

  it('notifies channels by default, and lets the organiser opt out', () => {
    const fixture = render();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Notify channels');
    const page = fixture.componentInstance;
    page.form.patchValue({ body: 'Quiet one', notify: false });
    page.save();
    expect(create).toHaveBeenCalledWith('ev-1', expect.objectContaining({ notify: false }));
  });

  it('hides the notify option once a post is out, since edits never notify', () => {
    const published = render({ postId: '5' });
    expect((published.nativeElement as HTMLElement).textContent).not.toContain('Notify channels');

    TestBed.resetTestingModule();
    editedPublishedAt = null;
    editedPublishAt = new Date(Date.now() + 2 * 60 * 60 * 1000).toISOString();
    const scheduled = render({ postId: '5' });
    expect((scheduled.nativeElement as HTMLElement).textContent).toContain('Notify channels');
  });

  it('lets the organiser turn comments off, and keeps that when editing', () => {
    const page = render().componentInstance;
    expect(page.form.getRawValue().commentsEnabled).toBe(true);
    page.form.patchValue({ body: 'Info only', commentsEnabled: false });
    page.save();
    expect(create).toHaveBeenCalledWith(
      'ev-1',
      expect.objectContaining({ commentsEnabled: false }),
    );

    TestBed.resetTestingModule();
    const fixture = render({ postId: '5' });
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Allow comments');
    expect(fixture.componentInstance.form.getRawValue().commentsEnabled).toBe(false);
  });

  it('only offers to ask again when editing a post that already asked', () => {
    const text = (params: Record<string, string>) =>
      (render(params).nativeElement as HTMLElement).textContent ?? '';

    const creating = text({});
    expect(creating).toContain('Pin to the top');
    expect(creating).not.toContain('acknowledge again');
    TestBed.resetTestingModule();
    expect(text({ postId: '5' })).not.toContain('acknowledge again');
    TestBed.resetTestingModule();
    editedRequiresAck = true;
    const fixture = render({ postId: '5' });
    const el = fixture.nativeElement as HTMLElement;
    expect(el.textContent).toContain('acknowledge again');
    expect(el.textContent).not.toContain('Pin to the top');

    fixture.componentInstance.form.patchValue({ resetAcks: true });
    fixture.componentInstance.save();
    expect(update).toHaveBeenCalledWith('ev-1', 5, {
      title: 'Old',
      body: 'Old body',
      requiresAck: true,
      commentsEnabled: false,
      notify: true,
      resetAcks: true,
    });
  });

  describe('scheduling', () => {
    const inTwoHours = () => new Date(Date.now() + 2 * 60 * 60 * 1000);
    const localInput = (d: Date) =>
      new Date(d.getTime() - d.getTimezoneOffset() * 60_000).toISOString().slice(0, 16);

    it('publishes a new post now by default, without a time', () => {
      const fixture = render();
      const page = fixture.componentInstance;
      expect(page.submitLabel()).toBe('Publish');
      page.form.patchValue({ body: 'Now' });
      page.save();
      expect(create.mock.calls[0][1].publishAt).toBeUndefined();
    });

    it('sends the chosen time when scheduling, and goes back to the feed', () => {
      create = vi.fn(() => of({ id: 9, publishAt: '2030-01-01T10:00:00Z', publishedAt: null }));
      const fixture = render();
      const page = fixture.componentInstance;
      const at = inTwoHours();
      at.setSeconds(0, 0);
      page.form.patchValue({ body: 'Later', when: 'later', publishAt: localInput(at) });
      fixture.detectChanges();
      expect(page.submitLabel()).toBe('Schedule');
      expect(
        (fixture.nativeElement as HTMLElement).querySelector('input[type=datetime-local]'),
      ).not.toBeNull();

      page.save();
      expect(create).toHaveBeenCalledWith(
        'ev-1',
        expect.objectContaining({ publishAt: at.toISOString() }),
      );
      expect(TestBed.inject(Router).navigate).toHaveBeenCalledWith(['/events', 'ev-1', 'news']);
    });

    it("won't schedule without a time far enough ahead", () => {
      const page = render().componentInstance;
      page.form.patchValue({ body: 'Later', when: 'later' });
      expect(page.form.hasError('publishAtRequired')).toBe(true);
      page.form.patchValue({ publishAt: localInput(new Date()) });
      expect(page.form.hasError('publishAtTooSoon')).toBe(true);
      page.save();
      expect(create).not.toHaveBeenCalled();
    });

    it('lets a scheduled post be moved or sent out now', () => {
      editedPublishAt = inTwoHours().toISOString();
      editedPublishedAt = null;
      const page = render({ postId: '5' }).componentInstance;
      expect(page.form.getRawValue().when).toBe('later');
      expect(page.submitLabel()).toBe('Save');

      page.form.patchValue({ when: 'now' });
      expect(page.submitLabel()).toBe('Publish now');
      page.save();
      expect(typeof update.mock.calls[0][2].publishAt).toBe('string');
    });

    it("doesn't offer scheduling for a published post", () => {
      const fixture = render({ postId: '5' });
      const page = fixture.componentInstance;
      expect(
        (fixture.nativeElement as HTMLElement).querySelector('[aria-label="When to publish"]'),
      ).toBeNull();
      page.save();
      expect(update.mock.calls[0][2].publishAt).toBeUndefined();
    });
  });

  const picked = () => ({
    kind: 'set' as const,
    image: new Blob(['jpeg']),
    fileName: 'beach.png',
    previewUrl: 'blob:preview',
  });

  describe('images', () => {
    beforeEach(() => {
      // jsdom has no object URLs.
      URL.createObjectURL ??= () => 'blob:preview';
      URL.revokeObjectURL ??= () => undefined;
    });

    it('uploads a picked image after creating the post, as its first image', () => {
      const page = render().componentInstance;
      page.form.patchValue({ body: 'Look at this' });
      page.imageChange.set(picked());
      expect(page.hasUnsavedChanges()).toBe(true);

      page.save();
      expect(create).toHaveBeenCalled();
      expect(uploadImage).toHaveBeenCalledWith('ev-1', 9, expect.any(Blob), 'beach.png', true);
      expect(TestBed.inject(Router).navigate).toHaveBeenCalledWith(['/events', 'ev-1', 'news', 9]);
    });

    it('replaces the image of an edited post as an edit', () => {
      editedImageId = 'img-1';
      const page = render({ postId: '5' }).componentInstance;
      expect(page.hasImage()).toBe(true);
      page.imageChange.set(picked());

      page.save();
      expect(uploadImage).toHaveBeenCalledWith('ev-1', 5, expect.any(Blob), 'beach.png', false);
    });

    it('removes the image of an edited post', () => {
      editedImageId = 'img-1';
      const page = render({ postId: '5' }).componentInstance;
      page.removeImage();
      expect(page.hasImage()).toBe(false);
      expect(page.hasUnsavedChanges()).toBe(true);

      page.save();
      expect(deleteImage).toHaveBeenCalledWith('ev-1', 5);
      expect(uploadImage).not.toHaveBeenCalled();
    });

    it('leaves the image alone when it was not touched', () => {
      editedImageId = 'img-1';
      const page = render({ postId: '5' }).componentInstance;
      page.save();
      expect(uploadImage).not.toHaveBeenCalled();
      expect(deleteImage).not.toHaveBeenCalled();
    });

    it('still opens the saved post when the image upload fails', () => {
      uploadImage = vi.fn(() => throwError(() => new Error('nope')));
      const page = render().componentInstance;
      page.form.patchValue({ body: 'Look at this' });
      page.imageChange.set(picked());

      page.save();
      expect(TestBed.inject(Router).navigate).toHaveBeenCalledWith(['/events', 'ev-1', 'news', 9]);
    });
  });
});
