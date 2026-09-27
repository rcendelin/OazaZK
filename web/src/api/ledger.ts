import { apiClient, ApiError } from './client.ts';
import type { AllocationMethod } from './costComponents.ts';

// House ledger and overview (T07). Saldo sign (X1): positive = přeplatek, negative = nedoplatek.

export type LedgerItemKind = 'Opening' | 'Payment' | 'Payout' | 'Cost' | 'Credit' | 'Water' | 'Loss';

export interface LedgerItem {
  date: string;
  kind: LedgerItemKind;
  description: string;
  componentId: string | null;
  componentName: string | null;
  /** Effect on the saldo: payments and credits positive, costs negative. */
  amount: number;
  balance: number;
  detail: {
    total: number;
    segmentFrom: string;
    segmentTo: string;
    segmentDays: number;
    segmentAmount: number;
    method: AllocationMethod;
    weight: number;
    totalWeight: number;
    explanation: string;
  } | null;
}

export interface LedgerPeriod {
  id: string;
  ownerName: string;
  validFrom: string;
  validTo: string | null;
}

export interface HouseLedger {
  houseId: string;
  houseName: string;
  from: string;
  to: string;
  ownershipPeriod: LedgerPeriod | null;
  ownershipPeriods: LedgerPeriod[];
  opening: number;
  payments: number;
  costs: number;
  saldo: number;
  items: LedgerItem[];
}

export interface LedgerOverview {
  from: string;
  to: string;
  components: { componentId: string; componentName: string; allocatedTotal: number; housesTotal: number; matches: boolean; warnings: string[] }[];
  houses: { houseId: string; houseName: string; opening: number; payments: number; costs: Record<string, number>; saldo: number }[];
}

export const kindLabels: Record<LedgerItemKind, string> = {
  Opening: 'Počáteční podíl',
  Payment: 'Platba',
  Payout: 'Výplata',
  Cost: 'Náklad',
  Credit: 'Kredit',
  Water: 'Voda',
  Loss: 'Ztráta',
};

const params = (values: Record<string, string | undefined>) => {
  const p = new URLSearchParams();
  Object.entries(values).forEach(([k, v]) => { if (v) p.set(k, v); });
  const s = p.toString();
  return s ? `?${s}` : '';
};

export const getHouseLedger = (houseId: string, range: { from?: string; to?: string; ownershipPeriodId?: string }): Promise<HouseLedger> =>
  apiClient.get(`/ledger/houses/${encodeURIComponent(houseId)}${params(range)}`);

export const getLedgerOverview = (range: { from?: string; to?: string }): Promise<LedgerOverview> =>
  apiClient.get(`/ledger/overview${params(range)}`);

/** Downloads an export (xlsx | csv) with the signed-in user's token. */
export async function downloadLedgerExport(
  path: string,
  query: Record<string, string | undefined>,
  getToken: () => Promise<string | null>,
): Promise<void> {
  const token = await getToken();
  const baseUrl = import.meta.env.VITE_API_BASE_URL || '/api';
  const response = await fetch(`${baseUrl}${path}${params(query)}`, {
    headers: token ? { Authorization: `Bearer ${token}` } : {},
  });
  if (!response.ok) throw new ApiError(response.status, 'Export se nezdařil');
  const disposition = response.headers.get('Content-Disposition') ?? '';
  const fileName = /filename="([^"]+)"/.exec(disposition)?.[1] ?? 'saldo';
  const url = URL.createObjectURL(await response.blob());
  const a = document.createElement('a');
  a.href = url;
  a.download = fileName;
  document.body.appendChild(a);
  a.click();
  document.body.removeChild(a);
  URL.revokeObjectURL(url);
}
