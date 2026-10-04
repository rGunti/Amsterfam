import { Birthday } from '../core/models/user';
import { APP_LOCALE } from './app-locale';

/** "14 July", or "14 July 1990" when the year was shared. */
export function formatBirthday(birthday: Birthday): string {
  // Any leap year works as a stand-in, so 29 February still formats without a year.
  const date = new Date(Date.UTC(birthday.year ?? 2000, birthday.month - 1, birthday.day));
  return new Intl.DateTimeFormat(APP_LOCALE, {
    day: 'numeric',
    month: 'long',
    year: birthday.year === null ? undefined : 'numeric',
    timeZone: 'UTC',
  }).format(date);
}

/** Days in the month, allowing 29 February when the year is unknown. */
export function daysInMonth(month: number, year: number | null): number {
  return new Date(Date.UTC(year ?? 2000, month, 0)).getUTCDate();
}
