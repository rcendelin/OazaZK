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
  name: string;
  type: MeterType;
  houseId: string | null;
  houseName: string | null;
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
  houseName: string | null;
  year: number;
  month: number;
  amount: number;
  paymentDate: string;
}

export interface Settlement {
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

// API response types

// Matches backend ReadingResponse DTO
export interface ReadingResponse {
  meterId: string;
  meterNumber: string;
  houseName: string | null;
  readingDate: string;
  value: number;
  consumption: number | null;
  source: string;
  importedAt: string;
  importedBy: string;
}

export interface MonthlyReadingsResponse {
  year: number;
  month: number;
  readings: ReadingResponse[];
}

// Matches backend ImportValidationMessage DTO
export interface ImportValidationMessage {
  type: string;
  message: string;
  row: number | null;
  meterId: string | null;
}

// Matches backend ImportPreviewRow DTO
export interface ImportPreviewRow {
  readingDate: string;
  meterValues: Record<string, number>;
}

// Matches backend ImportPreviewResponse DTO
export interface ImportPreviewResponse {
  importSessionId: string;
  rows: ImportPreviewRow[];
  errors: ImportValidationMessage[];
  warnings: ImportValidationMessage[];
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

// Document types
export interface DocumentResponse {
  id: string;
  category: string;
  name: string;
  fileSizeBytes: number;
  contentType: string;
  uploadedAt: string;
  uploadedBy: string;
}

// Document version types
export interface DocumentVersionResponse {
  versionNumber: number;
  fileSizeBytes: number;
  contentType: string;
  uploadedAt: string;
  uploadedBy: string;
}

// Financial record types
export interface FinanceResponse {
  id: string;
  year: number;
  type: FinancialRecordType;
  category: string;
  amount: number;
  date: string;
  description: string;
  hasAttachment: boolean;
}

export interface FinanceSummaryResponse {
  year: number;
  totalIncome: number;
  totalExpenses: number;
  balance: number;
  categories: CategorySummary[];
}

export interface CategorySummary {
  category: string;
  income: number;
  expenses: number;
}

export interface FinanceBalanceResponse {
  totalIncome: number;
  totalExpenses: number;
  balance: number;
}

export interface CreateFinanceRequest {
  type: FinancialRecordType;
  category: string;
  amount: number;
  date: string;
  description: string;
}

export type UpdateFinanceRequest = CreateFinanceRequest;

// Chart types
export interface ChartDataPoint {
  year: number;
  month: number;
  label: string;
  consumption: number;
}

export interface ChartResponse {
  houseId: string | null;
  houseName: string | null;
  dataPoints: ChartDataPoint[];
}

// Notification types
export interface SendNotificationRequest {
  type: 'reading_reminder' | 'import_completed' | 'settlement_closed';
  periodId?: string;
  year?: number;
  month?: number;
}
