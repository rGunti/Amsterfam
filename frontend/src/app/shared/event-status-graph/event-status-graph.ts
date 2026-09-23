import { Component, computed, input } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';

import { EventStatus } from '../../core/models/event';
import { StatusStage, statusClass, statusStages } from '../event-status';

/** How far an event has come in its lifecycle and what's still ahead (#128). */
@Component({
  selector: 'app-event-status-graph',
  imports: [MatIconModule],
  templateUrl: './event-status-graph.html',
  styleUrl: './event-status-graph.scss',
})
export class EventStatusGraph {
  readonly status = input.required<EventStatus>();

  readonly stages = computed(() => statusStages(this.status()));
  readonly cancelled = computed(() => this.status() === 'Cancelled');

  readonly statusClass = statusClass;

  describe(stage: StatusStage): string {
    switch (stage.state) {
      case 'done':
        return `${stage.label}: done`;
      case 'current':
        return `${stage.label}: current stage`;
      case 'upcoming':
        return `${stage.label}: upcoming`;
      case 'inactive':
        return `${stage.label}: not reached`;
    }
  }
}
