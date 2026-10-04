import { Component, OnInit, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { Location } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';

import { CurrentUserService } from '../../core/api/current-user.service';
import { UserApi } from '../../core/api/user.api';
import { UserProfile as UserProfileModel } from '../../core/models/user';
import { UserProfileDetails } from '../../shared/user-profile-details/user-profile-details';

/** Someone else's profile, read-only. Only fellow members of a trip can open it. */
@Component({
  selector: 'app-user-profile',
  imports: [MatButtonModule, MatCardModule, MatIconModule, UserProfileDetails],
  templateUrl: './user-profile.html',
  styleUrl: './user-profile.scss',
})
export class UserProfile implements OnInit {
  private readonly userApi = inject(UserApi);
  private readonly currentUser = inject(CurrentUserService).user;
  private readonly router = inject(Router);
  private readonly location = inject(Location);
  private readonly userId = Number(inject(ActivatedRoute).snapshot.paramMap.get('id'));

  readonly profile = signal<UserProfileModel | null>(null);
  readonly loading = signal(true);
  readonly unavailable = signal(false);

  ngOnInit(): void {
    if (this.currentUser()?.id === this.userId) {
      void this.router.navigate(['/profile'], { replaceUrl: true });
      return;
    }
    if (!Number.isInteger(this.userId)) {
      this.loading.set(false);
      this.unavailable.set(true);
      return;
    }
    this.userApi.getProfile(this.userId).subscribe({
      next: (profile) => {
        this.profile.set(profile);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.unavailable.set(true);
      },
    });
  }

  back(): void {
    this.location.back();
  }
}
