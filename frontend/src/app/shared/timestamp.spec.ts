import { fullTimestamp, timelineGroup } from './timestamp';

// Local-time ISO strings (no offset), so the tests hold in any timezone.
// Wednesday; the week started on Monday the 21st.
const now = new Date('2026-09-23T15:00:00');
const group = (iso: string, at = now) => {
  const { label, stamp } = timelineGroup(iso, at);
  return `${label} | ${stamp}`;
};

describe('timelineGroup', () => {
  it('shows the time for today and yesterday', () => {
    expect(group('2026-09-23T00:05:00')).toBe('Today | 00:05');
    expect(group('2026-09-22T23:59:00')).toBe('Yesterday | 23:59');
  });

  it('shows weekday and time earlier this week', () => {
    expect(group('2026-09-21T08:30:00')).toBe('Earlier this week | Mon 08:30');
  });

  it('shows the date earlier this month', () => {
    expect(group('2026-09-20T23:00:00')).toBe('Earlier this month | 20 Sept');
    expect(group('2026-09-01T00:00:00')).toBe('Earlier this month | 1 Sept');
  });

  it('groups by month before that, with the year only for other years', () => {
    expect(group('2026-08-31T12:00:00')).toBe('August | 31 Aug');
    expect(group('2025-10-17T12:00:00')).toBe('October 2025 | 17 Oct');
    expect(timelineGroup('2026-08-02T00:00:00', now).key).toBe(
      timelineGroup('2026-08-31T23:59:00', now).key,
    );
  });

  it('lets this week reach back into last month', () => {
    // Wednesday 2 Sept; the week started on Monday 31 Aug.
    const early = new Date('2026-09-02T12:00:00');
    expect(group('2026-08-31T10:00:00', early)).toBe('Earlier this week | Mon 10:00');
    expect(group('2026-08-30T10:00:00', early)).toBe('August | 30 Aug');
  });

  it('keeps yesterday ahead of the week on Mondays', () => {
    const monday = new Date('2026-09-21T09:00:00');
    expect(group('2026-09-20T22:00:00', monday)).toBe('Yesterday | 22:00');
    expect(group('2026-09-19T22:00:00', monday)).toBe('Earlier this month | 19 Sept');
  });
});

describe('fullTimestamp', () => {
  it('includes seconds', () => {
    expect(fullTimestamp('2026-09-23T21:30:09')).toBe('23 Sept 2026, 21:30:09');
  });
});
