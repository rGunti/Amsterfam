import { Component, OnInit, input, output, inject, signal } from '@angular/core';
import {
  FormBuilder,
  FormControl,
  FormGroup,
  ReactiveFormsModule,
  Validators,
} from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSnackBar } from '@angular/material/snack-bar';

import { DatePollApi } from '../../core/api/date-poll.api';
import { EventResponse } from '../../core/models/event';
import { DatePollSummaryResponse } from '../../core/models/date-poll';

interface RangeForm {
  pollRangeStart: FormControl<string>;
  pollRangeEnd: FormControl<string>;
}

@Component({
  selector: 'app-date-poll-range',
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatIconModule,
  ],
  templateUrl: './date-poll-range.html',
  styleUrl: './date-poll-range.scss',
})
export class DatePollRange implements OnInit {
  private readonly datePollApi = inject(DatePollApi);
  private readonly snackBar = inject(MatSnackBar);

  readonly event = input.required<EventResponse>();
  readonly rangeSaved = output<DatePollSummaryResponse>();

  readonly saving = signal(false);
  readonly form: FormGroup<RangeForm>;

  constructor() {
    this.form = inject(FormBuilder).nonNullable.group({
      pollRangeStart: ['', Validators.required],
      pollRangeEnd: ['', Validators.required],
    });
  }

  ngOnInit(): void {
    const ev = this.event();
    this.form.setValue({
      pollRangeStart: ev.pollRangeStart ?? '',
      pollRangeEnd: ev.pollRangeEnd ?? '',
    });
  }

  save(): void {
    if (this.form.invalid) {
      return;
    }
    const raw = this.form.getRawValue();
    this.saving.set(true);
    this.datePollApi
      .setRange(this.event().id, {
        pollRangeStart: raw.pollRangeStart,
        pollRangeEnd: raw.pollRangeEnd,
      })
      .subscribe({
        next: (summary) => {
          this.saving.set(false);
          this.rangeSaved.emit(summary);
          this.snackBar.open('Poll range saved', 'Dismiss', { duration: 3000 });
        },
        error: () => {
          this.saving.set(false);
          this.snackBar.open('Could not save poll range', 'Dismiss', { duration: 3000 });
        },
      });
  }
}
