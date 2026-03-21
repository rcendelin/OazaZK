import { useCallback, useRef, useState } from 'react';
import { useAuth } from '../auth/AuthContext';
import { useApi } from '../hooks/useApi';
import {
  getBillingPeriods,
  createBillingPeriod,
  calculateSettlement,
  closeBillingPeriod,
  getSettlements,
} from '../api/billing';
import { ConfirmDialog } from '../components/ConfirmDialog';
import type {
  BillingPeriodResponse,
  CreateBillingPeriodRequest,
  SettlementPreviewResponse,
  SettlementResponse,
} from '../types';

const formatCZK = (value: number): string =>
  new Intl.NumberFormat('cs-CZ', {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  }).format(value);

const formatNumber = (value: number, decimals = 1): string =>
  new Intl.NumberFormat('cs-CZ', {
    minimumFractionDigits: decimals,
    maximumFractionDigits: decimals,
  }).format(value);

const formatPercent = (value: number): string =>
  new Intl.NumberFormat('cs-CZ', {
    minimumFractionDigits: 1,
    maximumFractionDigits: 1,
  }).format(value);

const formatDate = (dateStr: string): string =>
  new Intl.DateTimeFormat('cs-CZ').format(new Date(dateStr));

export function BillingPage() {
  const { user } = useAuth();
  const isAdmin = user?.role === 'Admin';

  const fetchPeriods = useCallback(() => getBillingPeriods(), []);
  const {
    data: periods,
    loading: periodsLoading,
    error: periodsError,
    refetch: refetchPeriods,
  } = useApi(fetchPeriods, []);

  if (isAdmin) {
    return (
      <AdminBillingView
        periods={periods}
        periodsLoading={periodsLoading}
        periodsError={periodsError}
        refetchPeriods={refetchPeriods}
      />
    );
  }

  return (
    <MemberBillingView
      periods={periods}
      periodsLoading={periodsLoading}
      periodsError={periodsError}
      houseId={user?.houseId ?? null}
    />
  );
}

// ─── Admin View ────────────────────────────────────────────────────────────────

interface AdminBillingViewProps {
  periods: BillingPeriodResponse[] | null;
  periodsLoading: boolean;
  periodsError: string | null;
  refetchPeriods: () => void;
}

function AdminBillingView({
  periods,
  periodsLoading,
  periodsError,
  refetchPeriods,
}: AdminBillingViewProps) {
  const [showCreateForm, setShowCreateForm] = useState(false);
  const [selectedPeriodId, setSelectedPeriodId] = useState<string | null>(null);

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold text-gray-900">Vyúčtování</h1>
          <p className="mt-1 text-sm text-gray-600">
            Správa zúčtovacích období a vyúčtování
          </p>
        </div>
        <button
          onClick={() => setShowCreateForm((prev) => !prev)}
          className="rounded-md bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700 focus:outline-none focus:ring-2 focus:ring-blue-500 focus:ring-offset-2"
        >
          {showCreateForm ? 'Zavřít formulář' : 'Nové zúčtovací období'}
        </button>
      </div>

      {/* Create form */}
      {showCreateForm && (
        <CreatePeriodForm
          onCreated={() => {
            setShowCreateForm(false);
            refetchPeriods();
          }}
        />
      )}

      {/* Loading */}
      {periodsLoading && (
        <div className="flex items-center justify-center py-12">
          <div className="h-8 w-8 animate-spin rounded-full border-4 border-blue-600 border-t-transparent" />
        </div>
      )}

      {/* Error */}
      {periodsError && (
        <div className="rounded-md bg-red-50 p-4">
          <p className="text-sm text-red-700">{periodsError}</p>
        </div>
      )}

      {/* Periods list */}
      {!periodsLoading && !periodsError && periods && (
        <>
          {periods.length === 0 ? (
            <div className="rounded-md bg-gray-50 py-12 text-center">
              <p className="text-sm text-gray-500">
                Zatím nebylo vytvořeno žádné zúčtovací období.
              </p>
            </div>
          ) : (
            <div className="space-y-4">
              {periods.map((period) => (
                <PeriodCard
                  key={period.id}
                  period={period}
                  isSelected={selectedPeriodId === period.id}
                  onSelect={() =>
                    setSelectedPeriodId(
                      selectedPeriodId === period.id ? null : period.id,
                    )
                  }
                  onPeriodClosed={refetchPeriods}
                />
              ))}
            </div>
          )}
        </>
      )}
    </div>
  );
}

