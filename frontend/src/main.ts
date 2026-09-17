import { bootstrapApplication } from '@angular/platform-browser';
import { appConfig } from './app/app.config';
import { App } from './app/app';
import { authConfig } from './app/core/auth/auth.config';
import { loadRuntimeConfig } from './app/core/config/runtime-config';
import { environment } from './environments/environment';

async function main(): Promise<void> {
  // Must run before bootstrap: an `await` inside an Angular APP_INITIALIZER
  // breaks the injection context, so any inject() call after it throws
  // NG0203.
  if (!environment.useFakeAuth) {
    const config = await loadRuntimeConfig();
    authConfig.issuer = config.oidcIssuer;
  }
  await bootstrapApplication(App, appConfig);
}

main().catch((err) => console.error(err));
