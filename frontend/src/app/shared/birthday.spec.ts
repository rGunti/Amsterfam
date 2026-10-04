import { daysInMonth, formatBirthday } from './birthday';

describe('formatBirthday', () => {
  it('leaves the year out when it was not shared', () => {
    expect(formatBirthday({ month: 7, day: 14, year: null })).toBe('14 July');
  });

  it('includes the year when shared', () => {
    expect(formatBirthday({ month: 7, day: 14, year: 1990 })).toBe('14 July 1990');
  });

  it('formats a leap day without a year', () => {
    expect(formatBirthday({ month: 2, day: 29, year: null })).toBe('29 February');
  });
});

describe('daysInMonth', () => {
  it('allows 29 February when the year is unknown', () => {
    expect(daysInMonth(2, null)).toBe(29);
  });

  it('follows the actual year when given', () => {
    expect(daysInMonth(2, 2001)).toBe(28);
    expect(daysInMonth(4, 2001)).toBe(30);
    expect(daysInMonth(12, null)).toBe(31);
  });
});
