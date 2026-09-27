import { useCallback, useState } from 'react';
import { useAuth } from '../auth/AuthContext';
import { useApi } from '../hooks/useApi';
import { Spinner } from '../components/Spinner';
import { downloadLedgerExport, getHouseLedger, getLedgerOverview, kindLabels } from '../api/ledger';
import type { HouseLedger, LedgerItem, LedgerOverview } from '../api/ledger';
import { formatIsoDay } from '../utils/date';

const inputCls = 'border border-border rounded-lg px-2 py-1.5 text-sm bg-surface-raised focus:border-accent focus:ring-2 focus:ring-accent/20';
const btn = 'rounded-lg border border-border px-2.5 py-1 text-xs font-medium text-text-secondary hover:bg-surface-sunken';
const kc = (v: number) => `${v.toLocaleString('cs-CZ', { minimumFractionDigits: 2, maximumFractionDigits: 2 })} Kč`;

/** Saldo with its meaning in words (X1: positive = přeplatek). */
function SaldoText({ value }: { value: number }) {
  if (value === 0) return <span>vyrovnáno</span>;
  return (
    <span className={value > 0 ? 'text-success' : 'text-danger'}>
      {kc(Math.abs(value))} {value > 0 ? 'přeplatek' : 'nedoplatek'}
    </span>
  );
}

/**
 * House ledger (T07): who paid what, what cost each house bears and its saldo — every cost with its calculation.
 * Members see their own house in detail and the other houses only as a summary; Admin and Accountant see all.
 */
export function LedgerPage() {
  const { user, getAccessToken } = useAuth();
  const isManager = user?.role === 'Admin' || user?.role === 'Accountant';
  const [range, setRange] = useState<{ from?: string; to?: string }>({});
  const [draft, setDraft] = useState<{ from: string; to: string }>({ from: '', to: '' });
  const [selected, setSelected] = useState<string | null>(null);
  const houseId = isManager ? selected : user?.houseId ?? null;

  const { data: overview, loading, error } = useApi<LedgerOverview>(
    useCallback(() => getLedgerOverview(range), [range]), [range],
  );

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-bold text-text-primary">Saldo domu</h1>
        <p className="mt-1 max-w-3xl text-sm text-text-muted">
          Saldo = počáteční podíl ve fondu + platby domu − jeho podíl na nákladech. <strong>Kladné saldo je přeplatek</strong>
          {' '}(spolek dluží domu), záporné nedoplatek. U každého nákladu je vidět, jak vznikl.
        </p>
      </div>

      <form
        className="flex flex-wrap items-end gap-2"
        onSubmit={(e) => { e.preventDefault(); setRange({ from: draft.from || undefined, to: draft.to || undefined }); }}
      >
        <label className="text-xs text-text-secondary">
          <span className="mb-1 block">Od (prázdné = start účtování)</span>
          <input type="date" value={draft.from} onChange={(e) => setDraft({ ...draft, from: e.target.value })} className={inputCls} />
        </label>
        <label className="text-xs text-text-secondary">
          <span className="mb-1 block">Do (prázdné = dnes)</span>
          <input type="date" value={draft.to} onChange={(e) => setDraft({ ...draft, to: e.target.value })} className={inputCls} />
        </label>
        <button type="submit" className="rounded-xl bg-accent px-3 py-1.5 text-sm font-medium text-white hover:bg-accent-hover">Zobrazit</button>
      </form>

      {houseId && <HouseDetail key={`${houseId}-${range.from}-${range.to}`} houseId={houseId} range={range} getToken={getAccessToken} />}

      {error && <div className="rounded-xl bg-danger-light p-4 text-sm text-danger">{error}</div>}
      {loading ? (
        <div className="flex justify-center p-8"><Spinner size="lg" /></div>
      ) : overview && (
        <Overview
          overview={overview}
          selected={houseId}
          onSelect={isManager ? setSelected : undefined}
          onExport={(format) => void downloadLedgerExport('/ledger/overview/export', { ...range, format }, getAccessToken)}
        />
      )}
    </div>
  );
}

