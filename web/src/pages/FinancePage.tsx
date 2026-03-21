import { useCallback, useRef, useState } from 'react';
import { useAuth } from '../auth/AuthContext';
import { useApi } from '../hooks/useApi';
import {
  getFinanceRecords,
  getFinanceSummary,
  createFinanceRecord,
  exportFinancePdf,
  exportFinanceExcel,
} from '../api/finance';
import { MetricCard } from '../components/MetricCard';
import { Spinner } from '../components/Spinner';
import type {
  FinanceResponse,
  FinanceSummaryResponse,
  CreateFinanceRequest,
  FinancialRecordType,
} from '../types';

const CURRENT_YEAR = new Date().getFullYear();

const YEAR_OPTIONS: number[] = [];
for (let y = CURRENT_YEAR; y >= 2020; y--) {
  YEAR_OPTIONS.push(y);
}

const FINANCE_CATEGORIES = [
  { key: '', label: 'Vše' },
  { key: 'voda', label: 'Voda' },
  { key: 'elektro', label: 'Elektro' },
  { key: 'udrzba', label: 'Údržba' },
  { key: 'pojisteni', label: 'Pojištění' },
  { key: 'jine', label: 'Jiné' },
] as const;

const CATEGORY_LABELS: Record<string, string> = {
  voda: 'Voda',
  elektro: 'Elektro',
  udrzba: 'Údržba',
  pojisteni: 'Pojištění',
  jine: 'Jiné',
};

const CATEGORY_BADGE_COLORS: Record<string, string> = {
  voda: 'bg-blue-100 text-blue-700',
  elektro: 'bg-yellow-100 text-yellow-700',
  udrzba: 'bg-orange-100 text-orange-700',
  pojisteni: 'bg-purple-100 text-purple-700',
  jine: 'bg-gray-100 text-gray-700',
};

const TYPE_LABELS: Record<FinancialRecordType, string> = {
  Income: 'Příjem',
  Expense: 'Výdaj',
};

const formatCZK = (value: number): string =>
  new Intl.NumberFormat('cs-CZ', {
    style: 'currency',
    currency: 'CZK',
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  }).format(value);

const formatDate = (dateStr: string): string =>
  new Intl.DateTimeFormat('cs-CZ').format(new Date(dateStr));