// ─── Create Period Form ────────────────────────────────────────────────────────

function CreatePeriodForm({ onCreated }: { onCreated: () => void }) {
  const [form, setForm] = useState<CreateBillingPeriodRequest>({
    name: '',
    dateFrom: '',
    dateTo: '',
  });
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setSubmitting(true);
    setError(null);
    try {
      await createBillingPeriod(form);
      onCreated();
    } catch (err: unknown) {
      const message =
        err instanceof Error ? err.message : 'Nastala neočekávaná chyba';
      setError(message);
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <form
      onSubmit={(e) => void handleSubmit(e)}
      className="rounded-lg border border-gray-200 bg-white p-6 shadow-sm"
    >
      <h2 className="mb-4 text-lg font-semibold text-gray-900">
        Nové zúčtovací období
      </h2>

      {error && (
        <div className="mb-4 rounded-md bg-red-50 p-3">
          <p className="text-sm text-red-700">{error}</p>
        </div>
      )}

      <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
        <div>
          <label
            htmlFor="period-name"
            className="block text-sm font-medium text-gray-700"
          >
            Název
          </label>
          <input
            id="period-name"
            type="text"
            required
            value={form.name}
            onChange={(e) => setForm((prev) => ({ ...prev, name: e.target.value }))}
            placeholder="2. pololetí 2025"
            className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-2 text-sm shadow-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
          />
        </div>
        <div>
          <label
            htmlFor="period-from"
            className="block text-sm font-medium text-gray-700"
          >
            Datum od
          </label>
          <input
            id="period-from"
            type="date"
            required
            value={form.dateFrom}
            onChange={(e) =>
              setForm((prev) => ({ ...prev, dateFrom: e.target.value }))
            }
            className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-2 text-sm shadow-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
          />
        </div>
        <div>
          <label
            htmlFor="period-to"
            className="block text-sm font-medium text-gray-700"
          >
            Datum do
          </label>
          <input
            id="period-to"
            type="date"
            required
            value={form.dateTo}
            onChange={(e) =>
              setForm((prev) => ({ ...prev, dateTo: e.target.value }))
            }
            className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-2 text-sm shadow-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
          />
        </div>
      </div>

      <div className="mt-4 flex justify-end">
        <button
          type="submit"
          disabled={submitting}
          className="rounded-md bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700 focus:outline-none focus:ring-2 focus:ring-blue-500 focus:ring-offset-2 disabled:cursor-not-allowed disabled:opacity-50"
        >
          {submitting ? 'Vytváření...' : 'Vytvořit'}
        </button>
      </div>
    </form>
  );
}

// ─── Period Card ───────────────────────────────────────────────────────────────

interface PeriodCardProps {
  period: BillingPeriodResponse;
  isSelected: boolean;
  onSelect: () => void;
  onPeriodClosed: () => void;
}

function PeriodCard({
  period,
  isSelected,
  onSelect,
  onPeriodClosed,
}: PeriodCardProps) {
  return (
    <div className="rounded-lg border border-gray-200 bg-white shadow-sm">
      {/* Header */}
      <button
        onClick={onSelect}
        className="flex w-full items-center justify-between px-6 py-4 text-left hover:bg-gray-50"
      >
        <div className="flex items-center gap-4">
          <div>
            <h3 className="text-base font-semibold text-gray-900">
              {period.name}
            </h3>
            <p className="mt-0.5 text-sm text-gray-500">
              {formatDate(period.dateFrom)} – {formatDate(period.dateTo)}
            </p>
          </div>
        </div>
        <div className="flex items-center gap-4">
          {period.totalInvoiceAmount !== null && (
            <span className="text-sm font-medium text-gray-700">
              {formatCZK(period.totalInvoiceAmount)} Kč
            </span>
          )}
          <StatusBadge status={period.status} />
          <svg
            className={`h-5 w-5 text-gray-400 transition-transform ${
              isSelected ? 'rotate-180' : ''
            }`}
            fill="none"
            viewBox="0 0 24 24"
            strokeWidth="1.5"
            stroke="currentColor"
          >
            <path
              strokeLinecap="round"
              strokeLinejoin="round"
              d="m19.5 8.25-7.5 7.5-7.5-7.5"
            />
          </svg>
        </div>
      </button>

      {/* Expanded detail */}
      {isSelected && (
        <div className="border-t border-gray-200 px-6 py-4">
          {period.status === 'Open' ? (
            <OpenPeriodDetail
              period={period}
              onPeriodClosed={onPeriodClosed}
            />
          ) : (
            <ClosedPeriodDetail period={period} />
          )}
        </div>
      )}
    </div>
  );
}

