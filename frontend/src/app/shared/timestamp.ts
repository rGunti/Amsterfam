import { APP_LOCALE } from './app-locale';

const TIME = new Intl.DateTimeFormat(APP_LOCALE, { hour: '2-digit', minute: '2-digit' });
const WEEKDAY = new Intl.DateTimeFormat(APP_LOCALE, { weekday: 'short' });
const DAY_MONTH = new Intl.DateTimeFormat(APP_LOCALE, { day: 'numeric', month: 'short' });
const MONTH = new Intl.DateTimeFormat(APP_LOCALE, { month: 'long' });
const MONTH_YEAR = new Intl.DateTimeFormat(APP_LOCALE, { month: 'long', year: 'numeric' });
const FULL_DATE_TIME = new Intl.DateTimeFormat(APP_LOCALE, {
  day: 'numeric',
  month: 'short',
  year: 'numeric',
  hour: '2-digit',
  minute: '2-digit',
  second: '2-digit',
});

export interface TimelineGroup {
  /** Stable per group; consecutive entries with the same key share a heading. */
  key: string;
  label: string;
  /** How an entry in this group shows its time; the heading already covers the rest. */
  stamp: string;
}

const startOfDay = (d: Date) => new Date(d.getFullYear(), d.getMonth(), d.getDate());

/**
 * Buckets a past moment relative to `now`, in local time: Today, Yesterday, Earlier this
 * week (weeks start on Monday), Earlier this month, then one group per month.
 */
export function timelineGroup(iso: string, now = new Date()): TimelineGroup {
  const at = new Date(iso);
  const today = startOfDay(now);
  const yesterday = new Date(today.getFullYear(), today.getMonth(), today.getDate() - 1);
  const weekStart = new Date(
    today.getFullYear(),
    today.getMonth(),
    today.getDate() - ((today.getDay() + 6) % 7),
  );
  const monthStart = new Date(today.getFullYear(), today.getMonth(), 1);
  const time = TIME.format(at);

  if (at >= today) {
    return { key: 'today', label: 'Today', stamp: time };
  }
  if (at >= yesterday) {
    return { key: 'yesterday', label: 'Yesterday', stamp: time };
  }
  if (at >= weekStart) {
    return { key: 'week', label: 'Earlier this week', stamp: `${WEEKDAY.format(at)} ${time}` };
  }
  if (at >= monthStart) {
    return { key: 'month', label: 'Earlier this month', stamp: DAY_MONTH.format(at) };
  }
  return {
    key: `${at.getFullYear()}-${at.getMonth()}`,
    label: at.getFullYear() === now.getFullYear() ? MONTH.format(at) : MONTH_YEAR.format(at),
    stamp: DAY_MONTH.format(at),
  };
}

/** Full date and time with seconds, e.g. for a tooltip. */
export function fullTimestamp(iso: string): string {
  return FULL_DATE_TIME.format(new Date(iso));
}
