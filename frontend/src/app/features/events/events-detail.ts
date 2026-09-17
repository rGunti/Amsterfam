import { Component, OnInit, ViewChild, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import {
  FormBuilder,
  FormControl,
  FormGroup,
  ReactiveFormsModule,
  Validators,
} from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatListModule } from '@angular/material/list';
import { MatMenuModule } from '@angular/material/menu';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatDialog } from '@angular/material/dialog';
import { HttpErrorResponse } from '@angular/common/http';
import { CurrencyPipe } from '@angular/common';

import { ConfirmDialog, ConfirmDialogData } from '../../shared/confirm-dialog/confirm-dialog';
import {
  PaymentMethodsViewerDialog,
  PaymentMethodsViewerDialogData,
} from '../../shared/payment-methods-viewer-dialog/payment-methods-viewer-dialog';

import { EventApi } from '../../core/api/event.api';
import { AttendanceApi } from '../../core/api/attendance.api';
import { UserApi } from '../../core/api/user.api';
import { CurrentEventService } from '../../core/event/current-event.service';
import { EventResponse } from '../../core/models/event';
import { AttendeeResponse } from '../../core/models/attendance';
import { DatePollRange } from '../date-poll/date-poll-range';
import { DatePollCalendar } from '../date-poll/date-poll-calendar';
import { DatePollSummary } from '../date-poll/date-poll-summary';
import { OrganiserAvatarStack } from '../../shared/organiser-avatar-stack/organiser-avatar-stack';

interface EventForm {
  name: FormControl<string>;
  description: FormControl<string>;
  startDate: FormControl<string>;
  endDate: FormControl<string>;
  location: FormControl<string>;
  costPerNight: FormControl<number>;
}

@Component({
  selector: 'app-events-detail',
  imports: [
    ReactiveFormsModule,
    MatCardModule,
    MatChipsModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatIconModule,
    MatListModule,
    MatMenuModule,
    MatTooltipModule,
    CurrencyPipe,
    DatePollRange,
    DatePollCalendar,
    DatePollSummary,
    OrganiserAvatarStack,
  ],
  templateUrl: './events-detail.html',
  styleUrl: './events-detail.scss',
})
export class EventsDetail implements OnInit {
  private readonly eventApi = inject(EventApi);
  private readonly attendanceApi = inject(AttendanceApi);
  private readonly userApi = inject(UserApi);
  private readonly currentEventService = inject(CurrentEventService);
  private readonly router = inject(Router);
  private readonly snackBar = inject(MatSnackBar);
  private readonly dialog = inject(MatDialog);

  readonly event = this.currentEventService.event;
  readonly loading = this.currentEventService.loading;
  readonly saving = signal(false);
  readonly editing = signal(false);
  readonly currentUserId = signal<number | null>(null);
  readonly attendees = signal<AttendeeResponse[]>([]);
  readonly attendeesLoading = signal(false);
  readonly actioning = signal(false);
  readonly pending = computed(() => this.attendees().filter((a) => a.role === 'Pending'));
  readonly confirmed = computed(() => {
    const ownerId = this.event()?.createdById;
    const rank = (a: AttendeeResponse) =>
      a.userId === ownerId ? 0 : a.role === 'Organiser' ? 1 : 2;
    return this.attendees()
      .filter((a) => a.role !== 'Pending')
      .slice()
      .sort((a, b) => rank(a) - rank(b) || a.displayName.localeCompare(b.displayName));
  });
  readonly organisers = computed(() => this.attendees().filter((a) => a.role === 'Organiser'));
  readonly menuAttendee = signal<AttendeeResponse | null>(null);
  readonly form: FormGroup<EventForm>;

  @ViewChild(DatePollSummary) private datePollSummary?: DatePollSummary;

  constructor() {
    this.form = inject(FormBuilder).nonNullable.group({
      name: ['', [Validators.required, Validators.maxLength(200)]],
      description: [''],
      startDate: [''],
      endDate: [''],
      location: ['', Validators.required],
      costPerNight: [0, [Validators.required, Validators.min(0)]],
    });
  }