function StatusBadge({ status }: { status: string }) {
  const isOpen = status === 'Open';
  return (
    <span
      className={`inline-flex rounded-full px-2.5 py-0.5 text-xs font-medium ${
        isOpen ? 'bg-blue-100 text-blue-700' : 'bg-green-100 text-green-700'
      }`}
    >
      {isOpen ? 'Otevřené' : 'Uzavřené'}
    </span>
  );
}

// ─── Open Period Detail (Calculate + Close) ────────────────────────────────────

function OpenPeriodDetail({
  period,
  onPeriodClosed,
}: {
  period: BillingPeriodResponse;
  onPeriodClosed: () => void;
}) {
  const [method, setMethod] = useState<string>('Equal');
  const [preview, setPreview] = useState<SettlementPreviewResponse | null>(
    null,
  );
  const [calculating, setCalculating] = useState(false);
  const [calcError, setCalcError] = useState<string | null>(null);
  const [showCloseConfirm, setShowCloseConfirm] = useState(false);
  const [closing, setClosing] = useState(false);
  const [closeError, setCloseError] = useState<string | null>(null);
  const inFlight = useRef(false);

  const handleCalculate = async () => {
    if (inFlight.current) return;
    inFlight.current = true;
    setCalculating(true);
    setCalcError(null);
    try {
      const result = await calculateSettlement(period.id, method);
      setPreview(result);
    } catch (err: unknown) {
      const message =
        err instanceof Error ? err.message : 'Nastala neočekávaná chyba';
      setCalcError(message);
    } finally {
      setCalculating(false);
      inFlight.current = false;
    }
  };

  const handleClose = async () => {
    if (inFlight.current) return;
    inFlight.current = true;
    setClosing(true);
    setCloseError(null);
    try {
      await closeBillingPeriod(period.id, method);
      setShowCloseConfirm(false);
      onPeriodClosed();
    } catch (err: unknown) {
      const message =
        err instanceof Error ? err.message : 'Nastala neočekávaná chyba';
      setCloseError(message);
      setShowCloseConfirm(false);
    } finally {
      setClosing(false);
      inFlight.current = false;
    }
  };

  return (
    <div className="space-y-4">
      {/* Calculate controls */}
      <div className="flex flex-col gap-3 sm:flex-row sm:items-end">
        <div className="sm:max-w-xs">
          <label
            htmlFor="method-select"
            className="block text-sm font-medium text-gray-700"
          >
            Metoda rozdělení ztrát
          </label>
          <select
            id="method-select"
            value={method}
            onChange={(e) => setMethod(e.target.value)}
            className="mt-1 block w-full rounded-md border border-gray-300 bg-white px-3 py-2 text-sm shadow-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
          >
            <option value="Equal">Rovnoměrně</option>
            <option value="ProportionalToConsumption">Dle spotřeby</option>
          </select>
        </div>
        <button
          onClick={() => void handleCalculate()}
          disabled={calculating}
          className="rounded-md bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700 focus:outline-none focus:ring-2 focus:ring-blue-500 focus:ring-offset-2 disabled:cursor-not-allowed disabled:opacity-50"
        >
          {calculating ? 'Výpočet...' : 'Vypočítat vyúčtování'}
        </button>
      </div>

      {calcError && (
        <div className="rounded-md bg-red-50 p-3">
          <p className="text-sm text-red-700">{calcError}</p>
        </div>
      )}

      {closeError && (
        <div className="rounded-md bg-red-50 p-3">
          <p className="text-sm text-red-700">{closeError}</p>
        </div>
      )}

      {/* Preview */}
      {preview && (
        <div className="space-y-4">
          {/* Metric cards */}
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4">
            <MetricCard
              label="Faktura dodavatele"
              value={`${formatCZK(preview.totalInvoiceAmount)} Kč`}
            />
            <MetricCard
              label="Hlavní vodoměr"
              value={`${formatNumber(preview.mainMeterConsumption)} m³`}
            />
            <MetricCard
              label="Součet dílčích"
              value={`${formatNumber(preview.totalHouseConsumption)} m³`}
            />
            <MetricCard
              label="Ztráta"
              value={`${formatNumber(preview.totalLoss)} m³`}
              variant={preview.totalLoss > 0 ? 'warning' : 'normal'}
            />
          </div>

          {/* Settlement table */}
          <SettlementTable houses={preview.houses} />

          {/* Close button */}
          <div className="flex justify-end">
            <button
              onClick={() => setShowCloseConfirm(true)}
              disabled={closing}
              className="rounded-md bg-red-600 px-4 py-2 text-sm font-medium text-white hover:bg-red-700 focus:outline-none focus:ring-2 focus:ring-red-500 focus:ring-offset-2 disabled:cursor-not-allowed disabled:opacity-50"
            >
              {closing
                ? 'Uzavírání...'
                : 'Uzavřít období + generovat PDF'}
            </button>
          </div>
        </div>
      )}

      <ConfirmDialog
        isOpen={showCloseConfirm}
        title="Uzavřít zúčtovací období"
        message="Opravdu chcete uzavřít období? Tato akce je nevratná. Budou vygenerovány PDF vyúčtování pro všechny domácnosti."
        confirmLabel="Uzavřít období"
        confirmVariant="danger"
        onConfirm={() => void handleClose()}
        onCancel={() => setShowCloseConfirm(false)}
      />
    </div>
  );
}

