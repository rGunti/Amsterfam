import { Component, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import {
  FormBuilder,
  FormControl,
  FormGroup,
  ReactiveFormsModule,
  Validators,
} from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatSnackBar } from '@angular/material/snack-bar';
import { HttpErrorResponse } from '@angular/common/http';

import { EventApi } from '../../core/api/event.api';
import { localIsoDate } from '../../shared/local-date';

interface EventForm {
  name: FormControl<string>;
  description: FormControl<string>;
  startDate: FormControl<string>;
  endDate: FormControl<string>;
  location: FormControl<string>;
}

@Component({
  selector: 'app-events-create',
  imports: [
    RouterLink,
    ReactiveFormsModule,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
  ],
  templateUrl: './events-create.html',
  styleUrl: './events-create.scss',
})
export class EventsCreate {
  /** Local "yyyy-MM-dd", the earliest start date the backend accepts. */
  readonly today = localIsoDate();
  private readonly eventApi = inject(EventApi);
  private readonly router = inject(Router);
  private readonly snackBar = inject(MatSnackBar);

  readonly saving = signal(false);
  readonly form: FormGroup<EventForm>;

  constructor() {
    this.form = inject(FormBuilder).nonNullable.group({
      name: ['', [Validators.required, Validators.maxLength(200)]],
      description: [''],
      startDate: [''],
      endDate: [''],
      location: ['', Validators.required],
    });
  }

  save(): void {
    if (this.form.invalid) {
      return;
    }
    const raw = this.form.getRawValue();
    this.saving.set(true);
    this.eventApi
      .createEvent({
        name: raw.name.trim(),
        description: raw.description.trim() || null,
        startDate: raw.startDate || null,
        endDate: raw.endDate || null,
        location: raw.location.trim(),
        costPerNight: null,
      })
      .subscribe({
        next: (created) => {
          this.saving.set(false);
          this.snackBar.open('Event created', 'Dismiss', { duration: 3000 });
          this.router.navigate(['/events', created.id]);
        },
        error: (err: HttpErrorResponse) => {
          this.saving.set(false);
          const message = err.error?.error ?? 'Could not create event';
          this.snackBar.open(message, 'Dismiss', { duration: 3000 });
        },
      });
  }
}
