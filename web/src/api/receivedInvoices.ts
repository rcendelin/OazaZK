import { apiClient, ApiError } from './client.ts';
import type { ReceivedInvoice } from '../types/index.ts';

export const getReceivedInvoices = (
  year?: number,
  category?: string,
): Promise<ReceivedInvoice[]> => {
  const params = new URLSearchParams();
  if (year !== undefined) params.set('year', String(year));
  if (category) params.set('category', category);
  const qs = params.toString();
  return apiClient.get<ReceivedInvoice[]>(`/invoices/all${qs ? `?${qs}` : ''}`);
};

/**
 * Downloads a received invoice's attachment via its source-specific path
 * (/invoices/{id}/attachment or /finance/{id}/attachment) with auth.
 */
export const downloadReceivedAttachment = async (
  item: ReceivedInvoice,
  getToken: () => Promise<string | null>,
): Promise<void> => {
  if (!item.attachmentDownloadPath) return;
  const token = await getToken();
  const baseUrl = import.meta.env.VITE_API_BASE_URL || '/api';
  const response = await fetch(`${baseUrl}${item.attachmentDownloadPath}`, {
    headers: token ? { Authorization: `Bearer ${token}` } : {},
  });

  if (!response.ok) {
    throw new ApiError(response.status, 'Stahování přílohy se nezdařilo');
  }

  const blob = await response.blob();
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = `faktura-${item.description || item.id}.pdf`;
  document.body.appendChild(a);
  a.click();
  document.body.removeChild(a);
  URL.revokeObjectURL(url);
};
