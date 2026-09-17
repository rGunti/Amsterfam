import { Environment } from './environment.model';
import { APP_SHA, APP_VERSION } from './version';

export const environment: Environment = {
  apiAddress: 'http://localhost:5293',
  useFakeAuth: false,
  version: APP_VERSION,
  sha: APP_SHA,
};
