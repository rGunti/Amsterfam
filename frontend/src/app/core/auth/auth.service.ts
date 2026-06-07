import { Injectable, signal } from '@angular/core';
import { OAuthService } from 'angular-oauth2-oidc';

import { authConfig } from './auth.config';

@Injectable({ providedIn: 'root' })
export class AuthService {
  readonly isAuthenticated = signal(false);

  constructor(private readonly oauthService: OAuthService) {
    this.oauthService.configure(authConfig);
  }

  async init(): Promise<void> {
    this.oauthService.setupAutomaticSilentRefresh();
    await this.oauthService.loadDiscoveryDocumentAndTryLogin();
    this.isAuthenticated.set(this.oauthService.hasValidAccessToken());
  }

  login(): void {
    this.oauthService.initCodeFlow();
  }

  logout(): void {
    this.oauthService.logOut();
    this.isAuthenticated.set(false);
  }
}
