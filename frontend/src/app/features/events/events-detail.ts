import { Component, OnInit, ViewChild, computed, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
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
import { EventResponse } from '../../core/models/event';
import { AttendeeResponse } from '../../core/models/attendance';
import { DatePollRange } from '../date-poll/date-poll-range';
import { DatePollCalendar } from '../date-poll/date-poll-calendar';
import { DatePollSummary } from '../date-poll/date-poll-summary';

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
    CurrencyPipe,
    DatePollRange,
    DatePollCalendar,
    DatePollSummary,
  ],
  templateUrl: './events-detail.html',
  styleUrl: './events-detail.scss',
})
export class EventsDetail implements OnInit {
  private readonly eventApi = inject(EventApi);
  private readonly attendanceApi = inject(AttendanceApi);
  private readonly userApi = inject(UserApi);
  private readonly route = inject(ActivatedRoute);
  private readonly snackBar = inject(MatSnackBar);
  private readonly dialog = inject(MatDialog);

  readonly event = signal<EventResponse | null>(null);
  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly editing = signal(false);
  readonly currentUserId = signal<number | null>(null);
  readonly attendees = signal<AttendeeResponse[]>([]);
  readonly attendeesLoading = signal(false);
  readonly actioning = signal(false);
  readonly pending = computed(() => this.attendees().filter((a) => a.role === 'Pending'));
  readonly confirmed = computed(() => this.attendees().filter((a) => a.role !== 'Pending'));
  readonly form: FormGroup<EventForm>;

  @ViewChild(DatePollSummary) private datePollSummary?: DatePollSummary;

  constructor() {
    this.form = inject(FormBuilder).nonNullable.group({
      name: ['', [Validators.required, Validators.maxLength(200)]],
      description: [''],
      startDate: ['', Validators.required],
      endDate: ['', Validators.required],
      location: ['', Validators.required],
      costPerNight: [0, [Validators.required, Validators.min(0)]],
    });
  }

  ngOnInit(): void {
    const id = Number(this.route.snapshot.paramMap.get('id'));
    this.userApi.getMe().subscribe((me) => this.currentUserId.set(me.id));
    this.loadEvent(id);
  }

  get isOrganiser(): boolean {
    return this.event()?.currentUserRole === 'Organiser';
  }

  get isConfirmed(): boolean {
    const role = this.event()?.currentUserRole;
    return role === 'Attendee' || role === 'Organiser';
  }

  private loadEvent(id: number): void {
    this.eventApi.getEvent(id).subscribe({
      next: (event) => {
        this.setEvent(event);
        this.loading.set(false);
        // Confirmed attendees (Attendee/Organiser) may view the roster.
        if (event.currentUserRole === 'Attendee' || event.currentUserRole === 'Organiser') {
          this.loadAttendees(event.id);
        }
      },
      error: () => this.loading.set(false),
    });
  }

  private loadAttendees(eventId: number): void {
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
      startDate: ev.startDate,
      endDate: ev.endDate,
      location: ev.location,
      costPerNight: ev.costPerNight,
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
        startDate: raw.startDate,
        endDate: raw.endDate,
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
    this.event.set({
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
    this.event.set(event);
  }
}
