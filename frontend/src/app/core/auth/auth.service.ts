import { Injectable, inject, signal } from '@angular/core';
import { OAuthService } from 'angular-oauth2-oidc';

import { environment } from '../../../environments/environment';
import { authConfig } from './auth.config';

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
    this.isAuthenticated.set(this.oauthService.hasValidAccessToken());
  }

  login(): void {
    if (environment.useFakeAuth) {
      this.isAuthenticated.set(true);
      return;
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
