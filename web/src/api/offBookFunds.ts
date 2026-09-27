import { apiClient } from './client.ts';

// Off-book fund (T10) — outside the association's accounting.

export type FundRecordKind = 'Call' | 'Contribution' | 'Expense' | 'Settlement';

export interface OffBookFund {
  id: string;
  name: string;
  purpose: string | null;
  managerName: string;
  accountDescription: string | null;
  active: boolean;
  balance: number;
  outstanding: number;
}

export interface FundCall {
  id: string;
  date: string;
  dueDate: string | null;
  amountPerHouse: number;
  text: string;
  collected: number;
  debtors: number;
  houses: { houseId: string; houseName: string; expected: number; paid: number; isPaid: boolean }[];
}

export interface FundRecord {
  id: string;
  kind: FundRecordKind;
  date: string;
  amount: number;
  text: string;
  houseName: string | null;
  callId: string | null;
  method: string | null;
  paidBy: string | null;
  hasReceipt: boolean;
  expenseId: string | null;
  paidTo: string | null;
}

export interface OffBookFundDetail {
  fund: OffBookFund;
  calls: FundCall[];
  records: FundRecord[];
}

export interface AddFundRecord {
  kind: FundRecordKind;
  date: string;
  amount: number;
  text?: string;
  dueDate?: string;
  houseIds?: string[];
  houseId?: string;
  callId?: string;
  method?: string;
  paidBy?: string;
  hasReceipt?: boolean;
  expenseId?: string;
}

const enc = encodeURIComponent;

export const getOffBookFunds = (): Promise<OffBookFund[]> => apiClient.get('/off-book-funds');
export const getOffBookFund = (id: string): Promise<OffBookFundDetail> => apiClient.get(`/off-book-funds/${enc(id)}`);
export const createOffBookFund = (data: { name: string; purpose?: string; managerName: string; accountDescription?: string }): Promise<OffBookFund> =>
  apiClient.post('/off-book-funds', { ...data, active: true });
export const addFundRecord = (fundId: string, data: AddFundRecord): Promise<FundRecord> => apiClient.post(`/off-book-funds/${enc(fundId)}/records`, data);
