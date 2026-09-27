import type { Page, Request } from '@playwright/test';

export type Handler = (request: Request) => unknown;

/**
 * Serves every `/api/*` call from the given handlers (matched by path suffix,
 * longest first); anything else answers `[]` so list pages render empty.
 * Returns the requests the app made, for assertions.
 */
export async function mockApi(page: Page, handlers: Record<string, Handler>): Promise<Request[]> {
  const seen: Request[] = [];
  const keys = Object.keys(handlers).sort((a, b) => b.length - a.length);
  await page.route('**/api/**', async (route) => {
    const request = route.request();
    seen.push(request);
    const path = new URL(request.url()).pathname.replace(/^\/api/, '');
    const key = keys.find((k) => path === k || path.startsWith(`${k}?`));
    const body = key ? handlers[key](request) : [];
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) });
  });
  return seen;
}

export const adminUser = {
  id: 'u-admin',
  name: 'Admin Test',
  email: 'admin@example.cz',
  role: 'Admin',
  houseId: null,
  authMethod: 'MagicLink',
  lastLogin: null,
  notificationsEnabled: true,
};

/** Signs in through the magic-link page with a mocked token; the JWT lives in memory only. */
export async function signInAsAdmin(page: Page): Promise<void> {
  await page.goto('/auth/verify?token=e2e&email=admin%40example.cz');
  await page.waitForURL('**/dashboard');
}

/** Client-side navigation (a full reload would drop the in-memory session). */
export async function navigate(page: Page, path: string): Promise<void> {
  await page.evaluate((to) => {
    window.history.pushState({}, '', to);
    window.dispatchEvent(new PopStateEvent('popstate'));
  }, path);
}
