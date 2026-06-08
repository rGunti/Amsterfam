import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { OAuthService } from 'angular-oauth2-oidc';

import { environment } from '../../../environments/environment';

// Must match Amsterfam.Api.Auth.TestAuthHandler.UserIdHeader on the backend.
const TEST_USER_ID_HEADER = 'X-Test-User-ExternalId';

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  // UserApi (and friends) build absolute URLs from environment.apiAddress, so
  // requests look like `http://localhost:5293/api/v1/me`, not `/api/...`.
  if (!req.url.startsWith(`${environment.apiAddress}/api`)) {
    return next(req);
  }

  if (environment.useFakeAuth) {
    return next(
      req.clone({
        setHeaders: {
          Authorization: `Bearer ${environment.fakeAuthToken}`,
          [TEST_USER_ID_HEADER]: environment.fakeAuthExternalId ?? '',
        },
      }),
    );
  }

  const oauthService = inject(OAuthService);
  const token = oauthService.getAccessToken();

  if (!token) {
    return next(req);
  }

  return next(
    req.clone({
      setHeaders: { Authorization: `Bearer ${token}` },
    }),
  );
};
