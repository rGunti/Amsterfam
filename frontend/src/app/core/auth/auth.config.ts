import { AuthConfig } from 'angular-oauth2-oidc';

export const authConfig: AuthConfig = {
  issuer: 'http://localhost:9000/application/o/amsterfam/',
  clientId: 'amsterfam',
  redirectUri: window.location.origin + '/auth/callback',
  responseType: 'code',
  scope: 'openid profile email',
  showDebugInformation: true,
  strictDiscoveryDocumentValidation: false,
};