// ─── Closed Period Detail ──────────────────────────────────────────────────────

function ClosedPeriodDetail({ period }: { period: BillingPeriodResponse }) {
  const { getAccessToken } = useAuth();
  const fetchSettlements = useCallback(
    () => getSettlements(period.id),
    [period.id],
  );
  const {
    data: settlements,
    loading,
    error,
  } = useApi<SettlementResponse[]>(fetchSettlements, [period.id]);
  const [downloadError, setDownloadError] = useState<string | null>(null);

  const downloadFile = async (path: string, filename: string) => {
    try {
      setDownloadError(null);
      const token = await getAccessToken();
      const baseUrl = import.meta.env.VITE_API_BASE_URL || '/api';
      const response = await fetch(`${baseUrl}${path}`, {
        headers: token ? { Authorization: `Bearer ${token}` } : {},
      });
      if (!response.ok) throw new Error('Download selhalo');
      const blob = await response.blob();
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = filename;
      document.body.appendChild(a);
      a.click();
      document.body.removeChild(a);
      URL.revokeObjectURL(url);
    } catch {
      setDownloadError('Stahování se nezdařilo');
    }
  };

  const handleDownloadPdf = (houseId: string, houseName?: string) => {
    const periodId = encodeURIComponent(period.id);
    const hId = encodeURIComponent(houseId);
    void downloadFile(
      `/billing-periods/${periodId}/settlements/${hId}/pdf`,
      `vyuctovani-${houseName ?? houseId}.pdf`,
    );
  };

  const handleDownloadAll = () => {
    const periodId = encodeURIComponent(period.id);
    void downloadFile(
      `/billing-periods/${periodId}/pdf`,
      `vyuctovani-${period.name}.zip`,
    );
  };

  if (loading) {
    return (
      <div className="flex items-center justify-center py-8">
        <div className="h-6 w-6 animate-spin rounded-full border-4 border-blue-600 border-t-transparent" />
      </div>
    );
  }

  if (error) {
    return (
      <div className="rounded-md bg-red-50 p-3">
        <p className="text-sm text-red-700">{error}</p>
      </div>
    );
  }

  if (!settlements || settlements.length === 0) {
    return (
      <p className="text-sm text-gray-500">Žádná vyúčtování pro toto období.</p>
    );
  }

  return (
    <div className="space-y-4">
      {downloadError && (
        <div className="rounded-md bg-red-50 p-3">
          <p className="text-sm text-red-700">{downloadError}</p>
        </div>
      )}
      <ClosedSettlementTable
        settlements={settlements}
        onDownloadPdf={handleDownloadPdf}
      />
      <div className="flex justify-end">
        <button
          onClick={handleDownloadAll}
          className="rounded-md bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700 focus:outline-none focus:ring-2 focus:ring-blue-500 focus:ring-offset-2"
        >
          Stáhnout vše (ZIP)
        </button>
      </div>
    </div>
  );
}

