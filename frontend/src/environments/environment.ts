import packageJson from '../../package.json';
import { Environment } from './environment.model';
import { APP_SHA } from './version';

export const environment: Environment = {
  apiAddress: 'http://localhost:5293',
  useFakeAuth: false,
  version: packageJson.version,
  sha: APP_SHA,
};
