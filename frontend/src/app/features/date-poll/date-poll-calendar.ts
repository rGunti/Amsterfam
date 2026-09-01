import { Component, OnInit, computed, inject, input, output, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Observable, forkJoin, of } from 'rxjs';

import { DatePollApi } from '../../core/api/date-poll.api';
import { EventResponse } from '../../core/models/event';
import { DatePollEntry, DatePollStatus } from '../../core/models/date-poll';
import {
  CalendarDay,
  CalendarMonth,
  buildCalendarMonths,
  nextStatus,
  parseDateOnly,
  weekdayLabels,
} from './date-poll.util';

@Component({
  selector: 'app-date-poll-calendar',
  imports: [MatButtonModule, MatIconModule],
  templateUrl: './date-poll-calendar.html',
  styleUrl: './date-poll-calendar.scss',
})
export class DatePollCalendar implements OnInit {
  private readonly datePollApi = inject(DatePollApi);
  private readonly snackBar = inject(MatSnackBar);

  readonly event = input.required<EventResponse>();
  readonly saved = output<void>();

  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly dirty = signal(false);
  readonly weekStatuses = signal<Map<string, DatePollStatus>>(new Map());
  private original = new Map<string, DatePollStatus>();

  readonly weekdays = weekdayLabels();
  readonly months = computed<CalendarMonth[]>(() => {
    const ev = this.event();
    if (!ev.pollRangeStart || !ev.pollRangeEnd) {
      return [];
    }
    return buildCalendarMonths(parseDateOnly(ev.pollRangeStart), parseDateOnly(ev.pollRangeEnd));
  });

  ngOnInit(): void {
    this.datePollApi.getMyEntries(this.event().id).subscribe({
      next: (entries) => {
        const loaded = new Map(entries.map((e) => [e.weekStart, e.status]));
        this.weekStatuses.set(loaded);
        this.original = new Map(loaded);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  statusFor(weekStart: string): DatePollStatus | undefined {
    return this.weekStatuses().get(weekStart);
  }

  weekInRange(week: CalendarDay[]): boolean {
    return week.some((day) => day.inPollRange);
  }

  toggleWeek(weekStart: string, inPollRange: boolean): void {
    if (!inPollRange) {
      return;
    }
    const updated = new Map(this.weekStatuses());
    updated.set(weekStart, nextStatus(updated.get(weekStart)));
    this.weekStatuses.set(updated);
    this.dirty.set(true);
  }

  clearWeek(event: Event, weekStart: string, inPollRange: boolean): void {
    event.stopPropagation();
    if (!inPollRange || !this.weekStatuses().has(weekStart)) {
      return;
    }
    const updated = new Map(this.weekStatuses());
    updated.delete(weekStart);
    this.weekStatuses.set(updated);
    this.dirty.set(true);
  }

  save(): void {
    const current = this.weekStatuses();

    // Only send weeks that actually changed this session — resending untouched
    // entries would re-validate them against the poll range, which may have
    // since shrunk past weeks the user set before it changed.
    const entries: DatePollEntry[] = Array.from(current, ([weekStart, status]) => ({
      weekStart,
      status,
    })).filter(({ weekStart, status }) => this.original.get(weekStart) !== status);
    const removed = Array.from(this.original.keys()).filter((weekStart) => !current.has(weekStart));

    if (!entries.length && !removed.length) {
      this.dirty.set(false);
      return;
    }

    this.saving.set(true);
    const eventId = this.event().id;
    const deletes: Observable<unknown> = removed.length
      ? forkJoin(removed.map((weekStart) => this.datePollApi.deleteMyEntry(eventId, weekStart)))
      : of(null);
    const upsert: Observable<unknown> = entries.length
      ? this.datePollApi.updateMyEntries(eventId, { entries })
      : of(null);

    deletes.subscribe({
      next: () => {
        upsert.subscribe({
          next: () => {
            this.saving.set(false);
            this.dirty.set(false);
            this.original = new Map(current);
            this.snackBar.open('Availability saved', 'Dismiss', { duration: 3000 });
            this.saved.emit();
          },
          error: () => {
            this.saving.set(false);
            this.snackBar.open('Could not save availability', 'Dismiss', { duration: 3000 });
          },
        });
      },
      error: () => {
        this.saving.set(false);
        this.snackBar.open('Could not save availability', 'Dismiss', { duration: 3000 });
      },
    });
  }
}
