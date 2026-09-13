import { Injectable, effect, inject, signal } from '@angular/core';

import { AuthService } from '../auth/auth.service';
import { User } from '../models/user';
import { UserApi } from './user.api';

@Injectable({ providedIn: 'root' })
export class CurrentUserService {
  private readonly userApi = inject(UserApi);
  private readonly authService = inject(AuthService);

  private readonly _user = signal<User | null>(null);
  readonly user = this._user.asReadonly();

  constructor() {
    effect(() => {
      if (this.authService.isAuthenticated()) {
        this.refresh();
      } else {
        this._user.set(null);
      }
    });
  }

  refresh(): void {
    this.userApi.getMe().subscribe({
      next: (user) => this._user.set(user),
      error: () => {
        // Keep whatever we last had (e.g. cached offline) rather than clearing it on a transient error.
      },
    });
  }

  setUser(user: User): void {
    this._user.set(user);
  }
}
