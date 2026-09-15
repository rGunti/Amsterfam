import { Component, inject } from '@angular/core';
import { Router } from '@angular/router';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';

import { RETURN_URL_KEY } from '../../core/auth/auth.service';

@Component({
  selector: 'app-auth-callback',
  imports: [MatProgressSpinnerModule],
  templateUrl: './auth-callback.html',
  styleUrl: './auth-callback.scss',
})
export class AuthCallback {
  constructor() {
    const router = inject(Router);

    const returnUrl = sessionStorage.getItem(RETURN_URL_KEY);
    sessionStorage.removeItem(RETURN_URL_KEY);

    router.navigateByUrl(returnUrl && returnUrl !== '/auth/callback' ? returnUrl : '/');
  }
}
