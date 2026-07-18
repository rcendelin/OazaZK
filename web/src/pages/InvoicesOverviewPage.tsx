import { useCallback, useMemo, useState } from 'react';
import { useAuth } from '../auth/AuthContext';
import { useApi } from '../hooks/useApi';
import { getReceivedInvoices, downloadReceivedAttachment } from '../api/receivedInvoices';
import { Spinner } from '../components/Spinner';
import { Download } from 'lucide-react';
import type { ReceivedInvoice } from '../types';

const CATEGORY_FILTERS = [
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
  voda: 'bg-accent-light text-accent',
  elektro: 'bg-warning-light text-warning',
  udrzba: 'bg-orange-50 text-orange-600',
  pojisteni: 'bg-purple-50 text-purple-600',
  jine: 'bg-surface-sunken text-text-secondary',
};

const formatCZK = (value: number): string =>
  new Intl.NumberFormat('cs-CZ', {
    style: 'currency',
    currency: 'CZK',
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  }).format(value);

const formatDate = (dateStr: string | null): string =>
  dateStr && !dateStr.startsWith('0001')
    ? new Intl.DateTimeFormat('cs-CZ').format(new Date(dateStr))
    : '—';

export function InvoicesOverviewPage() {
  const { getAccessToken } = useAuth();
  const { data, loading } = useApi<ReceivedInvoice[]>(
    useCallback(() => getReceivedInvoices(), []),
  );

  const [selectedYear, setSelectedYear] = useState<number | 'all'>('all');
  const [selectedCategory, setSelectedCategory] = useState('');
  const [downloadingId, setDownloadingId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const items = useMemo(() => data ?? [], [data]);

  const availableYears = useMemo(() => {
    const years = new Set<number>();
    for (const it of items) {
      const y = new Date(it.date).getFullYear();
      if (!Number.isNaN(y)) years.add(y);
    }
    return [...years].sort((a, b) => b - a);
  }, [items]);

  const filtered = useMemo(() => {
    return items.filter((it) => {
      if (selectedYear !== 'all' && new Date(it.date).getFullYear() !== selectedYear) return false;
      if (selectedCategory === 'voda') return it.source === 'voda';
      if (selectedCategory) return it.source === 'ostatni' && it.category === selectedCategory;
      return true;
    });
  }, [items, selectedYear, selectedCategory]);

  const totals = useMemo(() => {
    let water = 0;
    let other = 0;
    for (const it of filtered) {
      if (it.source === 'voda') water += it.amount;
      else other += it.amount;
    }
    return { water, other, total: water + other };
  }, [filtered]);

  const handleDownload = async (item: ReceivedInvoice) => {
    setError(null);
    setDownloadingId(item.id);
    try {
      await downloadReceivedAttachment(item, getAccessToken);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Stahování přílohy se nezdařilo');
    } finally {
      setDownloadingId(null);
    }
  };

  if (loading) {
    return (
      <div className="flex justify-center p-12">
        <Spinner size="lg" />
      </div>
    );
  }

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-bold text-text-primary">Přehled faktur</h1>
        <p className="mt-1 text-sm text-text-secondary">
          Všechny přijaté faktury — voda i ostatní výdaje. Položky „voda" vstupují do vyúčtování vody.
        </p>
      </div>

      {/* Summary */}
      <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
        <div className="rounded-2xl bg-surface-raised p-4 shadow-card">
          <p className="text-xs font-medium uppercase text-text-muted">Celkem</p>
          <p className="mt-1 text-xl font-bold text-text-primary">{formatCZK(totals.total)}</p>
        </div>
        <div className="rounded-2xl bg-surface-raised p-4 shadow-card">
          <p className="text-xs font-medium uppercase text-accent">Voda</p>
          <p className="mt-1 text-xl font-bold text-text-primary">{formatCZK(totals.water)}</p>
        </div>
        <div className="rounded-2xl bg-surface-raised p-4 shadow-card">
          <p className="text-xs font-medium uppercase text-text-secondary">Ostatní</p>
          <p className="mt-1 text-xl font-bold text-text-primary">{formatCZK(totals.other)}</p>
        </div>
      </div>

      {/* Filters */}
      <div className="flex flex-wrap items-end gap-4">
        <div>
          <label htmlFor="year-filter" className="block text-sm font-medium text-text-secondary mb-1">Rok</label>
          <select
            id="year-filter"
            value={selectedYear === 'all' ? 'all' : String(selectedYear)}
            onChange={(e) => setSelectedYear(e.target.value === 'all' ? 'all' : Number(e.target.value))}
            className="rounded-xl border border-border bg-surface-raised px-3 py-2 text-sm focus:border-accent focus:outline-none focus:ring-2 focus:ring-accent/20"
          >
            <option value="all">Všechny roky</option>
            {availableYears.map((y) => (
              <option key={y} value={y}>{y}</option>
            ))}
          </select>
        </div>
        <div>
          <label htmlFor="category-filter" className="block text-sm font-medium text-text-secondary mb-1">Kategorie</label>
          <select
            id="category-filter"
            value={selectedCategory}
            onChange={(e) => setSelectedCategory(e.target.value)}
            className="rounded-xl border border-border bg-surface-raised px-3 py-2 text-sm focus:border-accent focus:outline-none focus:ring-2 focus:ring-accent/20"
          >
            {CATEGORY_FILTERS.map((c) => (
              <option key={c.key} value={c.key}>{c.label}</option>
            ))}
          </select>
        </div>
      </div>

      {error && (
        <div className="rounded-xl bg-danger-light px-4 py-3 text-sm text-danger">{error}</div>
      )}

      {/* Table */}
      <div className="overflow-x-auto rounded-2xl bg-surface-raised shadow-card">
        <table className="w-full text-sm">
          <thead>
            <tr className="border-b border-border text-left text-[11px] uppercase tracking-wide text-text-muted">
              <th className="px-4 py-3 font-medium">Datum</th>
              <th className="px-4 py-3 font-medium">Kategorie</th>
              <th className="px-4 py-3 font-medium">Popis</th>
              <th className="px-4 py-3 font-medium">Splatnost</th>
              <th className="px-4 py-3 font-medium">Zdroj</th>
              <th className="px-4 py-3 text-right font-medium">Částka</th>
              <th className="px-4 py-3 text-center font-medium">Příloha</th>
            </tr>
          </thead>
          <tbody>
            {filtered.length === 0 ? (
              <tr>
                <td colSpan={7} className="px-4 py-8 text-center text-text-muted">
                  {selectedYear === 'all'
                    ? 'Zatím nejsou žádné přijaté faktury.'
                    : `Za rok ${selectedYear} zatím nejsou žádné faktury.`}
                </td>
              </tr>
            ) : (
              filtered.map((it) => (
                <tr key={`${it.source}-${it.id}`} className="border-b border-border/60 last:border-0">
                  <td className="whitespace-nowrap px-4 py-3 tabular-nums text-text-secondary">{formatDate(it.date)}</td>
                  <td className="px-4 py-3">
                    <span className={`inline-block rounded-full px-2 py-0.5 text-xs font-medium ${CATEGORY_BADGE_COLORS[it.category] ?? 'bg-surface-sunken text-text-secondary'}`}>
                      {CATEGORY_LABELS[it.category] ?? it.category}
                    </span>
                  </td>
                  <td className="px-4 py-3 text-text-primary">{it.description || '—'}</td>
                  <td className="whitespace-nowrap px-4 py-3 tabular-nums text-text-secondary">{formatDate(it.dueDate)}</td>
                  <td className="px-4 py-3">
                    {it.countsTowardWaterSettlement ? (
                      <span className="inline-block rounded-full bg-accent-light px-2 py-0.5 text-xs font-medium text-accent" title="Vstupuje do vyúčtování vody">
                        vyúčtování vody
                      </span>
                    ) : (
                      <span className="text-xs text-text-muted">—</span>
                    )}
                  </td>
                  <td className="whitespace-nowrap px-4 py-3 text-right font-medium tabular-nums text-text-primary">{formatCZK(it.amount)}</td>
                  <td className="px-4 py-3 text-center">
                    {it.hasAttachment ? (
                      <button
                        type="button"
                        onClick={() => void handleDownload(it)}
                        disabled={downloadingId === it.id}
                        className="inline-flex items-center gap-1 text-accent hover:text-accent-hover disabled:opacity-50"
                        title="Stáhnout přílohu"
                      >
                        <Download size={16} />
                      </button>
                    ) : (
                      <span className="text-xs text-text-muted">—</span>
                    )}
                  </td>
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>
    </div>
  );
}
