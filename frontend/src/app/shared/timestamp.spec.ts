import { dayKey, dayLabel, fullTimestamp, timeOfDay } from './timestamp';

// Local-time ISO strings (no offset), so the tests hold in any timezone.
const now = new Date('2026-09-23T15:00:00');

describe('dayLabel', () => {
  it('says "Today" and "Yesterday"', () => {
    expect(dayLabel('2026-09-23T00:05:00', now)).toBe('Today');
    expect(dayLabel('2026-09-22T23:59:00', now)).toBe('Yesterday');
  });

  it('handles yesterday across a month boundary', () => {
    expect(dayLabel('2026-08-31T23:59:00', new Date('2026-09-01T00:10:00'))).toBe('Yesterday');
  });

  it('drops the year earlier this year', () => {
    expect(dayLabel('2026-03-02T10:00:00', now)).toBe('Mon 2 Mar');
  });

  it('shows the year for earlier years', () => {
    expect(dayLabel('2025-12-31T10:00:00', now)).toBe('Wed 31 Dec 2025');
  });
});

describe('dayKey', () => {
  it('groups by local calendar day', () => {
    expect(dayKey('2026-09-23T00:00:00')).toBe(dayKey('2026-09-23T23:59:59'));
    expect(dayKey('2026-09-23T23:59:59')).not.toBe(dayKey('2026-09-24T00:00:00'));
  });
});

describe('timeOfDay / fullTimestamp', () => {
  it('formats in 24h, the full form with seconds', () => {
    expect(timeOfDay('2026-09-23T09:05:00')).toBe('09:05');
    expect(fullTimestamp('2026-09-23T21:30:09')).toBe('23 Sept 2026, 21:30:09');
  });
});
