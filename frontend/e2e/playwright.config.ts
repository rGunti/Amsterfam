import { defineConfig, devices } from '@playwright/test';

const isCI = !!process.env.CI;

export default defineConfig({
  testDir: './tests',
  fullyParallel: true,
  forbidOnly: isCI,
  retries: isCI ? 2 : 0,
  reporter: 'html',
  use: {
    baseURL: 'http://localhost:4200',
    trace: 'on-first-retry',
    screenshot: 'only-on-failure',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
  // Boots the full real stack (db -> backend -> frontend) so specs run against
  // a live API and Postgres, not mocks. Order matters: each entry's readiness
  // check must pass before the next one starts.
  // Note: `cwd` is resolved relative to this config file's directory (frontend/e2e/).
  webServer: [
    // In CI, Postgres is provided by a GitHub Actions service container on the
    // same port — starting another one via docker compose would conflict.
    ...(isCI
      ? []
      : [
          {
            // Foreground process (no -d): Playwright owns its lifecycle and tears
            // it down with the others. TCP-port polling is the readiness check
            // since Postgres has no HTTP endpoint to probe.
            command: 'docker compose -f ../../infra/docker-compose.yml up db',
            port: 5432,
            reuseExistingServer: true,
            timeout: 120_000,
          },
        ]),
    {
      // --no-launch-profile skips launchSettings.json (which hardcodes
      // ASPNETCORE_ENVIRONMENT=Development), so the URL must be set explicitly too.
      command: 'dotnet run --no-launch-profile',
      cwd: '../../backend/Amsterfam/Amsterfam.Api',
      env: { ASPNETCORE_ENVIRONMENT: 'E2E', ASPNETCORE_URLS: 'http://localhost:5293' },
      url: 'http://localhost:5293/health',
      reuseExistingServer: !isCI,
      timeout: 120_000,
    },
    {
      command: 'pnpm run start:e2e',
      cwd: '..',
      url: 'http://localhost:4200',
      reuseExistingServer: !isCI,
      timeout: 120_000,
    },
  ],
});