function Overview({ overview, selected, onSelect, onExport }: {
  overview: LedgerOverview;
  selected: string | null;
  onSelect?: (houseId: string) => void;
  onExport: (format: 'xlsx' | 'csv') => void;
}) {
  const components = overview.components;
  const warnings = components.flatMap((c) => c.warnings.map((w) => `${c.componentName}: ${w}`));
  return (
    <section className="space-y-2" aria-label="Přehled domů">
      <div className="flex flex-wrap items-baseline justify-between gap-2">
        <h2 className="text-lg font-semibold text-text-primary">Všechny domy {formatIsoDay(overview.from)} – {formatIsoDay(overview.to)}</h2>
        <div className="flex gap-2">
          <button type="button" className={btn} onClick={() => onExport('xlsx')}>Export XLSX</button>
          <button type="button" className={btn} onClick={() => onExport('csv')}>Export CSV</button>
        </div>
      </div>
      <div className="overflow-x-auto rounded-2xl border border-border bg-surface-raised shadow-card">
        <table className="w-full text-sm" aria-label="Saldo domů">
          <thead className="bg-surface-sunken text-xs font-semibold uppercase tracking-wider text-text-muted">
            <tr>
              <th className="px-3 py-2 text-left">Dům</th>
              <th className="px-3 py-2 text-right">Počáteční podíl</th>
              {components.map((c) => <th key={c.componentId} className="px-3 py-2 text-right">{c.componentName}</th>)}
              <th className="px-3 py-2 text-right">Platby</th>
              <th className="px-3 py-2 text-right">Saldo</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-border">
            {overview.houses.map((h) => (
              <tr
                key={h.houseId}
                onClick={onSelect ? () => onSelect(h.houseId) : undefined}
                className={`${onSelect ? 'cursor-pointer hover:bg-surface-sunken/50' : ''} ${selected === h.houseId ? 'bg-accent-light/40' : ''}`}
              >
                <td className="px-3 py-2 font-medium">{h.houseName}</td>
                <td className="px-3 py-2 text-right">{kc(h.opening)}</td>
                {components.map((c) => <td key={c.componentId} className="px-3 py-2 text-right">{kc(h.costs[c.componentId] ?? 0)}</td>)}
                <td className="px-3 py-2 text-right">{kc(h.payments)}</td>
                <td className="whitespace-nowrap px-3 py-2 text-right font-medium"><SaldoText value={h.saldo} /></td>
              </tr>
            ))}
          </tbody>
          <tfoot className="border-t-2 border-border text-xs">
            <tr>
              <td className="px-3 py-2 font-semibold" colSpan={2}>Kontrola: Σ domů = náklad složky</td>
              {components.map((c) => (
                <td
                  key={c.componentId}
                  className={`px-3 py-2 text-right ${c.matches ? 'text-success' : 'bg-danger-light font-semibold text-danger'}`}
                  title={`Rozpočteno ${kc(c.allocatedTotal)}, Σ domů ${kc(c.housesTotal)}`}
                >
                  {c.matches ? `✓ ${kc(c.housesTotal)}` : `✗ ${kc(c.housesTotal)} ≠ ${kc(c.allocatedTotal)}`}
                </td>
              ))}
              <td colSpan={2} />
            </tr>
          </tfoot>
        </table>
      </div>
      {warnings.length > 0 && (
        <ul role="alert" className="space-y-1 rounded-lg bg-warning-light p-2 text-sm text-warning">
          {warnings.map((w) => <li key={w}>{w}</li>)}
        </ul>
      )}
      <p className="text-xs text-text-muted">Náklady jsou kladně; saldo = počáteční podíl + platby − náklady.</p>
    </section>
  );
}