  ngOnInit(): void {
    this.userApi.getMe().subscribe((me) => this.currentUserId.set(me.id));
    const ev = this.event();
    if (ev) {
      this.loadAttendees(ev.id);
    }
  }

  get isOrganiser(): boolean {
    return this.event()?.currentUserRole === 'Organiser';
  }

  get isOwner(): boolean {
    const ev = this.event();
    const userId = this.currentUserId();
    return ev !== null && userId !== null && ev.createdById === userId;
  }

  get isConfirmed(): boolean {
    const role = this.event()?.currentUserRole;
    return role === 'Attendee' || role === 'Organiser';
  }

  attendeeLabel(attendee: AttendeeResponse): string {
    const ev = this.event();
    if (attendee.userId === ev?.createdById) {
      return `${attendee.displayName} · Event owner`;
    }
    if (attendee.role === 'Organiser') {
      return `${attendee.displayName} · Organiser`;
    }
    return attendee.displayName;
  }

  private loadEvent(id: string): void {
    this.currentEventService.loadEvent(id).subscribe({
      next: (event) => {
        // Members see the full roster; non-members get organisers only (backend-enforced).
        this.loadAttendees(event.id);
      },
      error: () => {
        // CurrentEventService already surfaces loading state; nothing else to do here.
      },
    });
  }

  private loadAttendees(eventId: string): void {
    this.attendeesLoading.set(true);
    this.attendanceApi.getAttendees(eventId).subscribe({
      next: (attendees) => {
        this.attendees.set(attendees);
        this.attendeesLoading.set(false);
      },
      error: () => this.attendeesLoading.set(false),
    });
  }

  join(): void {
    const ev = this.event();
    if (!ev) {
      return;
    }
    this.actioning.set(true);
    this.attendanceApi.join(ev.id).subscribe({
      next: () => {
        this.actioning.set(false);
        this.loadEvent(ev.id);
        this.snackBar.open('Joined — waiting for confirmation', 'Dismiss', { duration: 3000 });
      },
      error: (err: HttpErrorResponse) => {
        this.actioning.set(false);
        const message = err.error?.error ?? 'Could not join event';
        this.snackBar.open(message, 'Dismiss', { duration: 3000 });
      },
    });
  }

  leave(): void {
    const ev = this.event();
    const userId = this.currentUserId();
    if (!ev || userId === null) {
      return;
    }
    this.confirmAction({
      title: 'Leave event',
      message: `Are you sure you want to leave “${ev.name}”?`,
      confirmLabel: 'Leave',
    }).subscribe((ok) => {
      if (!ok) {
        return;
      }
      this.actioning.set(true);
      this.attendanceApi.remove(ev.id, userId).subscribe({
        next: () => {
          this.actioning.set(false);
          this.loadEvent(ev.id);
          this.snackBar.open('You left the event', 'Dismiss', { duration: 3000 });
        },
        error: () => {
          this.actioning.set(false);
          this.snackBar.open('Could not leave event', 'Dismiss', { duration: 3000 });
        },
      });
    });
  }

  confirm(userId: number): void {
    const ev = this.event();
    if (!ev) {
      return;
    }
    this.actioning.set(true);
    this.attendanceApi.confirm(ev.id, userId).subscribe({
      next: () => {
        this.actioning.set(false);
        this.loadAttendees(ev.id);
        this.snackBar.open('Attendee confirmed', 'Dismiss', { duration: 3000 });
      },
      error: () => {
        this.actioning.set(false);
        this.snackBar.open('Could not confirm attendee', 'Dismiss', { duration: 3000 });
      },
    });
  }

