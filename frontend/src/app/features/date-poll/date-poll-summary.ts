import { Component, OnInit, computed, inject, input, signal } from '@angular/core';

import { DatePollApi } from '../../core/api/date-poll.api';
import { EventResponse } from '../../core/models/event';
import { DatePollWeekSummary } from '../../core/models/date-poll';
import { CalendarMonth, buildCalendarMonths, parseDateOnly, weekdayLabels } from './date-poll.util';

@Component({
  selector: 'app-date-poll-summary',
  imports: [],
  templateUrl: './date-poll-summary.html',
  styleUrl: './date-poll-summary.scss',
})
export class DatePollSummary implements OnInit {
  private readonly datePollApi = inject(DatePollApi);

  readonly event = input.required<EventResponse>();

  readonly loading = signal(true);
  readonly weekSummaries = signal<Map<string, DatePollWeekSummary>>(new Map());

  readonly weekdays = weekdayLabels();
  readonly months = computed<CalendarMonth[]>(() => {
    const ev = this.event();
    if (!ev.pollRangeStart || !ev.pollRangeEnd) {
      return [];
    }
    return buildCalendarMonths(parseDateOnly(ev.pollRangeStart), parseDateOnly(ev.pollRangeEnd));
  });

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.datePollApi.getSummary(this.event().id).subscribe({
      next: (summary) => {
        this.weekSummaries.set(new Map(summary.weeks.map((w) => [w.weekStart, w])));
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  summaryFor(weekStart: string): DatePollWeekSummary | undefined {
    return this.weekSummaries().get(weekStart);
  }

  bestAvailabilityRatio(summary: DatePollWeekSummary | undefined): number {
    if (!summary) {
      return 0;
    }
    const total = summary.available + summary.unavailable + summary.partial + summary.noResponse;
    return total === 0 ? 0 : summary.available / total;
  }
}
