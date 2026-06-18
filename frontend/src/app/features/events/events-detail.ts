import { Component, OnInit, inject, signal } from '@angular/core';
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
import { MatSnackBar } from '@angular/material/snack-bar';
import { CurrencyPipe } from '@angular/common';

import { EventApi } from '../../core/api/event.api';
import { EventResponse } from '../../core/models/event';

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
    CurrencyPipe,
  ],
  templateUrl: './events-detail.html',
  styleUrl: './events-detail.scss',
})
export class EventsDetail implements OnInit {
  private readonly eventApi = inject(EventApi);
  private readonly route = inject(ActivatedRoute);
  private readonly snackBar = inject(MatSnackBar);

  readonly event = signal<EventResponse | null>(null);
  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly editing = signal(false);
  readonly form: FormGroup<EventForm>;

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
    this.eventApi.getEvent(id).subscribe({
      next: (event) => {
        this.setEvent(event);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  get isOrganiser(): boolean {
    return this.event()?.currentUserRole === 'Organiser';
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

  private setEvent(event: EventResponse): void {
    this.event.set(event);
  }
}
