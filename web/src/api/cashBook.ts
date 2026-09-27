import { apiClient } from './client.ts';

// Cash book (T09). Days are `yyyy-MM-dd`.

export type CashBookEntryType = 'Deposit' | 'Expense' | 'Correction';

export interface CashBookEntry {
  id: string;
  date: string;
  type: CashBookEntryType;
  amount: number;
  /** Effect on the balance: deposits positive, expenses negative, a storno the opposite of its original. */
  effect: number;
  balance: number;
  category: string;
  description: string;
  counterparty: string | null;
  hasReceipt: boolean;
  documentId: string | null;
  bankTransactionRef: string | null;
  correctionOf: string | null;
  correctedBy: string | null;
  componentId: string | null;
  componentName: string | null;
  costEntryId: string | null;
  createdByName: string | null;
}

export interface CashBook {
  from: string | null;
  to: string | null;
  openingBalance: number;
  deposits: number;
  expenses: number;
  closingBalance: number;
  entries: CashBookEntry[];
}

export interface CreateCashBookEntry {
  date: string;
  type: 'Deposit' | 'Expense';
  amount: number;
  category: string;
  description: string;
  counterparty?: string;
  hasReceipt: boolean;
  documentId?: string;
  bankTransactionRef?: string;
  componentId?: string;
}

export const typeLabels: Record<CashBookEntryType, string> = { Deposit: 'Vklad', Expense: 'Výdaj', Correction: 'Storno' };

export const getCashBook = (range: { from?: string; to?: string }): Promise<CashBook> => {
  const params = new URLSearchParams();
  if (range.from) params.set('from', range.from);
  if (range.to) params.set('to', range.to);
  const query = params.toString();
  return apiClient.get(`/cash-book${query ? `?${query}` : ''}`);
};

export const createCashBookEntry = (data: CreateCashBookEntry): Promise<CashBookEntry> => apiClient.post('/cash-book', data);

export const stornoCashBookEntry = (id: string, reason: string): Promise<CashBookEntry> =>
  apiClient.post(`/cash-book/${encodeURIComponent(id)}/storno`, { reason });
