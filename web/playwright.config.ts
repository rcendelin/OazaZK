import { defineConfig, devices } from '@playwright/test';

/**
 * E2E smoke tests (X6). They run against the production build served by
 * `vite preview`; the backend is not started — every test mocks the API it
 * needs through `mockApi` (e2e/mockApi.ts), so the suite runs in CI without Azure.
 * Build first: `npm run build`, then `npx playwright test`.
 */
export default defineConfig({
  testDir: './e2e',
  timeout: 30_000,
  retries: process.env.CI ? 1 : 0,
  reporter: process.env.CI ? [['list'], ['html', { open: 'never' }]] : 'list',
  use: {
    baseURL: 'http://localhost:4173',
    trace: 'retain-on-failure',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
  webServer: {
    command: 'npm run preview -- --port 4173 --strictPort',
    url: 'http://localhost:4173',
    reuseExistingServer: !process.env.CI,
  },
});
