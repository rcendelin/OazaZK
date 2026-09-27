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

class ApiClient {
  private baseUrl = import.meta.env.VITE_API_BASE_URL || '/api';
  private getToken: (() => Promise<string | null>) | null = null;

  setTokenProvider(provider: () => Promise<string | null>): void {
    this.getToken = provider;
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
