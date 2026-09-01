import { DatePollStatus } from '../../core/models/date-poll';

export function parseDateOnly(value: string): Date {
  const [year, month, day] = value.split('-').map(Number);
  return new Date(year, month - 1, day);
}

export function formatDateOnly(date: Date): string {
  const y = date.getFullYear();
  const m = String(date.getMonth() + 1).padStart(2, '0');
  const d = String(date.getDate()).padStart(2, '0');
  return `${y}-${m}-${d}`;
}

/** Monday of the ISO week containing `date`. */
export function weekStartOf(date: Date): Date {
  const day = date.getDay(); // 0 = Sunday
  const offset = (day + 6) % 7; // Monday = 0
  const result = new Date(date);
  result.setDate(date.getDate() - offset);
  return result;
}

export function addDays(date: Date, days: number): Date {
  const result = new Date(date);
  result.setDate(date.getDate() + days);
  return result;
}

export interface CalendarDay {
  date: Date;
  dateStr: string;
  dayOfMonth: number;
  inCurrentMonth: boolean;
  inPollRange: boolean;
  weekStart: string;
}

export interface CalendarMonth {
  label: string;
  weeks: CalendarDay[][];
}

const WEEKDAY_LABELS = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];
const MONTH_FORMATTER = new Intl.DateTimeFormat(undefined, { month: 'long', year: 'numeric' });

export function weekdayLabels(): string[] {
  return WEEKDAY_LABELS;
}

/** Builds one calendar grid per month spanning [rangeStart, rangeEnd). */
export function buildCalendarMonths(rangeStart: Date, rangeEnd: Date): CalendarMonth[] {
  const months: CalendarMonth[] = [];
  let cursor = new Date(rangeStart.getFullYear(), rangeStart.getMonth(), 1);
  const last = new Date(rangeEnd.getFullYear(), rangeEnd.getMonth(), 1);

  while (cursor <= last) {
    const monthIndex = cursor.getMonth();
    const gridStart = weekStartOf(cursor);
    const monthEnd = new Date(cursor.getFullYear(), cursor.getMonth() + 1, 0);
    const gridEnd = addDays(weekStartOf(monthEnd), 6);

    const weeks: CalendarDay[][] = [];
    let day = gridStart;
    while (day <= gridEnd) {
      const week: CalendarDay[] = [];
      const weekStartStr = formatDateOnly(day);
      for (let i = 0; i < 7; i++) {
        week.push({
          date: day,
          dateStr: formatDateOnly(day),
          dayOfMonth: day.getDate(),
          inCurrentMonth: day.getMonth() === monthIndex,
          inPollRange: day >= rangeStart && day < rangeEnd,
          weekStart: weekStartStr,
        });
        day = addDays(day, 1);
      }
      weeks.push(week);
    }

    // Drop weeks with no selectable day at all — e.g. a month's leftover grid
    // when the poll range ends partway through it.
    const selectableWeeks = weeks.filter((week) => week.some((d) => d.inPollRange));
    if (selectableWeeks.length) {
      months.push({ label: MONTH_FORMATTER.format(cursor), weeks: selectableWeeks });
    }
    cursor = new Date(cursor.getFullYear(), cursor.getMonth() + 1, 1);
  }

  return months;
}

const STATUS_CYCLE: DatePollStatus[] = ['Available', 'Partial', 'Unavailable'];

export function nextStatus(current: DatePollStatus | undefined): DatePollStatus {
  if (current === undefined) {
    return STATUS_CYCLE[0];
  }
  const idx = STATUS_CYCLE.indexOf(current);
  return STATUS_CYCLE[(idx + 1) % STATUS_CYCLE.length];
}
