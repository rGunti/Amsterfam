import { Environment } from './environment.model';
import { APP_SHA, APP_VERSION } from './version';

export const environment: Environment = {
  apiAddress: 'http://localhost:5293',
  useFakeAuth: true,
  fakeAuthToken: 'e2e-fake-token',
  fakeAuthExternalId: 'e2e-user-1',
  version: APP_VERSION,
  sha: APP_SHA,
};
