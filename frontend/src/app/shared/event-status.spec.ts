import { EventResponse, EventStatus } from '../core/models/event';
import { canPostNews, canViewNews, statusStages } from './event-status';

const summary = (status: Parameters<typeof statusStages>[0]) =>
  statusStages(status).map((s) => `${s.status}:${s.state}`);

describe('statusStages', () => {
  it('marks earlier stages done and later ones upcoming', () => {
    expect(summary('Open')).toEqual([
      'Draft:done',
      'LookingForDate:done',
      'Open:current',
      'InProgress:upcoming',
      'Closed:upcoming',
      'Archived:upcoming',
    ]);
  });

  it('starts with only the draft stage active', () => {
    expect(summary('Draft')[0]).toBe('Draft:current');
    expect(
      statusStages('Draft')
        .slice(1)
        .every((s) => s.state === 'upcoming'),
    ).toBe(true);
  });

  it('checks off every stage before Archived', () => {
    expect(summary('Archived')).toEqual([
      'Draft:done',
      'LookingForDate:done',
      'Open:done',
      'InProgress:done',
      'Closed:done',
      'Archived:current',
    ]);
  });

  it('replaces Archived with Cancelled and greys out the rest', () => {
    expect(summary('Cancelled')).toEqual([
      'Draft:inactive',
      'LookingForDate:inactive',
      'Open:inactive',
      'InProgress:inactive',
      'Closed:inactive',
      'Cancelled:current',
    ]);
    expect(statusStages('Cancelled')[5].icon).toBe('cancel');
  });
});

describe('news permissions', () => {
  const ev = (currentUserRole: EventResponse['currentUserRole'], status: EventStatus = 'Open') =>
    ({ currentUserRole, status }) as EventResponse;

  it('lets confirmed members read the news', () => {
    expect(canViewNews(ev('Attendee'))).toBe(true);
    expect(canViewNews(ev('Organiser'))).toBe(true);
    expect(canViewNews(ev('Pending'))).toBe(false);
    expect(canViewNews(ev(null))).toBe(false);
  });

  it('lets organisers post until the event is read-only', () => {
    expect(canPostNews(ev('Organiser'))).toBe(true);
    expect(canPostNews(ev('Attendee'))).toBe(false);
    expect(canPostNews(ev('Organiser', 'Archived'))).toBe(false);
    expect(canPostNews(ev('Organiser', 'Cancelled'))).toBe(false);
  });
});
