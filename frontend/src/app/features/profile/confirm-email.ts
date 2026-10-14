import { Component, OnInit, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar } from '@angular/material/snack-bar';
import { firstValueFrom } from 'rxjs';

import { UserApi } from '../../core/api/user.api';

/**
 * Where the link in the confirmation email lands (ADR-018). Posts the token while signed in,
 * then goes back to the profile page. The token is taken out of the address bar right away.
 */
@Component({
  selector: 'app-confirm-email',
  imports: [RouterLink, MatButtonModule, MatCardModule, MatProgressSpinnerModule],
  templateUrl: './confirm-email.html',
  styleUrl: './confirm-email.scss',
})
export class ConfirmEmail implements OnInit {
  private readonly api = inject(UserApi);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly snackBar = inject(MatSnackBar);

  readonly failed = signal(false);

  async ngOnInit(): Promise<void> {
    const token = this.route.snapshot.queryParamMap.get('token');
    await this.router.navigate([], { queryParams: {}, replaceUrl: true });
    if (!token) {
      this.failed.set(true);
      return;
    }
    try {
      const settings = await firstValueFrom(this.api.confirmNotificationEmail(token));
      this.snackBar.open(`News will now be emailed to ${settings.effectiveEmail}`, 'Dismiss', {
        duration: 5000,
      });
      await this.router.navigateByUrl('/profile', { replaceUrl: true });
    } catch {
      this.failed.set(true);
    }
  }
}
