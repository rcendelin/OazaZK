import { apiClient } from './client.ts';

// House transfer to a new owner (T03). Saldo sign (X1): positive = přeplatek.

export interface HouseTransferPreview {
  houseId: string;
  houseName: string;
  transferDate: string;
  closingDate: string;
  currentOwnerName: string | null;
  currentOwnerFrom: string | null;
  closingSaldo: number;
  closingPayments: number;
  closingCosts: number;
  meterId: string | null;
  meterNumber: string | null;
  suggestedMeterValue: number | null;
  suggestedMeterNote: string | null;
  problems: string[];
}

export interface HouseTransferRequest {
  transferDate: string;
  newOwnerName: string;
  newOwnerContact?: string;
  meterValue?: number;
  meterIsEstimate: boolean;
  meterSource?: string;
  fundShare: number;
  updateHouseContact: boolean;
}

export const previewHouseTransfer = (houseId: string, date: string): Promise<HouseTransferPreview> =>
  apiClient.get(`/houses/${encodeURIComponent(houseId)}/transfer-preview?date=${encodeURIComponent(date)}`);

export const transferHouse = (houseId: string, data: HouseTransferRequest): Promise<{ closingId: string; closingSaldo: number; newOwnershipPeriodId: string }> =>
  apiClient.post(`/houses/${encodeURIComponent(houseId)}/transfer`, data);
