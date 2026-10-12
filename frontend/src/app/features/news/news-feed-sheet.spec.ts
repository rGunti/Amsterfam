import { TestBed } from '@angular/core/testing';
import { MAT_BOTTOM_SHEET_DATA } from '@angular/material/bottom-sheet';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { of } from 'rxjs';
import { vi } from 'vitest';

import { NewsApi } from '../../core/api/news.api';
import { NewsFeedTokenStatus } from '../../core/models/news';
import { NewsFeedSheet } from './news-feed-sheet';

const feedUrl = 'https://app.test/api/v1/feeds/abc/news.atom';

describe('NewsFeedSheet', () => {
  let api: {
    feedToken: ReturnType<typeof vi.fn>;
    createFeedToken: ReturnType<typeof vi.fn>;
    revokeFeedToken: ReturnType<typeof vi.fn>;
  };
  let confirmed: boolean;

  function render(status: NewsFeedTokenStatus) {
    api = {
      feedToken: vi.fn(() => of(status)),
      createFeedToken: vi.fn(() => of({ url: feedUrl, createdAt: '2030-01-02T10:00:00Z' })),
      revokeFeedToken: vi.fn(() => of(undefined)),
    };
    confirmed = true;
    TestBed.configureTestingModule({
      providers: [
        { provide: MAT_BOTTOM_SHEET_DATA, useValue: { eventId: 'ev-1' } },
        { provide: NewsApi, useValue: api },
        { provide: MatSnackBar, useValue: { open: vi.fn() } },
        {
          provide: MatDialog,
          useValue: { open: () => ({ afterClosed: () => of(confirmed) }) },
        },
      ],
    });
    const fixture = TestBed.createComponent(NewsFeedSheet);
    fixture.detectChanges();
    return fixture;
  }

  const button = (el: HTMLElement, label: string) =>
    [...el.querySelectorAll('button')].find((b) => b.textContent?.includes(label))!;

  it('creates a link and shows it once, with a copy button', async () => {
    const fixture = render({ active: false, createdAt: null, lastUsedAt: null });
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('.url')).toBeNull();

    button(el, 'Create feed link').click();
    fixture.detectChanges();

    expect(api.createFeedToken).toHaveBeenCalledWith('ev-1');
    expect(el.querySelector('.url')?.textContent).toBe(feedUrl);

    const writeText = vi.fn(() => Promise.resolve());
    Object.assign(navigator, { clipboard: { writeText } });
    button(el, 'Copy link').click();
    expect(writeText).toHaveBeenCalledWith(feedUrl);
  });

  it('offers a new link or revoking when one exists, without showing it', () => {
    const fixture = render({
      active: true,
      createdAt: '2030-01-01T10:00:00Z',
      lastUsedAt: null,
    });
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('.url')).toBeNull();
    expect(el.textContent).toContain('Not used yet');

    button(el, 'Get a new link').click();
    fixture.detectChanges();
    expect(api.createFeedToken).toHaveBeenCalled();
    expect(el.querySelector('.url')?.textContent).toBe(feedUrl);
  });

  it('revokes after confirmation', () => {
    const fixture = render({
      active: true,
      createdAt: '2030-01-01T10:00:00Z',
      lastUsedAt: '2030-01-02T10:00:00Z',
    });
    const el = fixture.nativeElement as HTMLElement;

    button(el, 'Revoke').click();
    fixture.detectChanges();

    expect(api.revokeFeedToken).toHaveBeenCalledWith('ev-1');
    expect(el.textContent).toContain('Create feed link');
  });
});
