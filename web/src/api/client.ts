export class ApiError extends Error {
  status: number;

  constructor(status: number, message: string) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
  }
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
        .catch(() => ({ error: 'Unknown error' }));
      throw new ApiError(
        response.status,
        (error as Record<string, string>).error ||
          (error as Record<string, string>).message ||
          'Request failed',
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
      throw new ApiError(response.status, 'Upload failed');
    }

    return response.json() as Promise<T>;
  }
}

export const apiClient = new ApiClient();
