import { Component, OnInit, signal } from '@angular/core';
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

import { UserApi } from '../../core/api/user.api';
import { User } from '../../core/models/user';

@Component({
  selector: 'app-profile',
  imports: [
    ReactiveFormsModule,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
  ],
  templateUrl: './profile.html',
  styleUrl: './profile.scss',
})
export class Profile implements OnInit {
  readonly user = signal<User | null>(null);
  readonly saving = signal(false);
  readonly form: FormGroup<{ displayName: FormControl<string> }>;

  constructor(
    formBuilder: FormBuilder,
    private readonly userApi: UserApi,
    private readonly snackBar: MatSnackBar,
  ) {
    this.form = formBuilder.nonNullable.group({
      displayName: ['', [Validators.required, Validators.maxLength(100)]],
    });
  }

  ngOnInit(): void {
    this.userApi.getMe().subscribe((user) => {
      this.user.set(user);
      this.form.setValue({ displayName: user.displayName });
    });
  }

  save(): void {
    const currentUser = this.user();
    if (!currentUser || this.form.invalid) {
      return;
    }

    this.saving.set(true);
    this.userApi
      .updateMe({
        displayName: this.form.getRawValue().displayName.trim(),
        avatarUrl: currentUser.avatarUrl,
      })
      .subscribe({
        next: (updated) => {
          this.user.set(updated);
          this.saving.set(false);
          this.snackBar.open('Profile updated', 'Dismiss', { duration: 3000 });
        },
        error: () => {
          this.saving.set(false);
          this.snackBar.open('Could not update profile', 'Dismiss', { duration: 3000 });
        },
      });
  }
}
