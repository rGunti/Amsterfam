import { Component, OnInit, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { MatIconModule } from '@angular/material/icon';

import { EventApi } from '../../core/api/event.api';
import { EventResponse } from '../../core/models/event';

@Component({
  selector: 'app-events-list',
  imports: [RouterLink, MatCardModule, MatChipsModule, MatIconModule],
  templateUrl: './events-list.html',
  styleUrl: './events-list.scss',
})
export class EventsList implements OnInit {
  private readonly eventApi = inject(EventApi);

  readonly events = signal<EventResponse[]>([]);
  readonly loading = signal(true);

  ngOnInit(): void {
    this.eventApi.getEvents().subscribe({
      next: (events) => {
        this.events.set(events);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }
}
