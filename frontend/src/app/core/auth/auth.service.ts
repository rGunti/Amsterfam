import { Injectable, inject, signal } from '@angular/core';
import { OAuthService } from 'angular-oauth2-oidc';

import { environment } from '../../../environments/environment';
import { authConfig } from './auth.config';

export const RETURN_URL_KEY = 'auth.returnUrl';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly oauthService = inject(OAuthService);

  readonly isAuthenticated = signal(false);

  constructor() {
    if (!environment.useFakeAuth) {
      this.oauthService.configure(authConfig);
    }
  }

  async init(): Promise<void> {
    if (environment.useFakeAuth) {
      // E2E mode: skip the real OIDC flow entirely — auth.interceptor attaches
      // a fixed test token/header that the backend's TestAuthHandler accepts.
      this.isAuthenticated.set(true);
      return;
    }

    this.oauthService.setupAutomaticSilentRefresh();
    await this.oauthService.loadDiscoveryDocumentAndTryLogin();

    // setupAutomaticSilentRefresh() only arms a timer that fires before the
    // currently loaded token's expiry — it does nothing for a token that is
    // already expired by the time the app is opened (e.g. reopened the next
    // day). If we still have a refresh token, try it eagerly before falling
    // back to an interactive login.
    if (!this.oauthService.hasValidAccessToken() && !!this.oauthService.getRefreshToken()) {
      try {
        await this.oauthService.refreshToken();
      } catch {
        // Refresh token expired/invalid — fall through to interactive login.
      }
    }

    this.isAuthenticated.set(this.oauthService.hasValidAccessToken());
  }

  login(returnUrl?: string): void {
    if (environment.useFakeAuth) {
      this.isAuthenticated.set(true);
      return;
    }

    if (returnUrl && returnUrl !== '/auth/callback') {
      sessionStorage.setItem(RETURN_URL_KEY, returnUrl);
    }

    this.oauthService.initCodeFlow();
  }

  logout(): void {
    if (environment.useFakeAuth) {
      this.isAuthenticated.set(false);
      return;
    }

    this.oauthService.logOut();
    this.isAuthenticated.set(false);
  }
}
