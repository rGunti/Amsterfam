import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';

import { TimelineApi } from '../../core/api/timeline.api';
import { CurrentUserService } from '../../core/api/current-user.service';
import { CurrentEventService } from '../../core/event/current-event.service';
import { TimelineEntry } from '../../core/models/timeline';
import { TimelineLine, describeEntry, visibilityNote } from '../../shared/timeline';
import { dayKey, dayLabel, fullTimestamp, timeOfDay } from '../../shared/timestamp';

const PAGE_SIZE = 50;

interface TimelineLineView extends TimelineLine {
  entry: TimelineEntry;
  note: string | null;
  time: string;
  fullTime: string;
}

@Component({
  selector: 'app-timeline-page',
  imports: [MatButtonModule, MatCardModule, MatIconModule, MatTooltipModule],
  templateUrl: './timeline-page.html',
  styleUrl: './timeline-page.scss',
})
export class TimelinePage implements OnInit {
  private readonly api = inject(TimelineApi);
  private readonly currentUser = inject(CurrentUserService);
  private readonly currentEventService = inject(CurrentEventService);

  readonly event = this.currentEventService.event;
  readonly entries = signal<TimelineEntry[]>([]);
  readonly loading = signal(true);
  readonly error = signal(false);
  readonly hasMore = signal(false);

  /** Entries grouped under a heading per local day, newest first. */
  readonly days = computed(() => {
    const viewerId = this.currentUser.user()?.id ?? null;
    const now = new Date();
    const days: { key: string; label: string; lines: TimelineLineView[] }[] = [];
    for (const entry of this.entries()) {
      const key = dayKey(entry.occurredAt);
      let day = days.at(-1);
      if (day?.key !== key) {
        day = { key, label: dayLabel(entry.occurredAt, now), lines: [] };
        days.push(day);
      }
      day.lines.push({
        entry,
        ...describeEntry(entry, viewerId),
        note: visibilityNote(entry.visibility),
        time: timeOfDay(entry.occurredAt),
        fullTime: fullTimestamp(entry.occurredAt),
      });
    }
    return days;
  });

  ngOnInit(): void {
    this.load();
  }

  loadMore(): void {
    this.load(this.entries().at(-1)?.id);
  }

  private load(before?: number): void {
    const ev = this.event();
    if (!ev) {
      return;
    }
    this.loading.set(true);
    this.error.set(false);
    this.api.list(ev.id, before, PAGE_SIZE).subscribe({
      next: (page) => {
        this.entries.update((current) => (before === undefined ? page : [...current, ...page]));
        this.hasMore.set(page.length === PAGE_SIZE);
        this.loading.set(false);
      },
      error: () => {
        this.error.set(true);
        this.loading.set(false);
      },
    });
  }
}
