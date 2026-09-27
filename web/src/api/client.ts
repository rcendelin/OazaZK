export class ApiError extends Error {
  status: number;
  /** Every reason from a `{ error, errors: [{ field, message }] }` answer (business rules, validation). */
  details: string[];

  constructor(status: number, message: string, details: string[] = []) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.details = details;
  }
}

function errorDetails(body: unknown): string[] {
  const errors = (body as { errors?: unknown }).errors;
  if (!Array.isArray(errors)) return [];
  return errors
    .map((e) => (typeof e === 'object' && e !== null ? (e as { message?: unknown }).message : undefined))
    .filter((m): m is string => typeof m === 'string' && m.length > 0);
}

/** The magic-link endpoints answer 401 for a wrong/expired link — that is not an expired session. */
const isMagicLinkPath = (path: string) => path.startsWith('/auth/magic-link');

class ApiClient {
  private baseUrl = import.meta.env.VITE_API_BASE_URL || '/api';
  private getToken: (() => Promise<string | null>) | null = null;
  private onUnauthorized: (() => void) | null = null;

  setTokenProvider(provider: () => Promise<string | null>): void {
    this.getToken = provider;
  }

  /** Called when the API rejects the session (401 on a request that carried a token). */
  setUnauthorizedHandler(handler: (() => void) | null): void {
    this.onUnauthorized = handler;
  }

  /**
   * Every API call (also the raw `fetch` ones in `api/*.ts`: uploads, downloads, exports) reports its status
   * here: a 401 on a request sent with a token means the session expired → log out. Requests without a token
   * and the magic-link endpoints never trigger it, so a 401 cannot loop.
   */
  reportStatus(status: number, path: string, hadToken: boolean): void {
    if (status === 401 && hadToken && !isMagicLinkPath(path)) this.onUnauthorized?.();
  }

  async fetch<T>(path: string, options?: RequestInit): Promise<T> {
    const token = await this.getToken?.();
    const headers: Record<string, string> = {
      'Content-Type': 'application/json',
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...((options?.headers as Record<string, string>) || {}),
    };

    const response = await globalThis.fetch(`${this.baseUrl}${path}`, {
      ...options,
      headers,
    });
    this.reportStatus(response.status, path, Boolean(token));

    if (!response.ok) {
      const error = await response
        .json()
        .catch(() => ({ error: 'Neznámá chyba' }));
      throw new ApiError(
        response.status,
        (error as Record<string, string>).error ||
          (error as Record<string, string>).message ||
          'Požadavek se nezdařil',
        errorDetails(error),
      );
    }

    if (response.status === 204) {
      return undefined as T;
    }

    return response.json() as Promise<T>;
  }

  get<T>(path: string): Promise<T> {
    return this.fetch<T>(path);
  }

  post<T>(path: string, body?: unknown): Promise<T> {
    return this.fetch<T>(path, {
      method: 'POST',
      body: body !== undefined ? JSON.stringify(body) : undefined,
    });
  }

  put<T>(path: string, body: unknown): Promise<T> {
    return this.fetch<T>(path, {
      method: 'PUT',
      body: JSON.stringify(body),
    });
  }

  delete(path: string): Promise<void> {
    return this.fetch<void>(path, { method: 'DELETE' });
  }

  async uploadFile<T>(path: string, file: File): Promise<T> {
    const token = await this.getToken?.();
    const response = await globalThis.fetch(`${this.baseUrl}${path}`, {
      method: 'POST',
      headers: {
        ...(token ? { Authorization: `Bearer ${token}` } : {}),
      },
      body: file,
    });
    this.reportStatus(response.status, path, Boolean(token));

    if (!response.ok) {
      const error = await response
        .json()
        .catch(() => ({ error: 'Nahrání se nezdařilo' }));
      throw new ApiError(
        response.status,
        (error as Record<string, string>).error || 'Nahrání se nezdařilo',
      );
    }

    return response.json() as Promise<T>;
  }
}

export const apiClient = new ApiClient();
