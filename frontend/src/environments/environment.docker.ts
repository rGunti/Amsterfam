import { Environment } from './environment.model';
import { APP_SHA, APP_VERSION } from './version';

// Used for every Docker-built image (dev "full" profile and prod alike) —
// API calls are proxied same-origin by nginx (see ../../nginx.conf), so no
// per-deploy host is baked in here. OIDC issuer is resolved at container
// startup instead, see core/config/runtime-config.ts.
export const environment: Environment = {
  apiAddress: '',
  useFakeAuth: false,
  version: APP_VERSION,
  sha: APP_SHA,
};
