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
import { MatSelectModule } from '@angular/material/select';
import { MatButtonModule } from '@angular/material/button';
import { MatSnackBar } from '@angular/material/snack-bar';
import { HttpErrorResponse } from '@angular/common/http';

import { EventApi } from '../../core/api/event.api';
import { localIsoDate } from '../../shared/local-date';
import { EVENT_NAME_MAX_LENGTH } from '../../core/models/event';
import { CURRENCIES, DEFAULT_CURRENCY, currencyName } from '../../shared/money';

interface EventForm {
  name: FormControl<string>;
  description: FormControl<string>;
  startDate: FormControl<string>;
  endDate: FormControl<string>;
  location: FormControl<string>;
  currency: FormControl<string>;
}

@Component({
  selector: 'app-events-create',
  imports: [
    RouterLink,
    ReactiveFormsModule,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
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
  readonly nameMaxLength = EVENT_NAME_MAX_LENGTH;
  readonly currencies = CURRENCIES;
  readonly currencyName = currencyName;
  readonly currencyHint = 'Expenses are tracked in this currency';
  readonly form: FormGroup<EventForm>;

  constructor() {
    this.form = inject(FormBuilder).nonNullable.group({
      name: ['', [Validators.required, Validators.maxLength(EVENT_NAME_MAX_LENGTH)]],
      description: [''],
      startDate: [''],
      endDate: [''],
      location: ['', Validators.required],
      currency: [DEFAULT_CURRENCY],
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
        currency: raw.currency,
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
