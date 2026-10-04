import { Component, computed, effect, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { Location } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { Observable, catchError, concat, map, of, switchMap } from 'rxjs';

import { CurrentUserService } from '../../core/api/current-user.service';
import { UserApi } from '../../core/api/user.api';
import { UserProfile as UserProfileModel } from '../../core/models/user';
import { UserProfileDetails } from '../../shared/user-profile-details/user-profile-details';

type ProfileState =
  | { kind: 'loading' }
  | { kind: 'loaded'; profile: UserProfileModel }
  | { kind: 'unavailable' };

/** Someone else's profile, read-only. Only people on a trip with them can open it. */
@Component({
  selector: 'app-user-profile',
  imports: [MatButtonModule, MatCardModule, MatIconModule, UserProfileDetails],
  templateUrl: './user-profile.html',
  styleUrl: './user-profile.scss',
})
export class UserProfile {
  private readonly userApi = inject(UserApi);
  private readonly currentUser = inject(CurrentUserService).user;
  private readonly router = inject(Router);
  private readonly location = inject(Location);

  // Follows the route rather than a snapshot: going from one profile straight to another
  // reuses this component.
  private readonly userIdChanges = inject(ActivatedRoute).paramMap.pipe(
    map((params) => Number(params.get('id'))),
  );
  private readonly userId = toSignal(this.userIdChanges, { requireSync: true });

  private readonly state = toSignal(this.userIdChanges.pipe(switchMap((id) => this.load(id))), {
    initialValue: { kind: 'loading' } as ProfileState,
  });

  readonly loading = computed(() => this.state().kind === 'loading');
  readonly unavailable = computed(() => this.state().kind === 'unavailable');
  readonly profile = computed(() => {
    const state = this.state();
    return state.kind === 'loaded' ? state.profile : null;
  });

  constructor() {
    // Your own profile lives at /profile, where it's editable. Waits for the current user to
    // load, so opening your own link directly still redirects.
    effect(() => {
      if (this.currentUser()?.id === this.userId()) {
        void this.router.navigate(['/profile'], { replaceUrl: true });
      }
    });
  }

  back(): void {
    this.location.back();
  }

  private load(id: number): Observable<ProfileState> {
    if (!Number.isInteger(id)) {
      return of({ kind: 'unavailable' });
    }
    return concat(
      of<ProfileState>({ kind: 'loading' }),
      this.userApi.getProfile(id).pipe(
        map((profile): ProfileState => ({ kind: 'loaded', profile })),
        catchError(() => of<ProfileState>({ kind: 'unavailable' })),
      ),
    );
  }
}