export function FinancePage() {
  const { user, getAccessToken } = useAuth();
  const isAdmin = user?.role === 'Admin';
  const canExport = user?.role === 'Admin' || user?.role === 'Accountant';

  const [selectedYear, setSelectedYear] = useState(CURRENT_YEAR);
  const [selectedCategory, setSelectedCategory] = useState('');
  const [showForm, setShowForm] = useState(false);
  const [exportingPdf, setExportingPdf] = useState(false);
  const [exportingExcel, setExportingExcel] = useState(false);
  const [exportError, setExportError] = useState<string | null>(null);

  // Fetch summary
  const fetchSummary = useCallback(
    () => getFinanceSummary(selectedYear),
    [selectedYear],
  );
  const {
    data: summary,
    loading: summaryLoading,
    error: summaryError,
  } = useApi(fetchSummary, [selectedYear]);

  // Fetch records
  const fetchRecords = useCallback(
    () =>
      getFinanceRecords(
        selectedYear,
        selectedCategory || undefined,
      ),
    [selectedYear, selectedCategory],
  );
  const {
    data: records,
    loading: recordsLoading,
    error: recordsError,
    refetch,
  } = useApi(fetchRecords, [selectedYear, selectedCategory]);

  const handleRecordCreated = () => {
    setShowForm(false);
    refetch();
  };

  const handleExportPdf = async () => {
    setExportingPdf(true);
    setExportError(null);
    try {
      await exportFinancePdf(selectedYear, getAccessToken);
    } catch {
      setExportError('Export PDF se nezdaril');
    } finally {
      setExportingPdf(false);
    }
  };

  const handleExportExcel = async () => {
    setExportingExcel(true);
    setExportError(null);
    try {
      await exportFinanceExcel(selectedYear, getAccessToken);
    } catch {
      setExportError('Export Excel se nezdaril');
    } finally {
      setExportingExcel(false);
    }
  };

  const error = summaryError ?? recordsError ?? exportError;

  return (
    <div>
      <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
        <h1 className="text-2xl font-bold text-gray-900">Hospodaření</h1>
        <div className="flex flex-wrap gap-2">
          {canExport && (
            <>
              <button
                onClick={() => void handleExportPdf()}
                disabled={exportingPdf}
                className="inline-flex items-center rounded-md border border-gray-300 bg-white px-3 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50 disabled:opacity-50"
              >
                {exportingPdf ? 'Exportuji...' : 'Export PDF'}
              </button>
              <button
                onClick={() => void handleExportExcel()}
                disabled={exportingExcel}
                className="inline-flex items-center rounded-md border border-gray-300 bg-white px-3 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50 disabled:opacity-50"
              >
                {exportingExcel ? 'Exportuji...' : 'Export Excel'}
              </button>
            </>
          )}
          {isAdmin && (
            <button
              onClick={() => setShowForm(!showForm)}
              className="inline-flex items-center rounded-md bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700"
            >
              {showForm ? 'Zavřít formulář' : 'Přidat záznam'}
            </button>
          )}
        </div>
      </div>

      {/* Error */}
      {error && (
        <div className="mt-4 rounded-md bg-red-50 p-3">
          <p className="text-sm text-red-700">{error}</p>
        </div>
      )}

      {/* Summary cards */}
      {summaryLoading && (
        <div className="mt-6 flex justify-center">
          <Spinner />
        </div>
      )}
      {!summaryLoading && summary && (
        <SummaryCards summary={summary} />
      )}

      {/* Add record form (admin only, collapsible) */}
      {isAdmin && showForm && (
        <AddRecordForm
          onCreated={handleRecordCreated}
          onCancel={() => setShowForm(false)}
        />
      )}

      {/* Filters */}
      <div className="mt-6 flex flex-col gap-3 sm:flex-row sm:items-center">
        <div>
          <label htmlFor="year-select" className="mr-2 text-sm font-medium text-gray-700">
            Rok:
          </label>
          <select
            id="year-select"
            value={selectedYear}
            onChange={(e) => setSelectedYear(Number(e.target.value))}
            className="rounded-md border border-gray-300 px-3 py-1.5 text-sm shadow-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
          >
            {YEAR_OPTIONS.map((y) => (
              <option key={y} value={y}>
                {y}
              </option>
            ))}
          </select>
        </div>
        <div>
          <label htmlFor="category-select" className="mr-2 text-sm font-medium text-gray-700">
            Kategorie:
          </label>
          <select
            id="category-select"
            value={selectedCategory}
            onChange={(e) => setSelectedCategory(e.target.value)}
            className="rounded-md border border-gray-300 px-3 py-1.5 text-sm shadow-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
          >
            {FINANCE_CATEGORIES.map((cat) => (
              <option key={cat.key} value={cat.key}>
                {cat.label}
              </option>
            ))}
          </select>
        </div>
      </div>

      {/* Records table */}
      {recordsLoading && (
        <div className="mt-6 flex justify-center">
          <Spinner size="lg" />
        </div>
      )}

      {!recordsLoading && records && records.length === 0 && (
        <div className="mt-8 text-center">
          <p className="text-gray-500">Žádné záznamy pro vybrané období.</p>
        </div>
      )}

      {!recordsLoading && records && records.length > 0 && (
        <RecordsTable records={records} />
      )}
    </div>
  );
}

// ─── Summary Cards ──────────────────────────────────────────────────────────

function SummaryCards({ summary }: { summary: FinanceSummaryResponse }) {
  return (
    <div className="mt-6 grid grid-cols-1 gap-4 sm:grid-cols-3">
      <MetricCard
        title="Celkové příjmy"
        value={formatCZK(summary.totalIncome)}
        subtitle={`Rok ${String(summary.year)}`}
      />
      <MetricCard
        title="Celkové výdaje"
        value={formatCZK(summary.totalExpenses)}
        subtitle={`Rok ${String(summary.year)}`}
      />
      <div className="rounded-lg bg-white p-5 shadow-sm">
        <p className="text-sm font-medium text-gray-500">Bilance</p>
        <p
          className={`mt-1 text-2xl font-bold ${
            summary.balance >= 0 ? 'text-green-600' : 'text-red-600'
          }`}
        >
          {formatCZK(summary.balance)}
        </p>
        <p className="mt-1 text-sm text-gray-400">Rok {summary.year}</p>
      </div>
    </div>
  );
}

// ─── Records Table ──────────────────────────────────────────────────────────

