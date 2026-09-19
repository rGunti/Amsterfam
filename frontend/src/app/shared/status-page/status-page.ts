import { Component, OnInit, inject } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';

import { CurrentEventService } from '../../core/event/current-event.service';

export interface StatusPageData {
  icon: string;
  title: string;
  message: string;
}

/**
 * Dead-end page (404, cancelled event, …). Configured via route `data`; leaves the event
 * context so the shell only offers the way back to the event list.
 */
@Component({
  selector: 'app-status-page',
  imports: [RouterLink, MatButtonModule, MatCardModule, MatIconModule],
  templateUrl: './status-page.html',
  styleUrl: './status-page.scss',
})
export class StatusPage implements OnInit {
  private readonly currentEventService = inject(CurrentEventService);
  readonly data = inject(ActivatedRoute).snapshot.data as StatusPageData;

  ngOnInit(): void {
    this.currentEventService.clear();
  }
}
