import { Environment } from './environment.model';

export const environment: Environment = {
  apiAddress: 'http://localhost:5293',
  useFakeAuth: true,
  fakeAuthToken: 'e2e-fake-token',
  fakeAuthExternalId: 'e2e-user-1',
};
