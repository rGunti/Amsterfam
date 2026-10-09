import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { vi } from 'vitest';

import { EventApi } from '../api/event.api';
import { EventResponse } from '../models/event';
import { CurrentEventService, EVENT_POLL_INTERVAL_MS } from './current-event.service';

describe('CurrentEventService badge polling', () => {
  const base = { id: 'ev-1', name: 'Trip', unreadNewsCount: 0 } as EventResponse;
  let fresh: EventResponse;
  let getEvent: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    vi.useFakeTimers();
    fresh = { ...base, name: 'Renamed elsewhere', unreadNewsCount: 2 };
    getEvent = vi.fn(() => of(fresh));
    TestBed.configureTestingModule({
      providers: [{ provide: EventApi, useValue: { getEvent } }],
    });
  });

  afterEach(() => vi.useRealTimers());

  function start(): CurrentEventService {
    const service = TestBed.inject(CurrentEventService);
    service.setEvent(base);
    TestBed.tick();
    return service;
  }

  it('re-fetches the open event every minute and takes over only the unread count', () => {
    const service = start();

    vi.advanceTimersByTime(EVENT_POLL_INTERVAL_MS - 1);
    expect(getEvent).not.toHaveBeenCalled();

    vi.advanceTimersByTime(1);
    expect(getEvent).toHaveBeenCalledWith('ev-1');
    expect(service.event()?.unreadNewsCount).toBe(2);
    // The rest stays as the page has it, so forms editing the event aren't disturbed.
    expect(service.event()?.name).toBe('Trip');
  });

  it('stops polling once the event is closed', () => {
    const service = start();
    service.clear();
    TestBed.tick();

    vi.advanceTimersByTime(EVENT_POLL_INTERVAL_MS * 3);
    expect(getEvent).not.toHaveBeenCalled();
  });

  it('clears the badge after the feed is opened', () => {
    const service = start();
    service.setEvent({ ...base, unreadNewsCount: 3 });
    service.setUnreadNews(0);
    expect(service.event()?.unreadNewsCount).toBe(0);
  });
});
