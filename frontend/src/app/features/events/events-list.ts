import { Component, OnInit, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatTooltipModule } from '@angular/material/tooltip';

import { EventApi } from '../../core/api/event.api';
import { CurrentEventService } from '../../core/event/current-event.service';
import { EventResponse } from '../../core/models/event';
import { OrganiserAvatarStack } from '../../shared/organiser-avatar-stack/organiser-avatar-stack';
import { statusIcon } from '../../shared/event-status-icon';

@Component({
  selector: 'app-events-list',
  imports: [
    RouterLink,
    MatCardModule,
    MatChipsModule,
    MatIconModule,
    MatButtonModule,
    MatTooltipModule,
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
}
