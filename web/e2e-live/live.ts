import { createHmac } from 'node:crypto';
import { expect, test as base } from '@playwright/test';
import type { APIRequestContext, Page, Request, Response } from '@playwright/test';

export const API = process.env.OAZA_LIVE_API_URL ?? 'https://func-oaza-dev.azurewebsites.net/api';
export const APP = process.env.OAZA_LIVE_APP_URL ?? 'https://oaza-dev.cendelinovi.cz';

export type Role = 'Admin' | 'Member' | 'Accountant';
export interface LiveUser { id: string; email: string; role: Role; houseId?: string }

export const users = {
  admin: { id: 'e2e-admin', email: 'e2e-admin@example.invalid', role: 'Admin' },
  member: { id: 'e2e-member', email: 'e2e-member@example.invalid', role: 'Member', houseId: process.env.OAZA_LIVE_MEMBER_HOUSE },
  accountant: { id: 'e2e-accountant', email: 'e2e-accountant@example.invalid', role: 'Accountant' },
  memberNoHouse: { id: 'e2e-member-nohouse', email: 'e2e-member-nohouse@example.invalid', role: 'Member' },
} satisfies Record<string, LiveUser>;

const b64 = (v: string | Buffer) => Buffer.from(v).toString('base64url');

/** HS256 JWT exactly like the API's JwtService (sub, email, role, houseId, authMethod; aud = iss), 1 h. */
export function token(user: LiveUser, opts: { secret?: string; expiresIn?: number } = {}): string {
  const secret = opts.secret ?? process.env.OAZA_LIVE_JWT_SECRET;
  const issuer = process.env.OAZA_LIVE_JWT_ISSUER;
  if (!secret || !issuer) throw new Error('Set OAZA_LIVE_JWT_SECRET and OAZA_LIVE_JWT_ISSUER.');
  const now = Math.floor(Date.now() / 1000);
  const payload: Record<string, unknown> = {
    sub: user.id, email: user.email, role: user.role, authMethod: 'MagicLink',
    iss: issuer, aud: issuer, iat: now, nbf: now, exp: now + (opts.expiresIn ?? 3600),
  };
  if (user.houseId) payload.houseId = user.houseId;
  const head = b64(JSON.stringify({ alg: 'HS256', typ: 'JWT' }));
  const body = b64(JSON.stringify(payload));
  const sig = createHmac('sha256', secret).update(`${head}.${body}`).digest('base64url');
  return `${head}.${body}.${sig}`;
}

/** Signs in through the real magic-link page; only the token exchange is answered locally with our JWT. */
export async function loginAs(page: Page, user: LiveUser): Promise<void> {
  const jwt = token(user);
  await page.route('**/api/auth/magic-link/verify', (route) =>
    route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ token: jwt }) }));
  await page.goto(`/auth/verify?token=e2e&email=${encodeURIComponent(user.email)}`);
  await page.waitForURL('**/dashboard', { timeout: 60_000 });
}

/** Client-side navigation (a reload would drop the in-memory session). */
export async function go(page: Page, path: string): Promise<void> {
  await page.evaluate((to) => {
    window.history.pushState({}, '', to);
    window.dispatchEvent(new PopStateEvent('popstate'));
  }, path);
}

/** API client for a user (Bearer token). */
export async function api(playwright: { request: { newContext: (o: object) => Promise<APIRequestContext> } }, user?: LiveUser, bearer?: string): Promise<APIRequestContext> {
  const headers: Record<string, string> = {};
  if (user || bearer) headers.Authorization = `Bearer ${bearer ?? token(user!)}`;
  return playwright.request.newContext({ baseURL: `${API}/`, extraHTTPHeaders: headers });
}

/** Unique marker for records this run creates. */
export const RUN = `E2E-${new Date().toISOString().slice(0, 16).replace(/[-:T]/g, '')}`;

/** Problems already reported and being fixed — recorded as annotations, not failures. */
export const KNOWN_ISSUES: RegExp[] = [];

/**
 * Page fixture that records problems: console errors, uncaught exceptions, API 5xx and failed requests.
 * Every test fails if any happened (unless the test calls `allowProblems`).
 */
export const test = base.extend<{ problems: string[]; allowProblems: (re: RegExp) => void }>({
  problems: async ({ page }, use) => {
    const problems: string[] = [];
    page.on('console', (m) => {
      // "Failed to load resource" is reported with its URL by the response listener below
      if (m.type() === 'error' && !m.text().startsWith('Failed to load resource')) problems.push(`console: ${m.text().slice(0, 300)}`);
    });
    page.on('pageerror', (e) => problems.push(`pageerror: ${e.message.slice(0, 300)}`));
    page.on('response', (r: Response) => {
      // 5xx always; 401/403 from the API = the page calls something its user may not use
      if (r.status() >= 500 || ([401, 403].includes(r.status()) && r.url().startsWith(API)))
        problems.push(`HTTP ${r.status()} ${r.request().method()} ${r.url().replace(API, '/api')}`);
    });
    page.on('requestfailed', (r: Request) => {
      const failure = r.failure()?.errorText ?? '';
      if (!/ERR_ABORTED|NS_BINDING_ABORTED/.test(failure)) problems.push(`failed: ${r.method()} ${r.url()} ${failure}`);
    });
    // eslint-disable-next-line react-hooks/rules-of-hooks -- Playwright fixture `use`, not a React hook
    await use(problems);
  },
  allowProblems: [async ({ problems }, use, testInfo) => {
    const allowed: RegExp[] = [...KNOWN_ISSUES];
    await use((re) => { allowed.push(re); });
    const known = problems.filter((p) => KNOWN_ISSUES.some((re) => re.test(p)));
    if (known.length > 0) testInfo.annotations.push({ type: 'known-issue', description: [...new Set(known)].join(' | ').slice(0, 500) });
    const left = problems.filter((p) => !allowed.some((re) => re.test(p)));
    expect(left, 'console errors / 5xx / failed requests during the test').toEqual([]);
  }, { auto: true }],
});

export { expect };

/** Czech money as shown: "1 234,50 Kč" with any space kind. */
export const kc = (value: number) => new RegExp(`${value < 0 ? '[-−]' : ''}${Math.abs(value).toLocaleString('cs-CZ', { minimumFractionDigits: 2, maximumFractionDigits: 2 }).replace(/\s/g, '\\s?')}\\s*Kč`);
