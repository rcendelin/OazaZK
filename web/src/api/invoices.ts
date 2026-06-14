import { apiClient, ApiError } from './client.ts';
import type { SupplierInvoice } from '../types/index.ts';

export interface InvoiceInput {
  year: number;
  month: number;
  invoiceNumber: string;
  issuedDate: string; // ISO
  dueDate: string; // ISO
  amount: number;
  consumptionM3: number;
}

export const getInvoices = (year?: number): Promise<SupplierInvoice[]> =>
  apiClient.get<SupplierInvoice[]>(
    `/invoices${year ? `?year=${encodeURIComponent(String(year))}` : ''}`,
  );

export const createInvoice = (data: InvoiceInput): Promise<SupplierInvoice> =>
  apiClient.post<SupplierInvoice>('/invoices', data);

export const updateInvoice = (id: string, data: InvoiceInput): Promise<SupplierInvoice> =>
  apiClient.put<SupplierInvoice>(`/invoices/${encodeURIComponent(id)}`, data);

export const deleteInvoice = (id: string): Promise<void> =>
  apiClient.delete(`/invoices/${encodeURIComponent(id)}`);

export const uploadInvoiceAttachment = async (
  id: string,
  file: File,
  getToken: () => Promise<string | null>,
): Promise<SupplierInvoice> => {
  const token = await getToken();
  const baseUrl = import.meta.env.VITE_API_BASE_URL || '/api';
  const response = await fetch(`${baseUrl}/invoices/${encodeURIComponent(id)}/attachment`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/pdf',
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
    },
    body: file,
  });

  if (!response.ok) {
    const error = await response.json().catch(() => ({ error: 'Nahrání přílohy selhalo' }));
    throw new ApiError(response.status, error.error || 'Nahrání přílohy selhalo');
  }

  return response.json() as Promise<SupplierInvoice>;
};

export const downloadInvoiceAttachment = async (
  id: string,
  filename: string,
  getToken: () => Promise<string | null>,
): Promise<void> => {
  const token = await getToken();
  const baseUrl = import.meta.env.VITE_API_BASE_URL || '/api';
  const response = await fetch(`${baseUrl}/invoices/${encodeURIComponent(id)}/attachment`, {
    headers: token ? { Authorization: `Bearer ${token}` } : {},
  });

  if (!response.ok) {
    throw new ApiError(response.status, 'Stahování přílohy se nezdařilo');
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
