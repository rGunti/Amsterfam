import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { vi } from 'vitest';

import { CurrentUserService } from '../../core/api/current-user.service';
import { NewsApi } from '../../core/api/news.api';
import { CurrentEventService } from '../../core/event/current-event.service';
import { EventResponse } from '../../core/models/event';
import { NewsFeed, NewsPost } from '../../core/models/news';
import { NewsPage } from './news-page';

const alice = { id: 1, displayName: 'Alice', avatarUrl: null };
const me = { id: 2, displayName: 'Me', avatarUrl: null };

const post = (id: number, publishedAt: string, author = alice): NewsPost => ({
  id,
  title: `Post ${id}`,
  body: `Body **${id}**`,
  author,
  createdAt: publishedAt,
  publishedAt,
  editedAt: null,
  canEdit: false,
});

describe('NewsPage', () => {
  const feed: NewsFeed = {
    posts: [
      post(3, '2030-01-03T10:00:00Z'),
      post(2, '2030-01-02T10:00:00Z', me),
      post(1, '2030-01-01T10:00:00Z'),
    ],
    seenAt: '2030-01-01T12:00:00Z',
  };
  let markSeen: ReturnType<typeof vi.fn>;
  let setUnreadNews: ReturnType<typeof vi.fn>;

  function render(role: EventResponse['currentUserRole'] = 'Attendee') {
    markSeen = vi.fn(() => of(undefined));
    setUnreadNews = vi.fn();
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: NewsApi, useValue: { list: () => of(feed), markSeen } },
        {
          provide: CurrentEventService,
          useValue: {
            event: signal({ id: 'ev-1', currentUserRole: role, status: 'Open' } as EventResponse),
            eventId: signal('ev-1'),
            setUnreadNews,
          },
        },
        { provide: CurrentUserService, useValue: { user: signal(me) } },
      ],
    });
    const fixture = TestBed.createComponent(NewsPage);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('lists the posts with their Markdown rendered', () => {
    const el = render();
    const titles = [...el.querySelectorAll('.post-title')].map((t) => t.textContent?.trim());
    expect(titles).toEqual(['Post 3', 'Post 2', 'Post 1']);
    expect(el.querySelector('.post-body strong')?.textContent).toBe('3');
  });

  it("marks others' posts since the last visit as new, then marks the feed read", () => {
    const el = render();
    const newPosts = [...el.querySelectorAll('.post-new .post-title')].map((t) =>
      t.textContent?.trim(),
    );
    // Post 2 is newer too, but the viewer wrote it.
    expect(newPosts).toEqual(['Post 3']);
    expect(markSeen).toHaveBeenCalledWith('ev-1');
    expect(setUnreadNews).toHaveBeenCalledWith(0);
  });

  it('offers organisers a new post button', () => {
    expect(render('Attendee').textContent).not.toContain('New post');
    TestBed.resetTestingModule();
    expect(render('Organiser').textContent).toContain('New post');
  });
});