// ─── Settlement Table (Preview) ────────────────────────────────────────────────

function SettlementTable({
  houses,
}: {
  houses: SettlementPreviewResponse['houses'];
}) {
  const totals = houses.reduce(
    (acc, h) => ({
      consumption: acc.consumption + h.consumptionM3,
      loss: acc.loss + h.lossAllocatedM3,
      share: acc.share + h.sharePercent,
      amount: acc.amount + h.calculatedAmount,
      advances: acc.advances + h.totalAdvances,
      balance: acc.balance + h.balance,
    }),
    { consumption: 0, loss: 0, share: 0, amount: 0, advances: 0, balance: 0 },
  );

  return (
    <div className="overflow-x-auto rounded-lg border border-gray-200">
      <table className="min-w-full divide-y divide-gray-200">
        <thead className="bg-gray-50">
          <tr>
            <th className="px-4 py-3 text-left text-xs font-medium uppercase tracking-wider text-gray-500">
              Dům
            </th>
            <th className="px-4 py-3 text-right text-xs font-medium uppercase tracking-wider text-gray-500">
              Spotřeba m³
            </th>
            <th className="px-4 py-3 text-right text-xs font-medium uppercase tracking-wider text-gray-500">
              Ztráta m³
            </th>
            <th className="px-4 py-3 text-right text-xs font-medium uppercase tracking-wider text-gray-500">
              Podíl %
            </th>
            <th className="px-4 py-3 text-right text-xs font-medium uppercase tracking-wider text-gray-500">
              Částka Kč
            </th>
            <th className="px-4 py-3 text-right text-xs font-medium uppercase tracking-wider text-gray-500">
              Zálohy Kč
            </th>
            <th className="px-4 py-3 text-right text-xs font-medium uppercase tracking-wider text-gray-500">
              Výsledek Kč
            </th>
          </tr>
        </thead>
        <tbody className="divide-y divide-gray-200 bg-white">
          {houses.map((house) => (
            <tr key={house.houseId} className="hover:bg-gray-50">
              <td className="whitespace-nowrap px-4 py-3 text-sm font-medium text-gray-900">
                {house.houseName}
              </td>
              <td className="whitespace-nowrap px-4 py-3 text-right text-sm font-mono text-gray-700">
                {formatNumber(house.consumptionM3)}
              </td>
              <td className="whitespace-nowrap px-4 py-3 text-right text-sm font-mono text-gray-700">
                {formatNumber(house.lossAllocatedM3)}
              </td>
              <td className="whitespace-nowrap px-4 py-3 text-right text-sm font-mono text-gray-700">
                {formatPercent(house.sharePercent)}
              </td>
              <td className="whitespace-nowrap px-4 py-3 text-right text-sm font-mono text-gray-700">
                {formatCZK(house.calculatedAmount)}
              </td>
              <td className="whitespace-nowrap px-4 py-3 text-right text-sm font-mono text-gray-700">
                {formatCZK(house.totalAdvances)}
              </td>
              <td
                className={`whitespace-nowrap px-4 py-3 text-right text-sm font-mono font-semibold ${
                  house.balance > 0
                    ? 'text-red-600'
                    : house.balance < 0
                      ? 'text-green-600'
                      : 'text-gray-700'
                }`}
              >
                {formatCZK(house.balance)}
                {house.balance > 0 && (
                  <span className="ml-1 text-xs font-normal">doplatek</span>
                )}
                {house.balance < 0 && (
                  <span className="ml-1 text-xs font-normal">přeplatek</span>
                )}
              </td>
            </tr>
          ))}
          {/* Totals row */}
          <tr className="bg-gray-50 font-bold">
            <td className="whitespace-nowrap px-4 py-3 text-sm text-gray-900">
              Celkem
            </td>
            <td className="whitespace-nowrap px-4 py-3 text-right text-sm font-mono text-gray-900">
              {formatNumber(totals.consumption)}
            </td>
            <td className="whitespace-nowrap px-4 py-3 text-right text-sm font-mono text-gray-900">
              {formatNumber(totals.loss)}
            </td>
            <td className="whitespace-nowrap px-4 py-3 text-right text-sm font-mono text-gray-900">
              {formatPercent(totals.share)}
            </td>
            <td className="whitespace-nowrap px-4 py-3 text-right text-sm font-mono text-gray-900">
              {formatCZK(totals.amount)}
            </td>
            <td className="whitespace-nowrap px-4 py-3 text-right text-sm font-mono text-gray-900">
              {formatCZK(totals.advances)}
            </td>
            <td className="whitespace-nowrap px-4 py-3 text-right text-sm font-mono text-gray-900">
              {formatCZK(totals.balance)}
            </td>
          </tr>
        </tbody>
      </table>
    </div>
  );
}

