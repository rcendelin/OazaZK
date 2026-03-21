// User roles
export type UserRole = 'Admin' | 'Member' | 'Accountant';
export type AuthMethod = 'EntraId' | 'MagicLink';
export type MeterType = 'Main' | 'Individual';
export type ReadingSource = 'Import' | 'Manual';
export type BillingPeriodStatus = 'Open' | 'Closed';
export type FinancialRecordType = 'Income' | 'Expense';

export interface User {
  id: string;
  name: string;
  email: string;
  role: UserRole;
  houseId: string | null;
  authMethod: AuthMethod;
  lastLogin: string | null;
  notificationsEnabled: boolean;
}

export interface House {
  id: string;
  name: string;
  address: string;
  contactPerson: string;
  email: string;
  isActive: boolean;
}

export interface WaterMeter {
  id: string;
  meterNumber: string;
  type: MeterType;
  houseId: string | null;
  installationDate: string;
}

export interface MeterReading {
  meterId: string;
  readingDate: string;
  value: number;
  source: ReadingSource;
  importedAt: string;
  importedBy: string;
}

export interface BillingPeriod {
  id: string;
  name: string;
  dateFrom: string;
  dateTo: string;
  status: BillingPeriodStatus;
}

export interface SupplierInvoice {
  id: string;
  year: number;
  month: number;
  invoiceNumber: string;
  issuedDate: string;
  dueDate: string;
  amount: number;
  consumptionM3: number;
  attachmentBlobName: string | null;
}

export interface AdvancePayment {
  houseId: string;
  year: number;
  month: number;
  amount: number;
  paymentDate: string;
}

export interface Settlement {
  periodId: string;
  houseId: string;
  consumptionM3: number;
  sharePercent: number;
  calculatedAmount: number;
  totalAdvances: number;
  balance: number;
  lossAllocatedM3: number;
}

// API response types

export interface ReadingWithMeter {
  meterId: string;
  meterNumber: string;
  meterType: MeterType;
  houseId: string | null;
  houseName: string | null;
  readingDate: string;
  value: number;
  consumption: number | null;
  source: ReadingSource;
}

export interface MonthlyReadingsResponse {
  year: number;
  month: number;
  readings: ReadingWithMeter[];
}

export interface ImportValidationMessage {
  type: 'Error' | 'Warning';
  message: string;
  row: number | null;
  column: string | null;
}

export interface ImportPreviewRow {
  date: string;
  readings: Record<string, number>;
}

export interface ImportPreviewResponse {
  importSessionId: string;
  rows: ImportPreviewRow[];
  meters: Array<{
    meterId: string;
    meterNumber: string;
    meterType: MeterType;
    houseName: string | null;
  }>;
  messages: ImportValidationMessage[];
  hasErrors: boolean;
}

export interface BillingPeriodResponse {
  id: string;
  name: string;
  dateFrom: string;
  dateTo: string;
  status: BillingPeriodStatus;
  totalInvoiceAmount: number | null;
}

export interface CreateBillingPeriodRequest {
  name: string;
  dateFrom: string;
  dateTo: string;
}

export interface SettlementPreviewResponse {
  periodId: string;
  periodName: string;
  dateFrom: string;
  dateTo: string;
  mainMeterConsumption: number;
  totalHouseConsumption: number;
  totalLoss: number;
  totalInvoiceAmount: number;
  lossAllocationMethod: string;
  houses: HouseSettlementDetail[];
}

export interface HouseSettlementDetail {
  houseId: string;
  houseName: string;
  consumptionM3: number;
  lossAllocatedM3: number;
  sharePercent: number;
  calculatedAmount: number;
  totalAdvances: number;
  balance: number;
}

export interface SettlementResponse {
  periodId: string;
  houseId: string;
  houseName: string;
  consumptionM3: number;
  sharePercent: number;
  calculatedAmount: number;
  totalAdvances: number;
  balance: number;
  lossAllocatedM3: number;
}
