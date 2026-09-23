import {
  ApplicationConfig,
  LOCALE_ID,
  inject,
  isDevMode,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
} from '@angular/core';
import { registerLocaleData } from '@angular/common';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import localeEnGb from '@angular/common/locales/en-GB';
import { Router, provideRouter, withNavigationErrorHandler } from '@angular/router';
import { provideServiceWorker } from '@angular/service-worker';
import { OAuthStorage, provideOAuthClient } from 'angular-oauth2-oidc';

import { routes } from './app.routes';
import { authInterceptor } from './core/auth/auth.interceptor';
import { AuthService } from './core/auth/auth.service';
import { provideEnvironment } from '../environments/environment.model';
import { environment } from '../environments/environment';
import { APP_LOCALE } from './shared/app-locale';

registerLocaleData(localeEnGb);

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    { provide: LOCALE_ID, useValue: APP_LOCALE },
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
    // Runtime config (see main.ts) is already applied to authConfig by the
    // time this runs — inject() must stay synchronous here (no await before
    // it), or Angular loses the injection context (NG0203).
    provideAppInitializer(() => inject(AuthService).init()),
    provideServiceWorker('ngsw-worker.js', {
      enabled: !isDevMode(),
      registrationStrategy: 'registerWhenStable:30000',
    }),
  ],
};
