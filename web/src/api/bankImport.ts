import { apiClient } from './client.ts';

export type BankImportRowStatus = 'New' | 'AlreadyImported' | 'Ignored' | 'Outgoing' | 'UnsupportedCurrency';
export type BankImportMatchSource = 'Account' | 'None';
export type BankImportPaymentType = 'Advance' | 'Doplatek';
export type BankImportAction = 'Import' | 'Ignore';

export interface BankStatementSummary {
  account: string;
  number: string | null;
  dateFrom: string | null;
  dateTo: string | null;
  openingBalance: number | null;
  closingBalance: number | null;
  totalIncome: number | null;
  totalExpense: number | null;
  sumCheckOk: boolean;
}

export interface BankImportRow {
  transactionId: string;
  date: string;
  amount: number;
  currency: string;
  counterAccount: string | null;
  counterName: string | null;
  message: string | null;
  note: string | null;
  variableSymbol: string | null;
  bankOperationType: string | null;
  status: BankImportRowStatus;
  houseId: string | null;
  matchSource: BankImportMatchSource;
  paymentType: BankImportPaymentType;
  year: number;
  month: number;
  waterAmount: number;
  electricityAmount: number;
  commonAmount: number;
  warnings: string[];
}

export interface BankImportHouse {
  houseId: string;
  houseName: string;
  waterAmount: number;
  electricityAmount: number;
  commonAmount: number;
  totalAmount: number;
}

export interface BankImportPreview {
  statement: BankStatementSummary;
  rows: BankImportRow[];
  warnings: string[];
  houses: BankImportHouse[];
  /** houseId → months ("YYYY-MM") that already have a regular advance. */
  existingAdvanceMonths: Record<string, string[]>;
}

export interface ConfirmBankImportRow {
  transactionId: string;
  date: string;
  amount: number;
  counterAccount: string | null;
  counterName: string | null;
  message: string | null;
  variableSymbol: string | null;
  action: BankImportAction;
  houseId: string | null;
  paymentType: BankImportPaymentType;
  year: number;
  month: number;
  waterAmount: number;
  electricityAmount: number;
  commonAmount: number;
}

export interface ConfirmBankImportResult {
  imported: number;
  ignored: number;
  newAccounts: number;
  skipped: string[];
}

export interface BankAccount {
  accountKey: string;
  accountNumber: string;
  houseId: string;
  accountName: string | null;
  updatedAt: string;
}

/** Uploads the raw CSV file; nothing is saved until {@link confirmBankImport}. */
export const previewBankImport = (file: File): Promise<BankImportPreview> =>
  apiClient.uploadFile<BankImportPreview>('/bank-import/preview', file);

export const confirmBankImport = (
  account: string,
  rows: ConfirmBankImportRow[],
): Promise<ConfirmBankImportResult> =>
  apiClient.post<ConfirmBankImportResult>('/bank-import/confirm', { account, rows });

export const getBankAccounts = (): Promise<BankAccount[]> =>
  apiClient.get<BankAccount[]>('/bank-accounts');

export const createBankAccount = (data: {
  houseId: string;
  accountNumber: string;
  accountName?: string;
}): Promise<BankAccount> =>
  apiClient.post<BankAccount>('/bank-accounts', data);

export const deleteBankAccount = (accountKey: string): Promise<void> =>
  apiClient.delete(`/bank-accounts/${encodeURIComponent(accountKey)}`);
