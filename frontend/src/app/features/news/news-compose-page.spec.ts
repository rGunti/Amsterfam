import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { firstValueFrom, isObservable, of } from 'rxjs';
import { vi } from 'vitest';

import { NewsApi } from '../../core/api/news.api';
import { CurrentEventService } from '../../core/event/current-event.service';
import { NewsComposePage } from './news-compose-page';

const DRAFT_KEY = 'amsterfam.news-draft.ev-1';

describe('NewsComposePage', () => {
  let dialogAnswer: boolean;

  function create(params: Record<string, string> = {}) {
    dialogAnswer = false;
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: convertToParamMap(params) } },
        },
        {
          provide: NewsApi,
          useValue: {
            get: () => of({ id: 5, title: 'Old', body: 'Old body', author: {}, canEdit: true }),
          },
        },
        { provide: CurrentEventService, useValue: { eventId: signal('ev-1') } },
        {
          provide: MatDialog,
          useValue: { open: () => ({ afterClosed: () => of(dialogAnswer) }) },
        },
      ],
    });
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
    const page = create().componentInstance;
    expect(await leave(page)).toBe(true);
  });

  it('asks before leaving with unsaved text, and drops the draft when you discard', async () => {
    vi.useFakeTimers();
    const page = create().componentInstance;
    page.form.setValue({ title: 'Hi', body: 'Bring towels' });
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
    localStorage.setItem(DRAFT_KEY, JSON.stringify({ title: 'T', body: 'Draft body' }));
    const fixture = create();
    expect(fixture.componentInstance.form.getRawValue()).toEqual({
      title: 'T',
      body: 'Draft body',
    });
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('unfinished draft');
  });

  it('loads the post when editing, without touching drafts', () => {
    localStorage.setItem(DRAFT_KEY, JSON.stringify({ title: 'T', body: 'Draft body' }));
    const page = create({ postId: '5' }).componentInstance;
    expect(page.form.getRawValue()).toEqual({ title: 'Old', body: 'Old body' });
    expect(page.hasUnsavedChanges()).toBe(false);
  });
});