// ─── Closed Settlement Table ───────────────────────────────────────────────────

function ClosedSettlementTable({
  settlements,
  onDownloadPdf,
}: {
  settlements: SettlementResponse[];
  onDownloadPdf: (houseId: string, houseName?: string) => void;
}) {
  return (
    <div className="overflow-x-auto rounded-lg border border-gray-200">
      <table className="min-w-full divide-y divide-gray-200">
        <thead className="bg-gray-50">
          <tr>
            <th className="px-4 py-3 text-left text-xs font-medium uppercase tracking-wider text-gray-500">
              Dům
            </th>
            <th className="px-4 py-3 text-right text-xs font-medium uppercase tracking-wider text-gray-500">
              Spotřeba m³
            </th>
            <th className="px-4 py-3 text-right text-xs font-medium uppercase tracking-wider text-gray-500">
              Ztráta m³
            </th>
            <th className="px-4 py-3 text-right text-xs font-medium uppercase tracking-wider text-gray-500">
              Podíl %
            </th>
            <th className="px-4 py-3 text-right text-xs font-medium uppercase tracking-wider text-gray-500">
              Částka Kč
            </th>
            <th className="px-4 py-3 text-right text-xs font-medium uppercase tracking-wider text-gray-500">
              Zálohy Kč
            </th>
            <th className="px-4 py-3 text-right text-xs font-medium uppercase tracking-wider text-gray-500">
              Výsledek Kč
            </th>
            <th className="px-4 py-3 text-right text-xs font-medium uppercase tracking-wider text-gray-500">
              PDF
            </th>
          </tr>
        </thead>
        <tbody className="divide-y divide-gray-200 bg-white">
          {settlements.map((s) => (
            <tr key={s.houseId} className="hover:bg-gray-50">
              <td className="whitespace-nowrap px-4 py-3 text-sm font-medium text-gray-900">
                {s.houseName}
              </td>
              <td className="whitespace-nowrap px-4 py-3 text-right text-sm font-mono text-gray-700">
                {formatNumber(s.consumptionM3)}
              </td>
              <td className="whitespace-nowrap px-4 py-3 text-right text-sm font-mono text-gray-700">
                {formatNumber(s.lossAllocatedM3)}
              </td>
              <td className="whitespace-nowrap px-4 py-3 text-right text-sm font-mono text-gray-700">
                {formatPercent(s.sharePercent)}
              </td>
              <td className="whitespace-nowrap px-4 py-3 text-right text-sm font-mono text-gray-700">
                {formatCZK(s.calculatedAmount)}
              </td>
              <td className="whitespace-nowrap px-4 py-3 text-right text-sm font-mono text-gray-700">
                {formatCZK(s.totalAdvances)}
              </td>
              <td
                className={`whitespace-nowrap px-4 py-3 text-right text-sm font-mono font-semibold ${
                  s.balance > 0
                    ? 'text-red-600'
                    : s.balance < 0
                      ? 'text-green-600'
                      : 'text-gray-700'
                }`}
              >
                {formatCZK(s.balance)}
                {s.balance > 0 && (
                  <span className="ml-1 text-xs font-normal">doplatek</span>
                )}
                {s.balance < 0 && (
                  <span className="ml-1 text-xs font-normal">přeplatek</span>
                )}
              </td>
              <td className="whitespace-nowrap px-4 py-3 text-right">
                <button
                  onClick={() => onDownloadPdf(s.houseId, s.houseName)}
                  className="text-sm text-blue-600 hover:text-blue-800 hover:underline"
                >
                  Stáhnout
                </button>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

// ─── Member View ───────────────────────────────────────────────────────────────

interface MemberBillingViewProps {
  periods: BillingPeriodResponse[] | null;
  periodsLoading: boolean;
  periodsError: string | null;
  houseId: string | null;
}

function MemberBillingView({
  periods,
  periodsLoading,
  periodsError,
  houseId,
}: MemberBillingViewProps) {
  // Members only see closed periods
  const closedPeriods = periods?.filter((p) => p.status === 'Closed') ?? [];

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-bold text-gray-900">Vyúčtování</h1>
        <p className="mt-1 text-sm text-gray-600">
          Přehled vašeho vyúčtování za uzavřená období
        </p>
      </div>

      {periodsLoading && (
        <div className="flex items-center justify-center py-12">
          <div className="h-8 w-8 animate-spin rounded-full border-4 border-blue-600 border-t-transparent" />
        </div>
      )}

      {periodsError && (
        <div className="rounded-md bg-red-50 p-4">
          <p className="text-sm text-red-700">{periodsError}</p>
        </div>
      )}

      {!periodsLoading && !periodsError && (
        <>
          {closedPeriods.length === 0 ? (
            <div className="rounded-md bg-gray-50 py-12 text-center">
              <p className="text-sm text-gray-500">
                Zatím není k dispozici žádné uzavřené vyúčtování.
              </p>
            </div>
          ) : (
            <div className="space-y-4">
              {closedPeriods.map((period) => (
                <MemberPeriodCard
                  key={period.id}
                  period={period}
                  houseId={houseId}
                />
              ))}
            </div>
          )}
        </>
      )}
    </div>
  );
}

function MemberPeriodCard({
  period,
  houseId,
}: {
  period: BillingPeriodResponse;
  houseId: string | null;
}) {
  const [isExpanded, setIsExpanded] = useState(false);

  return (
    <div className="rounded-lg border border-gray-200 bg-white shadow-sm">
      <button
        onClick={() => setIsExpanded((prev) => !prev)}
        className="flex w-full items-center justify-between px-6 py-4 text-left hover:bg-gray-50"
      >
        <div>
          <h3 className="text-base font-semibold text-gray-900">
            {period.name}
          </h3>
          <p className="mt-0.5 text-sm text-gray-500">
            {formatDate(period.dateFrom)} – {formatDate(period.dateTo)}
          </p>
        </div>
        <div className="flex items-center gap-3">
          <StatusBadge status={period.status} />
          <svg
            className={`h-5 w-5 text-gray-400 transition-transform ${
              isExpanded ? 'rotate-180' : ''
            }`}
            fill="none"
            viewBox="0 0 24 24"
            strokeWidth="1.5"
            stroke="currentColor"
          >
            <path
              strokeLinecap="round"
              strokeLinejoin="round"
              d="m19.5 8.25-7.5 7.5-7.5-7.5"
            />
          </svg>
        </div>
      </button>

      {isExpanded && (
        <div className="border-t border-gray-200 px-6 py-4">
          <MemberSettlementDetail periodId={period.id} houseId={houseId} />
        </div>
      )}
    </div>
  );
}

function MemberSettlementDetail({
  periodId,
  houseId,
}: {
  periodId: string;
  houseId: string | null;
}) {
  const { getAccessToken } = useAuth();
  const fetchSettlements = useCallback(
    () => getSettlements(periodId),
    [periodId],
  );
  const { data: settlements, loading, error } = useApi(fetchSettlements, [periodId]);
  const [downloadError, setDownloadError] = useState<string | null>(null);

  if (loading) {
    return (
      <div className="flex items-center justify-center py-6">
        <div className="h-6 w-6 animate-spin rounded-full border-4 border-blue-600 border-t-transparent" />
      </div>
    );
  }

  if (error) {
    return (
      <div className="rounded-md bg-red-50 p-3">
        <p className="text-sm text-red-700">{error}</p>
      </div>
    );
  }

  // For member view, the API should return only their settlement, but filter just in case
  const mySettlement = settlements?.find((s) => s.houseId === houseId) ?? settlements?.[0];

  if (!mySettlement) {
    return (
      <p className="text-sm text-gray-500">
        Vyúčtování pro vaši domácnost nebylo nalezeno.
      </p>
    );
  }

  const handleDownloadPdf = async () => {
    try {
      setDownloadError(null);
      const token = await getAccessToken();
      const pId = encodeURIComponent(periodId);
      const hId = encodeURIComponent(mySettlement.houseId);
      const baseUrl = import.meta.env.VITE_API_BASE_URL || '/api';
      const response = await fetch(`${baseUrl}/billing-periods/${pId}/settlements/${hId}/pdf`, {
        headers: token ? { Authorization: `Bearer ${token}` } : {},
      });
      if (!response.ok) throw new Error('Download selhalo');
      const blob = await response.blob();
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `vyuctovani-${mySettlement.houseName ?? mySettlement.houseId}.pdf`;
      document.body.appendChild(a);
      a.click();
      document.body.removeChild(a);
      URL.revokeObjectURL(url);
    } catch {
      setDownloadError('Stahování se nezdařilo');
    }
  };

  return (
    <div className="space-y-4">
      {downloadError && (
        <div className="rounded-md bg-red-50 p-3">
          <p className="text-sm text-red-700">{downloadError}</p>
        </div>
      )}
      <div className="grid grid-cols-2 gap-4 sm:grid-cols-3">
        <DetailItem label="Spotřeba" value={`${formatNumber(mySettlement.consumptionM3)} m³`} />
        <DetailItem label="Ztráta" value={`${formatNumber(mySettlement.lossAllocatedM3)} m³`} />
        <DetailItem label="Podíl" value={`${formatPercent(mySettlement.sharePercent)} %`} />
        <DetailItem label="Částka" value={`${formatCZK(mySettlement.calculatedAmount)} Kč`} />
        <DetailItem label="Zálohy" value={`${formatCZK(mySettlement.totalAdvances)} Kč`} />
        <DetailItem
          label="Výsledek"
          value={`${formatCZK(mySettlement.balance)} Kč`}
          valueClassName={
            mySettlement.balance > 0
              ? 'text-red-600'
              : mySettlement.balance < 0
                ? 'text-green-600'
                : 'text-gray-900'
          }
          note={
            mySettlement.balance > 0
              ? 'doplatek'
              : mySettlement.balance < 0
                ? 'přeplatek'
                : undefined
          }
        />
      </div>
      <div className="flex justify-end">
        <button
          onClick={() => void handleDownloadPdf()}
          className="rounded-md bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700 focus:outline-none focus:ring-2 focus:ring-blue-500 focus:ring-offset-2"
        >
          Stáhnout vyúčtování (PDF)
        </button>
      </div>
    </div>
  );
}

function DetailItem({
  label,
  value,
  valueClassName,
  note,
}: {
  label: string;
  value: string;
  valueClassName?: string;
  note?: string;
}) {
  return (
    <div>
      <p className="text-xs font-medium text-gray-500">{label}</p>
      <p className={`mt-0.5 text-lg font-semibold ${valueClassName ?? 'text-gray-900'}`}>
        {value}
      </p>
      {note && <p className="text-xs text-gray-500">{note}</p>}
    </div>
  );
}

// ─── Metric Card ───────────────────────────────────────────────────────────────

function MetricCard({
  label,
  value,
  variant = 'normal',
}: {
  label: string;
  value: string;
  variant?: 'normal' | 'warning';
}) {
  return (
    <div
      className={`rounded-lg border p-4 ${
        variant === 'warning'
          ? 'border-amber-200 bg-amber-50'
          : 'border-gray-200 bg-white'
      }`}
    >
      <p className="text-sm font-medium text-gray-500">{label}</p>
      <p
        className={`mt-1 text-xl font-semibold ${
          variant === 'warning' ? 'text-amber-700' : 'text-gray-900'
        }`}
      >
        {value}
      </p>
    </div>
  );
}
