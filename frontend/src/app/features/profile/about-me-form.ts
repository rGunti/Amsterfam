import { Component, OnInit, computed, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import {
  AbstractControl,
  FormBuilder,
  ReactiveFormsModule,
  ValidationErrors,
  Validators,
} from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatChipsModule } from '@angular/material/chips';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';

import { UserApi } from '../../core/api/user.api';
import {
  DietaryOption,
  MAX_BIO_LENGTH,
  MAX_DIETARY_NOTES_LENGTH,
  MAX_LOCATION_LENGTH,
  MAX_PRONOUNS_LENGTH,
  UpdateAboutRequest,
  User,
} from '../../core/models/user';
import { APP_LOCALE } from '../../shared/app-locale';
import { daysInMonth } from '../../shared/birthday';

const PRONOUN_SUGGESTIONS = [
  'she/her',
  'he/him',
  'they/them',
  'she/they',
  'he/they',
  'any pronouns',
];

const MONTHS = Array.from({ length: 12 }, (_, i) => ({
  value: i + 1,
  label: new Intl.DateTimeFormat(APP_LOCALE, { month: 'long', timeZone: 'UTC' }).format(
    new Date(Date.UTC(2000, i, 1)),
  ),
}));

const MAX_YEAR = new Date().getFullYear();

/** The year to check days against, ignoring one that's half-typed or out of range. */
function usableYear(year: number | null | undefined): number | null {
  return year != null && Number.isInteger(year) && year >= 1900 && year <= MAX_YEAR ? year : null;
}

/** Day and month go together, and the day has to exist in that month (and year, if given). */
function birthdayValidator(group: AbstractControl): ValidationErrors | null {
  const { month, day, year } = group.value as {
    month: number | null;
    day: number | null;
    year: number | null;
  };
  if (month === null && day === null) {
    return year === null ? null : { birthdayIncomplete: true };
  }
  if (month === null || day === null) {
    return { birthdayIncomplete: true };
  }
  return day > daysInMonth(month, usableYear(year)) ? { birthdayInvalid: true } : null;
}

@Component({
  selector: 'app-about-me-form',
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatCheckboxModule,
    MatChipsModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
  ],
  templateUrl: './about-me-form.html',
  styleUrl: './about-me-form.scss',
})
export class AboutMeForm implements OnInit {
  private readonly userApi = inject(UserApi);

  readonly user = input.required<User>();
  readonly saving = input(false);
  readonly save = output<UpdateAboutRequest>();
  readonly cancelled = output<void>();

  readonly pronounSuggestions = PRONOUN_SUGGESTIONS;
  readonly months = MONTHS;
  readonly maxYear = MAX_YEAR;
  readonly limits = {
    pronouns: MAX_PRONOUNS_LENGTH,
    location: MAX_LOCATION_LENGTH,
    bio: MAX_BIO_LENGTH,
    dietaryNotes: MAX_DIETARY_NOTES_LENGTH,
  };

  readonly dietaryOptions = signal<DietaryOption[]>([]);
  readonly dietaryOptionsFailed = signal(false);
  /** Ids rather than a FormArray: the options arrive after the form is built. */
  readonly selectedDietaryIds = signal<ReadonlySet<number>>(new Set());

  private readonly fb = inject(FormBuilder);
  readonly form = this.fb.group({
    pronouns: ['', [Validators.maxLength(MAX_PRONOUNS_LENGTH)]],
    location: ['', [Validators.maxLength(MAX_LOCATION_LENGTH)]],
    bio: ['', [Validators.maxLength(MAX_BIO_LENGTH)]],
    dietaryNotes: ['', [Validators.maxLength(MAX_DIETARY_NOTES_LENGTH)]],
    birthday: this.fb.group(
      {
        month: this.fb.control<number | null>(null),
        day: this.fb.control<number | null>(null),
        year: this.fb.control<number | null>(null, [
          // Whole four-digit years only; a decimal would otherwise reach the API and fail there.
          Validators.pattern(/^\d{4}$/),
          Validators.min(1900),
          Validators.max(this.maxYear),
        ]),
      },
      { validators: birthdayValidator },
    ),
  });

  private readonly birthdayValue = toSignal(this.form.controls.birthday.valueChanges, {
    initialValue: this.form.controls.birthday.value,
  });

  readonly days = computed(() => {
    const { month, year } = this.birthdayValue();
    const count = month ? daysInMonth(month, usableYear(year)) : 31;
    return Array.from({ length: count }, (_, i) => i + 1);
  });

  constructor() {
    // A day that no longer exists after changing month or year (31 → February) would sit in
    // the select invisibly; clear it so the empty field asks for a new pick instead.
    this.form.controls.birthday.valueChanges
      .pipe(takeUntilDestroyed())
      .subscribe(({ month, day, year }) => {
        if (month && day && day > daysInMonth(month, usableYear(year))) {
          this.form.controls.birthday.controls.day.setValue(null);
        }
      });
  }

  ngOnInit(): void {
    const user = this.user();
    this.form.setValue({
      pronouns: user.pronouns ?? '',
      location: user.location ?? '',
      bio: user.bio ?? '',
      dietaryNotes: user.dietaryNotes ?? '',
      birthday: {
        month: user.birthday?.month ?? null,
        day: user.birthday?.day ?? null,
        year: user.birthday?.year ?? null,
      },
    });
    this.selectedDietaryIds.set(new Set(user.dietaryOptions.map((o) => o.id)));

    this.userApi.getDietaryOptions().subscribe({
      next: (options) => this.dietaryOptions.set(options),
      error: () => this.dietaryOptionsFailed.set(true),
    });
  }

  usePronouns(pronouns: string): void {
    this.form.controls.pronouns.setValue(pronouns);
    this.form.controls.pronouns.markAsDirty();
  }

  toggleDietary(id: number, checked: boolean): void {
    const next = new Set(this.selectedDietaryIds());
    if (checked) {
      next.add(id);
    } else {
      next.delete(id);
    }
    this.selectedDietaryIds.set(next);
  }

  clearBirthday(): void {
    this.form.controls.birthday.setValue({ month: null, day: null, year: null });
  }

  submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const value = this.form.getRawValue();
    const { month, day, year } = value.birthday;
    this.save.emit({
      pronouns: blankToNull(value.pronouns),
      location: blankToNull(value.location),
      bio: blankToNull(value.bio),
      dietaryNotes: blankToNull(value.dietaryNotes),
      birthday: month !== null && day !== null ? { month, day, year: year ?? null } : null,
      dietaryOptionIds: [...this.selectedDietaryIds()],
    });
  }
}

function blankToNull(value: string | null): string | null {
  const trimmed = value?.trim() ?? '';
  return trimmed.length > 0 ? trimmed : null;
}