function HouseDetail({ houseId, range, getToken }: {
  houseId: string;
  range: { from?: string; to?: string };
  getToken: () => Promise<string | null>;
}) {
  const [periodId, setPeriodId] = useState<string | undefined>(undefined);
  const { data: ledger, loading, error } = useApi<HouseLedger>(
    useCallback(() => getHouseLedger(houseId, { ...range, ownershipPeriodId: periodId }), [houseId, range, periodId]), [houseId, range, periodId],
  );

  if (loading && !ledger) return <Spinner />;
  if (error) return <div className="rounded-xl bg-danger-light p-4 text-sm text-danger">{error}</div>;
  if (!ledger) return null;

  const exportQuery = { ...range, ownershipPeriodId: periodId };
  return (
    <section className="space-y-3 rounded-2xl border border-border bg-surface-raised p-4 shadow-card" aria-label={`Saldo ${ledger.houseName}`}>
      <div className="flex flex-wrap items-baseline justify-between gap-2">
        <div>
          <h2 className="text-lg font-semibold text-text-primary">{ledger.houseName}</h2>
          <p className="text-xs text-text-muted">
            {formatIsoDay(ledger.from)} – {formatIsoDay(ledger.to)}
            {ledger.ownershipPeriod && ` · vlastník ${ledger.ownershipPeriod.ownerName}`}
          </p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          {ledger.ownershipPeriods.length > 1 && (
            <select
              aria-label="Období vlastnictví"
              value={periodId ?? ledger.ownershipPeriod?.id ?? ''}
              onChange={(e) => setPeriodId(e.target.value)}
              className={inputCls}
            >
              {ledger.ownershipPeriods.map((p) => (
                <option key={p.id} value={p.id}>{p.ownerName} ({formatIsoDay(p.validFrom)} – {p.validTo ? formatIsoDay(p.validTo) : 'dosud'})</option>
              ))}
            </select>
          )}
          <button type="button" className={btn} onClick={() => void downloadLedgerExport(`/ledger/houses/${encodeURIComponent(houseId)}/export`, { ...exportQuery, format: 'xlsx' }, getToken)}>Export XLSX</button>
          <button type="button" className={btn} onClick={() => void downloadLedgerExport(`/ledger/houses/${encodeURIComponent(houseId)}/export`, { ...exportQuery, format: 'csv' }, getToken)}>Export CSV</button>
        </div>
      </div>

      <dl className="grid grid-cols-2 gap-x-6 gap-y-1 text-sm sm:grid-cols-4">
        <div><dt className="text-xs text-text-muted">Počáteční podíl</dt><dd>{kc(ledger.opening)}</dd></div>
        <div><dt className="text-xs text-text-muted">Platby</dt><dd>{kc(ledger.payments)}</dd></div>
        <div><dt className="text-xs text-text-muted">Náklady</dt><dd>{kc(ledger.costs)}</dd></div>
        <div><dt className="text-xs text-text-muted">Saldo</dt><dd className="font-semibold"><SaldoText value={ledger.saldo} /></dd></div>
      </dl>

      <table className="w-full text-sm" aria-label="Položky salda">
        <thead className="text-xs uppercase tracking-wider text-text-muted">
          <tr>
            <th className="py-1 text-left">Datum</th>
            <th className="py-1 text-left">Položka</th>
            <th className="py-1 text-right">Částka</th>
            <th className="py-1 text-right">Saldo</th>
          </tr>
        </thead>
        <tbody className="divide-y divide-border">
          {ledger.items.map((item, index) => <ItemRow key={`${item.date}-${index}`} item={item} />)}
          {ledger.items.length === 0 && <tr><td colSpan={4} className="py-4 text-center text-text-muted">V období nejsou žádné položky.</td></tr>}
        </tbody>
      </table>
    </section>
  );
}

function ItemRow({ item }: { item: LedgerItem }) {
  return (
    <tr className="align-top">
      <td className="whitespace-nowrap py-1.5 pr-3">{formatIsoDay(item.date)}</td>
      <td className="py-1.5 pr-3">
        <span className="text-xs text-text-muted">{kindLabels[item.kind]}{item.componentName ? ` · ${item.componentName}` : ''}</span>
        <p>{item.description}</p>
        {item.detail && (
          <details>
            <summary className="cursor-pointer text-xs font-medium text-accent">Jak vznikl</summary>
            <p className="mt-1 text-xs text-text-secondary">{item.detail.explanation}</p>
          </details>
        )}
      </td>
      <td className={`whitespace-nowrap py-1.5 text-right ${item.amount < 0 ? 'text-danger' : 'text-success'}`}>{item.amount > 0 ? '+' : ''}{kc(item.amount)}</td>
      <td className="whitespace-nowrap py-1.5 text-right">{kc(item.balance)}</td>
    </tr>
  );
}
