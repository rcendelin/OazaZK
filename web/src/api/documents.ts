import { apiClient, ApiError } from './client.ts';
import type { DocumentResponse } from '../types/index.ts';

export const getDocuments = (category?: string): Promise<DocumentResponse[]> =>
  apiClient.get<DocumentResponse[]>(
    `/documents${category ? `?category=${encodeURIComponent(category)}` : ''}`,
  );

export const uploadDocument = async (
  file: File,
  name: string,
  category: string,
  getToken: () => Promise<string | null>,
): Promise<DocumentResponse> => {
  const token = await getToken();
  const baseUrl = import.meta.env.VITE_API_BASE_URL || '/api';
  const url = `${baseUrl}/documents?name=${encodeURIComponent(name)}&category=${encodeURIComponent(category)}`;
  const response = await fetch(url, {
    method: 'POST',
    headers: {
      'Content-Type': file.type || 'application/octet-stream',
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
    },
    body: file,
  });

  if (!response.ok) {
    const error = await response.json().catch(() => ({ error: 'Upload selhalo' }));
    throw new ApiError(response.status, error.error || 'Upload selhalo');
  }

  return response.json() as Promise<DocumentResponse>;
};

export const downloadDocument = async (
  id: string,
  filename: string,
  getToken: () => Promise<string | null>,
): Promise<void> => {
  const token = await getToken();
  const baseUrl = import.meta.env.VITE_API_BASE_URL || '/api';
  const response = await fetch(
    `${baseUrl}/documents/${encodeURIComponent(id)}/download`,
    {
      headers: token ? { Authorization: `Bearer ${token}` } : {},
    },
  );

  if (!response.ok) {
    throw new ApiError(response.status, 'Stahování se nezdařilo');
  }

  const blob = await response.blob();
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = filename;
  document.body.appendChild(a);
  a.click();
  document.body.removeChild(a);
  URL.revokeObjectURL(url);
};

export const deleteDocument = (id: string): Promise<void> =>
  apiClient.delete(`/documents/${encodeURIComponent(id)}`);