function RecordsTable({ records }: { records: FinanceResponse[] }) {
  // Sort by date descending
  const sorted = [...records].sort(
    (a, b) => new Date(b.date).getTime() - new Date(a.date).getTime(),
  );

  return (
    <>
      {/* Desktop table */}
      <div className="mt-6 hidden overflow-x-auto md:block">
        <table className="min-w-full divide-y divide-gray-200">
          <thead className="bg-gray-50">
            <tr>
              <th className="px-4 py-3 text-left text-xs font-medium uppercase tracking-wider text-gray-500">
                Datum
              </th>
              <th className="px-4 py-3 text-left text-xs font-medium uppercase tracking-wider text-gray-500">
                Typ
              </th>
              <th className="px-4 py-3 text-left text-xs font-medium uppercase tracking-wider text-gray-500">
                Kategorie
              </th>
              <th className="px-4 py-3 text-left text-xs font-medium uppercase tracking-wider text-gray-500">
                Popis
              </th>
              <th className="px-4 py-3 text-right text-xs font-medium uppercase tracking-wider text-gray-500">
                Částka
              </th>
            </tr>
          </thead>
          <tbody className="divide-y divide-gray-200 bg-white">
            {sorted.map((rec) => (
              <tr key={rec.id} className="hover:bg-gray-50">
                <td className="whitespace-nowrap px-4 py-3 text-sm text-gray-500">
                  {formatDate(rec.date)}
                </td>
                <td className="whitespace-nowrap px-4 py-3 text-sm">
                  <span
                    className={`inline-flex rounded-full px-2 py-0.5 text-xs font-medium ${
                      rec.type === 'Income'
                        ? 'bg-green-100 text-green-700'
                        : 'bg-red-100 text-red-700'
                    }`}
                  >
                    {TYPE_LABELS[rec.type]}
                  </span>
                </td>
                <td className="whitespace-nowrap px-4 py-3 text-sm">
                  <span
                    className={`inline-flex rounded-full px-2 py-0.5 text-xs font-medium ${
                      CATEGORY_BADGE_COLORS[rec.category] ?? 'bg-gray-100 text-gray-700'
                    }`}
                  >
                    {CATEGORY_LABELS[rec.category] ?? rec.category}
                  </span>
                </td>
                <td className="px-4 py-3 text-sm text-gray-700">
                  {rec.description}
                </td>
                <td
                  className={`whitespace-nowrap px-4 py-3 text-right text-sm font-medium ${
                    rec.type === 'Income' ? 'text-green-600' : 'text-red-600'
                  }`}
                >
                  {rec.type === 'Income' ? '+' : '-'}{formatCZK(rec.amount)}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {/* Mobile cards */}
      <div className="mt-6 space-y-3 md:hidden">
        {sorted.map((rec) => (
          <div key={rec.id} className="rounded-lg bg-white p-4 shadow-sm">
            <div className="flex items-start justify-between">
              <div className="min-w-0 flex-1">
                <p className="text-sm font-medium text-gray-900">
                  {rec.description}
                </p>
                <div className="mt-1 flex flex-wrap items-center gap-2">
                  <span
                    className={`inline-flex rounded-full px-2 py-0.5 text-xs font-medium ${
                      rec.type === 'Income'
                        ? 'bg-green-100 text-green-700'
                        : 'bg-red-100 text-red-700'
                    }`}
                  >
                    {TYPE_LABELS[rec.type]}
                  </span>
                  <span
                    className={`inline-flex rounded-full px-2 py-0.5 text-xs font-medium ${
                      CATEGORY_BADGE_COLORS[rec.category] ?? 'bg-gray-100 text-gray-700'
                    }`}
                  >
                    {CATEGORY_LABELS[rec.category] ?? rec.category}
                  </span>
                  <span className="text-xs text-gray-500">
                    {formatDate(rec.date)}
                  </span>
                </div>
              </div>
              <p
                className={`ml-3 text-sm font-medium ${
                  rec.type === 'Income' ? 'text-green-600' : 'text-red-600'
                }`}
              >
                {rec.type === 'Income' ? '+' : '-'}{formatCZK(rec.amount)}
              </p>
            </div>
          </div>
        ))}
      </div>
    </>
  );
}

// ─── Add Record Form ────────────────────────────────────────────────────────

interface AddRecordFormProps {
  onCreated: () => void;
  onCancel: () => void;
}

function AddRecordForm({ onCreated, onCancel }: AddRecordFormProps) {
  const [type, setType] = useState<FinancialRecordType>('Expense');
  const [category, setCategory] = useState('voda');
  const [amount, setAmount] = useState('');
  const [date, setDate] = useState('');
  const [description, setDescription] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [formError, setFormError] = useState<string | null>(null);
  const submittingRef = useRef(false);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (submittingRef.current) return;

    const parsedAmount = parseFloat(amount.replace(',', '.'));
    if (isNaN(parsedAmount) || parsedAmount <= 0) {
      setFormError('Zadejte platnou částku');
      return;
    }
    if (!date) {
      setFormError('Zadejte datum');
      return;
    }
    if (!description.trim()) {
      setFormError('Zadejte popis');
      return;
    }

    submittingRef.current = true;
    setSubmitting(true);
    setFormError(null);

    const data: CreateFinanceRequest = {
      type,
      category,
      amount: parsedAmount,
      date,
      description: description.trim(),
    };

    try {
      await createFinanceRecord(data);
      onCreated();
    } catch {
      setFormError('Uložení se nezdařilo');
    } finally {
      setSubmitting(false);
      submittingRef.current = false;
    }
  };

  return (
    <div className="mt-6 rounded-lg bg-white p-6 shadow-sm">
      <h2 className="text-lg font-semibold text-gray-900">Nový záznam</h2>
      <form onSubmit={(e) => void handleSubmit(e)} className="mt-4 space-y-4">
        {/* Type radio */}
        <div>
          <span className="block text-sm font-medium text-gray-700">Typ</span>
          <div className="mt-1 flex gap-4">
            <label className="inline-flex items-center">
              <input
                type="radio"
                name="type"
                value="Income"
                checked={type === 'Income'}
                onChange={() => setType('Income')}
                className="text-blue-600 focus:ring-blue-500"
              />
              <span className="ml-2 text-sm text-gray-700">Příjem</span>
            </label>
            <label className="inline-flex items-center">
              <input
                type="radio"
                name="type"
                value="Expense"
                checked={type === 'Expense'}
                onChange={() => setType('Expense')}
                className="text-blue-600 focus:ring-blue-500"
              />
              <span className="ml-2 text-sm text-gray-700">Výdaj</span>
            </label>
          </div>
        </div>

        {/* Category */}
        <div>
          <label htmlFor="fin-category" className="block text-sm font-medium text-gray-700">
            Kategorie
          </label>
          <select
            id="fin-category"
            value={category}
            onChange={(e) => setCategory(e.target.value)}
            className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-2 text-sm shadow-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500 sm:w-64"
          >
            <option value="voda">Voda</option>
            <option value="elektro">Elektro</option>
            <option value="udrzba">Údržba</option>
            <option value="pojisteni">Pojištění</option>
            <option value="jine">Jiné</option>
          </select>
        </div>

        {/* Amount */}
        <div>
          <label htmlFor="fin-amount" className="block text-sm font-medium text-gray-700">
            Částka (Kč)
          </label>
          <input
            id="fin-amount"
            type="text"
            inputMode="decimal"
            value={amount}
            onChange={(e) => setAmount(e.target.value)}
            placeholder="0,00"
            className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-2 text-sm shadow-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500 sm:w-48"
          />
        </div>

        {/* Date */}
        <div>
          <label htmlFor="fin-date" className="block text-sm font-medium text-gray-700">
            Datum
          </label>
          <input
            id="fin-date"
            type="date"
            value={date}
            onChange={(e) => setDate(e.target.value)}
            className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-2 text-sm shadow-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500 sm:w-48"
          />
        </div>

        {/* Description */}
        <div>
          <label htmlFor="fin-description" className="block text-sm font-medium text-gray-700">
            Popis
          </label>
          <textarea
            id="fin-description"
            value={description}
            onChange={(e) => setDescription(e.target.value)}
            rows={2}
            className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-2 text-sm shadow-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
            placeholder="Popis záznamu"
          />
        </div>

        {/* Error */}
        {formError && (
          <div className="rounded-md bg-red-50 p-3">
            <p className="text-sm text-red-700">{formError}</p>
          </div>
        )}

        {/* Actions */}
        <div className="flex gap-3">
          <button
            type="submit"
            disabled={submitting}
            className="rounded-md bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700 disabled:cursor-not-allowed disabled:opacity-50"
          >
            {submitting ? 'Ukládám...' : 'Uložit'}
          </button>
          <button
            type="button"
            onClick={onCancel}
            disabled={submitting}
            className="rounded-md border border-gray-300 bg-white px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50"
          >
            Zrušit
          </button>
        </div>
      </form>
    </div>
  );
}
