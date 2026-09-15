import {
  ApplicationConfig,
  inject,
  isDevMode,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
} from '@angular/core';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { Router, provideRouter, withNavigationErrorHandler } from '@angular/router';
import { provideServiceWorker } from '@angular/service-worker';
import { OAuthStorage, provideOAuthClient } from 'angular-oauth2-oidc';

import { routes } from './app.routes';
import { authInterceptor } from './core/auth/auth.interceptor';
import { AuthService } from './core/auth/auth.service';
import { provideEnvironment } from '../environments/environment.model';
import { environment } from '../environments/environment';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(
      routes,
      withNavigationErrorHandler((event) => {
        const message = event.error instanceof Error ? event.error.message : String(event.error);
        const isChunkLoadFailure = /dynamically imported module|Loading chunk/i.test(message);
        if (isChunkLoadFailure) {
          inject(Router).navigateByUrl('/offline');
        } else {
          console.error(event.error);
        }
      }),
    ),
    provideHttpClient(withInterceptors([authInterceptor])),
    provideOAuthClient(),
    // Tokens default to sessionStorage otherwise, which is wiped whenever the
    // PWA/tab is closed and forces a fresh interactive login on every launch
    // even though the refresh token is still valid for days. See issue #85.
    { provide: OAuthStorage, useFactory: () => localStorage },
    provideEnvironment(environment),
    provideAppInitializer(() => inject(AuthService).init()),
    provideServiceWorker('ngsw-worker.js', {
      enabled: !isDevMode(),
      registrationStrategy: 'registerWhenStable:30000',
    }),
  ],
};