  removeAttendee(userId: number, displayName: string): void {
    const ev = this.event();
    if (!ev) {
      return;
    }
    this.confirmAction({
      title: 'Remove attendee',
      message: `Are you sure you want to remove ${displayName} from this event?`,
      confirmLabel: 'Remove',
    }).subscribe((ok) => {
      if (!ok) {
        return;
      }
      this.actioning.set(true);
      this.attendanceApi.remove(ev.id, userId).subscribe({
        next: () => {
          this.actioning.set(false);
          this.loadAttendees(ev.id);
          this.snackBar.open('Attendee removed', 'Dismiss', { duration: 3000 });
        },
        error: () => {
          this.actioning.set(false);
          this.snackBar.open('Could not remove attendee', 'Dismiss', { duration: 3000 });
        },
      });
    });
  }

  promoteToOrganiser(userId: number, displayName: string): void {
    const ev = this.event();
    if (!ev) {
      return;
    }
    this.actioning.set(true);
    this.attendanceApi.promote(ev.id, userId).subscribe({
      next: () => {
        this.actioning.set(false);
        this.loadAttendees(ev.id);
        this.snackBar.open(`${displayName} is now an organiser`, 'Dismiss', { duration: 3000 });
      },
      error: (err: HttpErrorResponse) => {
        this.actioning.set(false);
        const message = err.error?.error ?? 'Could not promote attendee';
        this.snackBar.open(message, 'Dismiss', { duration: 3000 });
      },
    });
  }

  demoteOrganiser(userId: number, displayName: string): void {
    const ev = this.event();
    if (!ev) {
      return;
    }
    this.confirmAction({
      title: 'Remove from organiser team',
      message: `Are you sure you want to remove ${displayName} from the organiser team? They'll remain a confirmed attendee.`,
      confirmLabel: 'Remove',
    }).subscribe((ok) => {
      if (!ok) {
        return;
      }
      this.actioning.set(true);
      this.attendanceApi.demote(ev.id, userId).subscribe({
        next: () => {
          this.actioning.set(false);
          this.loadAttendees(ev.id);
          this.snackBar.open(`${displayName} is no longer an organiser`, 'Dismiss', {
            duration: 3000,
          });
        },
        error: (err: HttpErrorResponse) => {
          this.actioning.set(false);
          const message = err.error?.error ?? 'Could not remove organiser';
          this.snackBar.open(message, 'Dismiss', { duration: 3000 });
        },
      });
    });
  }

  transferOwnership(userId: number, displayName: string): void {
    const ev = this.event();
    if (!ev) {
      return;
    }
    this.confirmAction({
      title: 'Transfer ownership',
      message: `Are you sure you want to make ${displayName} the owner of this event? You'll remain an organiser, but they'll gain full control, including deleting the event.`,
      confirmLabel: 'Transfer',
    }).subscribe((ok) => {
      if (!ok) {
        return;
      }
      this.actioning.set(true);
      this.attendanceApi.transferOwnership(ev.id, userId).subscribe({
        next: () => {
          this.actioning.set(false);
          this.loadEvent(ev.id);
          this.snackBar.open(`Ownership transferred to ${displayName}`, 'Dismiss', {
            duration: 3000,
          });
        },
        error: (err: HttpErrorResponse) => {
          this.actioning.set(false);
          const message = err.error?.error ?? 'Could not transfer ownership';
          this.snackBar.open(message, 'Dismiss', { duration: 3000 });
        },
      });
    });
  }

  deleteEvent(): void {
    const ev = this.event();
    if (!ev) {
      return;
    }
    this.confirmAction({
      title: 'Delete event',
      message: `Are you sure you want to permanently delete “${ev.name}”? This cannot be undone.`,
      confirmLabel: 'Delete',
    }).subscribe((ok) => {
      if (!ok) {
        return;
      }
      this.saving.set(true);
      this.eventApi.deleteEvent(ev.id).subscribe({
        next: () => {
          this.saving.set(false);
          this.currentEventService.clear();
          this.snackBar.open('Event deleted', 'Dismiss', { duration: 3000 });
          this.router.navigate(['/']);
        },
        error: () => {
          this.saving.set(false);
          this.snackBar.open('Could not delete event', 'Dismiss', { duration: 3000 });
        },
      });
    });
  }

