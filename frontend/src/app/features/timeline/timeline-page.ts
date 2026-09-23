import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';

import { TimelineApi } from '../../core/api/timeline.api';
import { CurrentUserService } from '../../core/api/current-user.service';
import { CurrentEventService } from '../../core/event/current-event.service';
import { TimelineEntry } from '../../core/models/timeline';
import { describeEntry, visibilityNote } from '../../shared/timeline';

const PAGE_SIZE = 50;

@Component({
  selector: 'app-timeline-page',
  imports: [DatePipe, MatButtonModule, MatCardModule, MatIconModule, MatTooltipModule],
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

  readonly lines = computed(() => {
    const viewerId = this.currentUser.user()?.id ?? null;
    return this.entries().map((entry) => ({
      entry,
      ...describeEntry(entry, viewerId),
      note: visibilityNote(entry.visibility),
    }));
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
