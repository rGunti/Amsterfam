import { bootstrapApplication } from '@angular/platform-browser';
import { appConfig } from './app/app.config';
import { App } from './app/app';
import { authConfig } from './app/core/auth/auth.config';
import { loadRuntimeConfig } from './app/core/config/runtime-config';
import { environment } from './environments/environment';

async function main(): Promise<void> {
  // Done here, before bootstrap, rather than in an APP_INITIALIZER — inject()
  // (used by AuthService's constructor to read authConfig) must run
  // synchronously, and an await inside an initializer breaks that (NG0203).
  if (!environment.useFakeAuth) {
    const config = await loadRuntimeConfig();
    authConfig.issuer = config.oidcIssuer;
  }
  await bootstrapApplication(App, appConfig);
}

main().catch((err) => console.error(err));