  private confirmAction(data: ConfirmDialogData) {
    return this.dialog
      .open<ConfirmDialog, ConfirmDialogData, boolean>(ConfirmDialog, { data })
      .afterClosed();
  }

  viewPaymentMethods(userId: number, displayName: string): void {
    this.dialog.open<PaymentMethodsViewerDialog, PaymentMethodsViewerDialogData>(
      PaymentMethodsViewerDialog,
      { data: { userId, displayName } },
    );
  }

  startEdit(): void {
    const ev = this.event();
    if (!ev) {
      return;
    }
    this.form.setValue({
      name: ev.name,
      description: ev.description ?? '',
      startDate: ev.startDate ?? '',
      endDate: ev.endDate ?? '',
      location: ev.location,
      costPerNight: ev.costPerNight ?? 0,
    });
    this.editing.set(true);
  }

  cancelEdit(): void {
    this.editing.set(false);
  }

  save(): void {
    const ev = this.event();
    if (!ev || this.form.invalid) {
      return;
    }
    const raw = this.form.getRawValue();
    this.saving.set(true);
    this.eventApi
      .updateEvent(ev.id, {
        name: raw.name.trim(),
        description: raw.description.trim() || null,
        startDate: raw.startDate || null,
        endDate: raw.endDate || null,
        location: raw.location.trim(),
        costPerNight: raw.costPerNight,
      })
      .subscribe({
        next: (updated) => {
          this.setEvent(updated);
          this.saving.set(false);
          this.editing.set(false);
          this.snackBar.open('Event updated', 'Dismiss', { duration: 3000 });
        },
        error: () => {
          this.saving.set(false);
          this.snackBar.open('Could not update event', 'Dismiss', { duration: 3000 });
        },
      });
  }

  publish(): void {
    const ev = this.event();
    if (!ev) {
      return;
    }
    this.saving.set(true);
    this.eventApi.publishEvent(ev.id).subscribe({
      next: (updated) => {
        this.setEvent(updated);
        this.saving.set(false);
        this.snackBar.open('Event published', 'Dismiss', { duration: 3000 });
      },
      error: () => {
        this.saving.set(false);
        this.snackBar.open('Could not publish event', 'Dismiss', { duration: 3000 });
      },
    });
  }

  unpublish(): void {
    const ev = this.event();
    if (!ev) {
      return;
    }
    this.saving.set(true);
    this.eventApi.unpublishEvent(ev.id).subscribe({
      next: (updated) => {
        this.setEvent(updated);
        this.saving.set(false);
        this.snackBar.open('Event moved back to draft', 'Dismiss', { duration: 3000 });
      },
      error: () => {
        this.saving.set(false);
        this.snackBar.open('Could not unpublish event', 'Dismiss', { duration: 3000 });
      },
    });
  }

  close(): void {
    const ev = this.event();
    if (!ev) {
      return;
    }
    this.saving.set(true);
    this.eventApi.closeEvent(ev.id).subscribe({
      next: (updated) => {
        this.setEvent(updated);
        this.saving.set(false);
        this.snackBar.open('Event closed', 'Dismiss', { duration: 3000 });
      },
      error: () => {
        this.saving.set(false);
        this.snackBar.open('Could not close event', 'Dismiss', { duration: 3000 });
      },
    });
  }

  reopen(): void {
    const ev = this.event();
    if (!ev) {
      return;
    }
    this.saving.set(true);
    this.eventApi.reopenEvent(ev.id).subscribe({
      next: (updated) => {
        this.setEvent(updated);
        this.saving.set(false);
        this.snackBar.open('Event reopened', 'Dismiss', { duration: 3000 });
      },
      error: () => {
        this.saving.set(false);
        this.snackBar.open('Could not reopen event', 'Dismiss', { duration: 3000 });
      },
    });
  }

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

  private setEvent(event: EventResponse): void {
    this.currentEventService.setEvent(event);
  }
}
