import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { of } from 'rxjs';
import { vi } from 'vitest';

import { CurrentUserService } from '../../core/api/current-user.service';
import { NewsApi } from '../../core/api/news.api';
import { CurrentEventService } from '../../core/event/current-event.service';
import { EventResponse } from '../../core/models/event';
import { NewsComment, NewsPost } from '../../core/models/news';
import { NewsComments } from './news-comments';

const post: NewsPost = {
  id: 7,
  title: 'Deposit due',
  body: 'Pay by Friday.',
  author: { id: 1, displayName: 'Alice', avatarUrl: null },
  createdAt: '2030-01-01T10:00:00Z',
  publishedAt: '2030-01-01T10:00:00Z',
  editedAt: null,
  canEdit: false,
  pinnedAt: null,
  requiresAck: false,
  canAcknowledge: false,
  acknowledgedByMe: false,
  reactions: [],
  myReactions: [],
  ackCount: null,
  ackTotal: null,
  imageFileId: null,
  commentsEnabled: true,
  commentCount: 2,
  newCommentCount: 1,
};

const comment = (overrides: Partial<NewsComment> = {}): NewsComment => ({
  id: 1,
  author: { id: 2, displayName: 'Bob', avatarUrl: null },
  body: 'Which day exactly?',
  createdAt: '2030-01-02T10:00:00Z',
  editedAt: null,
  canEdit: false,
  canDelete: false,
  ...overrides,
});

describe('NewsComments', () => {
  let api: Record<string, ReturnType<typeof vi.fn>>;
  let dialogAnswer: boolean;

  function render(
    overrides: Partial<NewsPost> = {},
    comments: NewsComment[] = [],
    status: EventResponse['status'] = 'Open',
  ) {
    api = {
      comments: vi.fn(() => of(comments)),
      addComment: vi.fn((_e: string, _p: number, body: string) =>
        of(comment({ id: 99, body, canEdit: true, canDelete: true })),
      ),
      editComment: vi.fn(),
      deleteComment: vi.fn(() => of(undefined)),
    };
    dialogAnswer = true;
    TestBed.configureTestingModule({
      providers: [
        { provide: NewsApi, useValue: api },
        {
          provide: CurrentUserService,
          useValue: {
            user: signal({ id: 3, handle: 'carol', displayName: 'Carol', avatarUrl: null }),
          },
        },
        {
          provide: CurrentEventService,
          useValue: { event: signal({ id: 'ev-1', status } as EventResponse) },
        },
        {
          provide: MatDialog,
          useValue: { open: () => ({ afterClosed: () => of(dialogAnswer) }) },
        },
      ],
    });
    const fixture = TestBed.createComponent(NewsComments);
    fixture.componentRef.setInput('eventId', 'ev-1');
    fixture.componentRef.setInput('post', { ...post, ...overrides });
    fixture.detectChanges();
    return fixture;
  }

  const el = (fixture: { nativeElement: unknown }) => fixture.nativeElement as HTMLElement;
  const toggle = (fixture: { nativeElement: unknown; detectChanges(): void }) => {
    el(fixture).querySelector<HTMLButtonElement>('.comments-toggle')!.click();
    fixture.detectChanges();
  };

  it('shows the count and new comments, and loads only when opened', () => {
    const fixture = render({}, [comment(), comment({ id: 2, body: 'Friday.' })]);
    expect(el(fixture).textContent).toContain('2 comments');
    expect(el(fixture).textContent).toContain('1 new');
    expect(api['comments']).not.toHaveBeenCalled();

    toggle(fixture);
    expect(api['comments']).toHaveBeenCalledWith('ev-1', 7, undefined);
    const items = el(fixture).querySelectorAll('.comment');
    expect(items.length).toBe(2);
    expect(items[0].textContent).toContain('Bob');
    expect(items[0].textContent).toContain('Which day exactly?');
    expect(el(fixture).textContent).not.toContain('1 new');
  });

  it('posts a comment and reports the new count', async () => {
    const fixture = render({ commentCount: 0, newCommentCount: 0 });
    const counts: number[] = [];
    fixture.componentInstance.countChanged.subscribe((n) => counts.push(n));
    expect(el(fixture).textContent).toContain('Comment');

    toggle(fixture);
    await fixture.whenStable();
    const textarea = el(fixture).querySelector<HTMLTextAreaElement>('.new-comment textarea')!;
    textarea.value = '  See you there  ';
    textarea.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    el(fixture).querySelector<HTMLButtonElement>('.new-comment button')!.click();
    fixture.detectChanges();

    expect(api['addComment']).toHaveBeenCalledWith('ev-1', 7, 'See you there');
    expect(el(fixture).querySelectorAll('.comment').length).toBe(1);
    expect(el(fixture).textContent).toContain('1 comment');
    expect(counts).toEqual([1]);
  });

  it('sends with Ctrl+Enter or Cmd+Enter, and shows the viewer next to the box', async () => {
    const fixture = render({ commentCount: 0, newCommentCount: 0 });
    toggle(fixture);
    await fixture.whenStable();
    const form = el(fixture).querySelector('.new-comment')!;
    expect(form.querySelector('.avatar')!.textContent).toContain('C');

    const textarea = form.querySelector<HTMLTextAreaElement>('textarea')!;
    textarea.value = 'First';
    textarea.dispatchEvent(new Event('input'));
    const plainEnter = new KeyboardEvent('keydown', { key: 'Enter', cancelable: true });
    textarea.dispatchEvent(plainEnter);
    expect(api['addComment']).not.toHaveBeenCalled();
    // A plain Enter must still type a new line.
    expect(plainEnter.defaultPrevented).toBe(false);

    textarea.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', ctrlKey: true }));
    expect(api['addComment']).toHaveBeenCalledWith('ev-1', 7, 'First');

    fixture.detectChanges();
    textarea.value = 'Second';
    textarea.dispatchEvent(new Event('input'));
    textarea.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', metaKey: true }));
    expect(api['addComment']).toHaveBeenCalledWith('ev-1', 7, 'Second');
  });

  it('only offers actions the viewer may take', () => {
    const fixture = render({}, [
      comment({ id: 1 }),
      comment({ id: 2, canEdit: true, canDelete: true }),
      comment({ id: 3, canDelete: true }),
    ]);
    toggle(fixture);
    const menus = Array.from(el(fixture).querySelectorAll('.comment')).map(
      (c) => c.querySelector('.comment-menu') !== null,
    );
    expect(menus).toEqual([false, true, true]);
  });

  it('deletes a comment after confirming', () => {
    const fixture = render({}, [comment({ canDelete: true })]);
    toggle(fixture);
    (fixture.componentInstance as unknown as { confirmDelete(c: NewsComment): void }).confirmDelete(
      comment({ canDelete: true }),
    );
    fixture.detectChanges();
    expect(api['deleteComment']).toHaveBeenCalledWith('ev-1', 7, 1);
    expect(el(fixture).querySelectorAll('.comment').length).toBe(0);
  });

  it('hides the box when comments are off or the event is read-only', () => {
    const off = render({ commentsEnabled: false }, [comment()]);
    toggle(off);
    expect(el(off).querySelector('.new-comment')).toBeNull();
    expect(el(off).textContent).toContain('Comments are turned off');
    expect(el(off).querySelectorAll('.comment').length).toBe(1);

    TestBed.resetTestingModule();
    const archived = render({}, [], 'Archived');
    toggle(archived);
    expect(el(archived).querySelector('.new-comment')).toBeNull();
  });
});
