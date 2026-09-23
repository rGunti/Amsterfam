import { APP_LOCALE } from './app-locale';

const TIME = new Intl.DateTimeFormat(APP_LOCALE, { hour: '2-digit', minute: '2-digit' });
const DAY = new Intl.DateTimeFormat(APP_LOCALE, {
  weekday: 'short',
  day: 'numeric',
  month: 'short',
});
// en-GB adds a comma after the weekday once a year is included, so the weekday is joined
// on by hand to match the "Mon 2 Mar" style.
const WEEKDAY = new Intl.DateTimeFormat(APP_LOCALE, { weekday: 'short' });
const DATE_WITH_YEAR = new Intl.DateTimeFormat(APP_LOCALE, {
  day: 'numeric',
  month: 'short',
  year: 'numeric',
});
const FULL_DATE_TIME = new Intl.DateTimeFormat(APP_LOCALE, {
  day: 'numeric',
  month: 'short',
  year: 'numeric',
  hour: '2-digit',
  minute: '2-digit',
  second: '2-digit',
});

const pad = (n: number) => String(n).padStart(2, '0');

/** The local calendar day of a moment as "yyyy-MM-dd", for grouping. */
export function dayKey(iso: string): string {
  const at = new Date(iso);
  return `${at.getFullYear()}-${pad(at.getMonth() + 1)}-${pad(at.getDate())}`;
}

/**
 * Heading for a local calendar day: "Today", "Yesterday", the weekday and date earlier
 * this year, and the full date with year before that.
 */
export function dayLabel(iso: string, now = new Date()): string {
  const key = dayKey(iso);
  if (key === dayKey(now.toISOString())) {
    return 'Today';
  }
  const yesterday = new Date(now.getFullYear(), now.getMonth(), now.getDate() - 1);
  if (key === dayKey(yesterday.toISOString())) {
    return 'Yesterday';
  }
  const at = new Date(iso);
  return at.getFullYear() === now.getFullYear()
    ? DAY.format(at)
    : `${WEEKDAY.format(at)} ${DATE_WITH_YEAR.format(at)}`;
}

/** Local time of day, e.g. "21:30". */
export function timeOfDay(iso: string): string {
  return TIME.format(new Date(iso));
}

/** Full date and time with seconds, e.g. for a tooltip. */
export function fullTimestamp(iso: string): string {
  return FULL_DATE_TIME.format(new Date(iso));
}
