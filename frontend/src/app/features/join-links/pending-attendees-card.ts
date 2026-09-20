import { Component, OnInit, computed, inject, input, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatListModule } from '@angular/material/list';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Observable, catchError, forkJoin, map, of } from 'rxjs';

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
  private readonly selected = signal<ReadonlySet<number>>(new Set());
  readonly selectedCount = computed(() => this.selected().size);
  readonly allSelected = computed(
    () => this.pending().length > 0 && this.selected().size === this.pending().length,
  );
  /** Organiser requests can only be confirmed by the owner. */
  readonly confirmableSelected = computed(() =>
    this.pending().filter((a) => this.selected().has(a.userId) && this.canConfirm(a)),
  );

  ngOnInit(): void {
    this.load();
  }

  private load(): void {
    this.attendanceApi.getAttendees(this.eventId()).subscribe((attendees) => {
      this.attendees.set(attendees);
      // Drop selections for people who are no longer pending.
      const stillPending = new Set(this.pending().map((a) => a.userId));
      this.selected.update((sel) => new Set([...sel].filter((id) => stillPending.has(id))));
      this.currentEventService.setPendingCount(this.pending().length);
    });
  }

  canConfirm(attendee: AttendeeResponse): boolean {
    return !attendee.requestedOrganiser || this.isOwner();
  }

  isSelected(userId: number): boolean {
    return this.selected().has(userId);
  }

  toggle(userId: number): void {
    this.selected.update((sel) => {
      const next = new Set(sel);
      if (!next.delete(userId)) {
        next.add(userId);
      }
      return next;
    });
  }

  toggleAll(): void {
    this.selected.set(
      this.allSelected() ? new Set() : new Set(this.pending().map((a) => a.userId)),
    );
  }

  clearSelection(): void {
    this.selected.set(new Set());
  }

  confirmSelected(): void {
    const ids = this.confirmableSelected().map((a) => a.userId);
    this.runBulk(ids, (id) => this.attendanceApi.confirm(this.eventId(), id), 'confirmed');
  }

  removeSelected(): void {
    const ids = [...this.selected()];
    this.dialog
      .open<ConfirmDialog, ConfirmDialogData, boolean>(ConfirmDialog, {
        data: {
          title: 'Remove attendees',
          message: `Are you sure you want to remove ${ids.length} ${
            ids.length === 1 ? 'request' : 'requests'
          } from this event?`,
          confirmLabel: 'Remove',
        },
      })
      .afterClosed()
      .subscribe((ok) => {
        if (ok) {
          this.runBulk(ids, (id) => this.attendanceApi.remove(this.eventId(), id), 'removed');
        }
      });
  }

  /** Runs one request per person; a failure for one doesn't stop the others. */
  private runBulk(
    ids: number[],
    action: (userId: number) => Observable<void>,
    doneLabel: 'confirmed' | 'removed',
  ): void {
    if (ids.length === 0) {
      return;
    }
    this.actioning.set(true);
    forkJoin(
      ids.map((id) =>
        action(id).pipe(
          map(() => true),
          catchError(() => of(false)),
        ),
      ),
    ).subscribe((results) => {
      const ok = results.filter(Boolean).length;
      this.actioning.set(false);
      this.load();
      this.snackBar.open(
        ok === ids.length
          ? `${ok} ${ok === 1 ? 'attendee' : 'attendees'} ${doneLabel}`
          : `${ok} of ${ids.length} ${doneLabel}; the rest failed`,
        'Dismiss',
        { duration: 4000 },
      );
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
