import { defineConfig, devices } from '@playwright/test';

/**
 * Live E2E against a deployed environment (default DEV) — NOT run in CI. Real frontend, real API, real storage.
 * Needs env: OAZA_LIVE_JWT_SECRET + OAZA_LIVE_JWT_ISSUER (the environment's JwtSecret/JwtIssuer — to sign short-lived
 * tokens for the E2E users) and the E2E users in its Users table (see e2e-live/README.md). The suite writes data:
 * back the storage up before and restore it after (api/tools/Oaza.StorageBackup). Tests run serially in file order.
 */
export default defineConfig({
  testDir: './e2e-live',
  timeout: 90_000,
  expect: { timeout: 20_000 },
  fullyParallel: false,
  workers: 1,
  retries: 0,
  reporter: [['list'], ['json', { outputFile: process.env.OAZA_LIVE_REPORT ?? 'test-results/live-report.json' }]],
  use: {
    baseURL: process.env.OAZA_LIVE_APP_URL ?? 'https://oaza-dev.cendelinovi.cz',
    locale: 'cs-CZ',
    timezoneId: 'Europe/Prague',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    actionTimeout: 20_000,
    navigationTimeout: 45_000,
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
});
