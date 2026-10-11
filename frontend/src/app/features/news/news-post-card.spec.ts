import { TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { vi } from 'vitest';

import { NewsApi } from '../../core/api/news.api';
import { NewsPost } from '../../core/models/news';
import { NewsPostCard } from './news-post-card';

const base: NewsPost = {
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
  acknowledgedByMe: false,
  reactions: [],
  myReactions: [],
  ackCount: null,
  ackTotal: null,
};

describe('NewsPostCard', () => {
  let api: Record<string, ReturnType<typeof vi.fn>>;
  let dialogOpen: ReturnType<typeof vi.fn>;

  function render(overrides: Partial<NewsPost> = {}) {
    api = {
      react: vi.fn(() => of(undefined)),
      unreact: vi.fn(() => of(undefined)),
      acknowledge: vi.fn(() => of(undefined)),
    };
    dialogOpen = vi.fn();
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: NewsApi, useValue: api },
        { provide: MatDialog, useValue: { open: dialogOpen } },
      ],
    });
    const fixture = TestBed.createComponent(NewsPostCard);
    fixture.componentRef.setInput('eventId', 'ev-1');
    fixture.componentRef.setInput('post', { ...base, ...overrides });
    fixture.detectChanges();
    return fixture;
  }

  const reaction = (el: HTMLElement, emoji: string) =>
    [...el.querySelectorAll<HTMLButtonElement>('.reaction')].find((b) =>
      b.textContent?.includes(emoji),
    )!;

  it('adds and removes a reaction, updating the count', () => {
    const fixture = render({ reactions: [{ emoji: '🎉', count: 2 }] });
    const el = fixture.nativeElement as HTMLElement;
    const party = reaction(el, '🎉');
    expect(party.textContent).toContain('2');
    expect(party.getAttribute('aria-pressed')).toBe('false');

    party.click();
    fixture.detectChanges();
    expect(api['react']).toHaveBeenCalledWith('ev-1', 7, '🎉');
    expect(reaction(el, '🎉').textContent).toContain('3');
    expect(reaction(el, '🎉').getAttribute('aria-pressed')).toBe('true');

    reaction(el, '🎉').click();
    fixture.detectChanges();
    expect(api['unreact']).toHaveBeenCalledWith('ev-1', 7, '🎉');
    expect(reaction(el, '🎉').textContent).toContain('2');
  });

  it('hides "Got it" once acknowledged', () => {
    const fixture = render({ requiresAck: true });
    const el = fixture.nativeElement as HTMLElement;
    const button = [...el.querySelectorAll('button')].find((b) =>
      b.textContent?.includes('Got it'),
    )!;
    button.click();
    fixture.detectChanges();

    expect(api['acknowledge']).toHaveBeenCalledWith('ev-1', 7);
    expect(el.textContent).not.toContain('Got it');
    expect(el.textContent).toContain("You've acknowledged this");
  });

  it('shows the acknowledgement count to organisers only', () => {
    const attendee = render({ requiresAck: true }).nativeElement as HTMLElement;
    expect(attendee.textContent).not.toContain('acknowledged');
    TestBed.resetTestingModule();

    const fixture = render({ requiresAck: true, ackCount: 3, ackTotal: 18 });
    const el = fixture.nativeElement as HTMLElement;
    const count = el.querySelector<HTMLButtonElement>('.ack-count')!;
    expect(count.textContent).toContain('3 of 18 acknowledged');
    count.click();
    expect(dialogOpen).toHaveBeenCalled();
  });

  it('leaves out reactions in compact mode', () => {
    const fixture = TestBed.configureTestingModule({
      providers: [provideRouter([]), { provide: NewsApi, useValue: {} }],
    }).createComponent(NewsPostCard);
    fixture.componentRef.setInput('eventId', 'ev-1');
    fixture.componentRef.setInput('post', base);
    fixture.componentRef.setInput('compact', true);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('.reactions')).toBeNull();
    expect(el.textContent).toContain('Read more');
  });
});
