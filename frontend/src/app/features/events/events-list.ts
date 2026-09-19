import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { RouterLink } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatExpansionModule } from '@angular/material/expansion';

import { EventApi } from '../../core/api/event.api';
import { CurrentEventService } from '../../core/event/current-event.service';
import { EventResponse } from '../../core/models/event';
import { OrganiserAvatarStack } from '../../shared/organiser-avatar-stack/organiser-avatar-stack';
import { isReadOnly, statusClass, statusIcon, statusLabel } from '../../shared/event-status';

@Component({
  selector: 'app-events-list',
  imports: [
    RouterLink,
    NgTemplateOutlet,
    MatCardModule,
    MatChipsModule,
    MatIconModule,
    MatButtonModule,
    MatTooltipModule,
    MatExpansionModule,
    OrganiserAvatarStack,
  ],
  templateUrl: './events-list.html',
  styleUrl: './events-list.scss',
})
export class EventsList implements OnInit {
  private readonly eventApi = inject(EventApi);
  private readonly currentEventService = inject(CurrentEventService);

  readonly events = signal<EventResponse[]>([]);
  readonly loading = signal(true);
  /** Archived and cancelled trips are tucked away so the list stays about what's coming up. */
  readonly activeEvents = computed(() => this.events().filter((e) => !isReadOnly(e.status)));
  readonly pastEvents = computed(() => this.events().filter((e) => isReadOnly(e.status)));

  ngOnInit(): void {
    this.currentEventService.clear();
    this.eventApi.getEvents().subscribe({
      next: (events) => {
        this.events.set(events);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  readonly statusIcon = statusIcon;
  readonly statusLabel = statusLabel;
  readonly statusClass = statusClass;
}
