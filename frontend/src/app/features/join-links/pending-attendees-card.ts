import { Component, OnInit, computed, inject, input, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatListModule } from '@angular/material/list';
import { MatSnackBar } from '@angular/material/snack-bar';

import { AttendanceApi } from '../../core/api/attendance.api';
import { CurrentEventService } from '../../core/event/current-event.service';
import { AttendeeResponse } from '../../core/models/attendance';
import { ConfirmDialog, ConfirmDialogData } from '../../shared/confirm-dialog/confirm-dialog';

/** Requests waiting for approval. Hidden when there are none. */
@Component({
  selector: 'app-pending-attendees-card',
  imports: [MatButtonModule, MatCardModule, MatIconModule, MatListModule],
  templateUrl: './pending-attendees-card.html',
  styleUrl: './pending-attendees-card.scss',
})
export class PendingAttendeesCard implements OnInit {
  private readonly attendanceApi = inject(AttendanceApi);
  private readonly currentEventService = inject(CurrentEventService);
  private readonly snackBar = inject(MatSnackBar);
  private readonly dialog = inject(MatDialog);

  readonly eventId = input.required<string>();
  readonly isOwner = input(false);

  readonly attendees = signal<AttendeeResponse[]>([]);
  readonly actioning = signal(false);
  readonly pending = computed(() => this.attendees().filter((a) => a.role === 'Pending'));

  ngOnInit(): void {
    this.load();
  }

  private load(): void {
    this.attendanceApi.getAttendees(this.eventId()).subscribe((attendees) => {
      this.attendees.set(attendees);
      this.currentEventService.setPendingCount(this.pending().length);
    });
  }

  confirm(userId: number): void {
    this.actioning.set(true);
    this.attendanceApi.confirm(this.eventId(), userId).subscribe({
      next: () => {
        this.actioning.set(false);
        this.load();
        this.snackBar.open('Attendee confirmed', 'Dismiss', { duration: 3000 });
      },
      error: () => {
        this.actioning.set(false);
        this.snackBar.open('Could not confirm attendee', 'Dismiss', { duration: 3000 });
      },
    });
  }

  remove(userId: number, displayName: string): void {
    this.dialog
      .open<ConfirmDialog, ConfirmDialogData, boolean>(ConfirmDialog, {
        data: {
          title: 'Remove attendee',
          message: `Are you sure you want to remove ${displayName} from this event?`,
          confirmLabel: 'Remove',
        },
      })
      .afterClosed()
      .subscribe((ok) => {
        if (!ok) {
          return;
        }
        this.actioning.set(true);
        this.attendanceApi.remove(this.eventId(), userId).subscribe({
          next: () => {
            this.actioning.set(false);
            this.load();
            this.snackBar.open('Attendee removed', 'Dismiss', { duration: 3000 });
          },
          error: () => {
            this.actioning.set(false);
            this.snackBar.open('Could not remove attendee', 'Dismiss', { duration: 3000 });
          },
        });
      });
  }
}
