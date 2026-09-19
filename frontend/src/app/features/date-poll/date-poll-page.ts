import { Component, ViewChild, computed, inject } from '@angular/core';
import { MatCardModule } from '@angular/material/card';

import { CurrentEventService } from '../../core/event/current-event.service';
import { DatePollCalendar } from './date-poll-calendar';
import { DatePollRange } from './date-poll-range';
import { DatePollSummary } from './date-poll-summary';

@Component({
  selector: 'app-date-poll-page',
  imports: [MatCardModule, DatePollRange, DatePollCalendar, DatePollSummary],
  templateUrl: './date-poll-page.html',
  styleUrl: './date-poll-page.scss',
})
export class DatePollPage {
  private readonly currentEventService = inject(CurrentEventService);

  readonly event = this.currentEventService.event;
  readonly isOrganiser = computed(() => this.event()?.currentUserRole === 'Organiser');

  @ViewChild(DatePollSummary) private datePollSummary?: DatePollSummary;

  onRangeSaved(summary: { pollRangeStart: string | null; pollRangeEnd: string | null }): void {
    const ev = this.event();
    if (!ev) {
      return;
    }
    this.currentEventService.setEvent({
      ...ev,
      pollRangeStart: summary.pollRangeStart,
      pollRangeEnd: summary.pollRangeEnd,
    });
    this.datePollSummary?.load();
  }

  onAvailabilitySaved(): void {
    this.datePollSummary?.load();
  }
}
