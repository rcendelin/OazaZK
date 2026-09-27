import { useCallback, useState } from 'react';
import type { FormEvent } from 'react';
import { useAuth } from '../auth/AuthContext';
import { useApi } from '../hooks/useApi';
import { Spinner } from '../components/Spinner';
import { HelpDisclosure } from '../components/help/HelpDisclosure';
import { HelpNote } from '../components/help/HelpNote';
import { HelpTerm } from '../components/help/HelpTerm';
import { ApiError } from '../api/client';
import { getHouses } from '../api/houses';
import { downloadLedgerExport } from '../api/ledger';
import { createInterimClosing, deleteInterimClosing, getInterimClosing, getInterimClosings } from '../api/interimClosings';
import type { ClosingScope, InterimClosing } from '../api/interimClosings';
import type { House } from '../types';
import { formatIsoDay, shiftIsoDate, todayIso } from '../utils/date';

const inputCls = 'border border-border rounded-lg px-2 py-1.5 text-sm bg-surface-raised focus:border-accent focus:ring-2 focus:ring-accent/20';
const primaryBtn = 'rounded-xl bg-accent px-3 py-1.5 text-sm font-medium text-white hover:bg-accent-hover disabled:opacity-50';
const btn = 'rounded-lg border border-border px-2.5 py-1 text-xs font-medium text-text-secondary hover:bg-surface-sunken';
const kc = (v: number) => `${v.toLocaleString('cs-CZ', { minimumFractionDigits: 2, maximumFractionDigits: 2 })} Kč`;

function reasons(err: unknown): string[] {
  if (err instanceof ApiError && err.details.length > 0) return err.details;
  return [err instanceof Error ? err.message : 'Nastala neočekávaná chyba'];
}

function Errors({ items }: { items: string[] }) {
  if (items.length === 0) return null;
  return (
    <ul role="alert" className="space-y-1 rounded-lg bg-danger-light p-2 text-sm text-danger">
      {items.map((m) => <li key={m}>{m}</li>)}
    </ul>
  );
}

/** Interim closings (T08): fix the saldo at a date — all houses (annual closing) or one house (a sale). */
export function InterimClosingsPage() {
  const { user } = useAuth();
  const isAdmin = user?.role === 'Admin';
  const { data: closings, loading, error, refetch } = useApi<InterimClosing[]>(useCallback(() => getInterimClosings(), []));
  const [openId, setOpenId] = useState<string | null>(null);

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-bold text-text-primary">Mezizávěrky</h1>
        <HelpNote sectionId="interimClosing" />
        <HelpDisclosure sectionId="interimClosing" />
      </div>

      {isAdmin && <CreateForm onCreated={(c) => { setOpenId(c.id); refetch(); }} />}

      {error && <div className="rounded-xl bg-danger-light p-4 text-sm text-danger">{error}</div>}
      {loading ? (
        <div className="flex justify-center p-8"><Spinner size="lg" /></div>
      ) : (
        <div className="overflow-x-auto rounded-2xl border border-border bg-surface-raised shadow-card">
          <table className="w-full text-sm" aria-label="Seznam mezizávěrek">
            <thead className="bg-surface-sunken text-xs font-semibold uppercase tracking-wider text-text-muted">
              <tr>
                <th className="px-3 py-2 text-left">Uzavřeno do</th>
                <th className="px-3 py-2 text-left">Rozsah</th>
                <th className="px-3 py-2 text-left">Důvod</th>
                <th className="px-3 py-2 text-left">Kdo</th>
                <th className="px-3 py-2 text-right">Σ saldo</th>
                <th className="px-3 py-2" />
              </tr>
            </thead>
            <tbody className="divide-y divide-border">
              {(closings ?? []).map((c) => (
                <tr key={c.id} className="align-top">
                  <td className="whitespace-nowrap px-3 py-2 font-medium">{formatIsoDay(c.date)}</td>
                  <td className="px-3 py-2">{c.scope === 'All' ? 'všechny domy' : c.houseName}</td>
                  <td className="px-3 py-2 text-text-secondary">{c.reason}</td>
                  <td className="px-3 py-2 text-text-secondary">{c.createdByName ?? '—'}</td>
                  <td className="whitespace-nowrap px-3 py-2 text-right">{kc(c.totalSaldo)}</td>
                  <td className="whitespace-nowrap px-3 py-2 text-right">
                    <button type="button" onClick={() => setOpenId(openId === c.id ? null : c.id)} className="text-xs font-medium text-accent hover:text-accent-hover">
                      {openId === c.id ? 'Skrýt' : 'Detail'}
                    </button>
                  </td>
                </tr>
              ))}
              {closings && closings.length === 0 && (
                <tr><td colSpan={6} className="px-3 py-6 text-center text-text-muted">Zatím žádná mezizávěrka — vše je otevřené.</td></tr>
              )}
            </tbody>
          </table>
        </div>
      )}

      {openId && <ClosingDetail key={openId} id={openId} isAdmin={isAdmin} onDeleted={() => { setOpenId(null); refetch(); }} />}
    </div>
  );
}

function CreateForm({ onCreated }: { onCreated: (c: InterimClosing) => void }) {
  const [date, setDate] = useState(shiftIsoDate(todayIso(), { days: -1 }));
  const [scope, setScope] = useState<ClosingScope>('All');
  const [houseId, setHouseId] = useState('');
  const [reason, setReason] = useState('');
  const [errors, setErrors] = useState<string[]>([]);
  const [busy, setBusy] = useState(false);
  const { data: houses } = useApi<House[]>(useCallback(() => getHouses(), []));

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    setBusy(true);
    setErrors([]);
    try {
      onCreated(await createInterimClosing({ date, scope, houseId: scope === 'House' ? houseId : undefined, reason }));
      setReason('');
    } catch (err) {
      setErrors(reasons(err));
    } finally {
      setBusy(false);
    }
  };

  return (
    <form onSubmit={(e) => void submit(e)} className="space-y-2 rounded-2xl border border-border bg-surface-raised p-4 shadow-card" aria-label="Nová mezizávěrka">
      <h2 className="font-semibold text-text-primary">Nová mezizávěrka<HelpTerm id="mezizaverka" /></h2>
      <div className="flex flex-wrap items-end gap-2">
        <label className="text-xs text-text-secondary">
          <span className="mb-1 block">Uzavřít do (včetně)</span>
          <input type="date" value={date} onChange={(e) => setDate(e.target.value)} className={inputCls} required />
        </label>
        <label className="text-xs text-text-secondary">
          <span className="mb-1 block">Rozsah</span>
          <select value={scope} onChange={(e) => setScope(e.target.value as ClosingScope)} className={inputCls}>
            <option value="All">Všechny domy</option>
            <option value="House">Jeden dům</option>
          </select>
        </label>
        {scope === 'House' && (
          <label className="text-xs text-text-secondary">
            <span className="mb-1 block">Dům</span>
            <select value={houseId} onChange={(e) => setHouseId(e.target.value)} className={inputCls} required>
              <option value="">Vyberte…</option>
              {(houses ?? []).map((h) => <option key={h.id} value={h.id}>{h.name}</option>)}
            </select>
          </label>
        )}
        <label className="min-w-[16rem] flex-1 text-xs text-text-secondary">
          <span className="mb-1 block">Důvod</span>
          <input value={reason} onChange={(e) => setReason(e.target.value)} placeholder="roční závěrka 2026" className={`${inputCls} w-full`} required />
        </label>
        <button type="submit" disabled={busy} className={primaryBtn}>Uzavřít</button>
      </div>
      <p className="text-xs text-text-muted">Po uzavření už nejde měnit nic, co do toho dne patří. Zrušit jde jen poslední mezizávěrku.</p>
      <Errors items={errors} />
    </form>
  );
}

function ClosingDetail({ id, isAdmin, onDeleted }: { id: string; isAdmin: boolean; onDeleted: () => void }) {
  const { getAccessToken } = useAuth();
  const { data: closing, loading, error } = useApi<InterimClosing>(useCallback(() => getInterimClosing(id), [id]));
  const [reason, setReason] = useState('');
  const [errors, setErrors] = useState<string[]>([]);

  if (loading) return <Spinner />;
  if (error) return <div className="rounded-xl bg-danger-light p-4 text-sm text-danger">{error}</div>;
  if (!closing) return null;

  const remove = async () => {
    setErrors([]);
    try {
      await deleteInterimClosing(closing.id, reason);
      onDeleted();
    } catch (err) {
      setErrors(reasons(err));
    }
  };
  const exportPath = `/interim-closings/${encodeURIComponent(closing.id)}/export`;
  const changed = (closing.houses ?? []).some((h) => h.difference !== 0);

  return (
    <section className="space-y-3 rounded-2xl border border-border bg-surface-raised p-4 shadow-card" aria-label={`Mezizávěrka k ${formatIsoDay(closing.date)}`}>
      <div className="flex flex-wrap items-baseline justify-between gap-2">
        <div>
          <h2 className="text-lg font-semibold text-text-primary">Mezizávěrka k {formatIsoDay(closing.date)}</h2>
          <p className="text-xs text-text-muted">{closing.scope === 'All' ? 'všechny domy' : closing.houseName} · {closing.reason}</p>
        </div>
        <div className="flex gap-2">
          <button type="button" className={btn} onClick={() => void downloadLedgerExport(exportPath, { format: 'xlsx' }, getAccessToken)}>Export pro účetní (XLSX)</button>
          <button type="button" className={btn} onClick={() => void downloadLedgerExport(exportPath, { format: 'csv' }, getAccessToken)}>CSV</button>
        </div>
      </div>
      <p className={`text-sm ${changed ? 'font-medium text-danger' : 'text-success'}`}>
        {changed ? 'Saldo k datu mezizávěrky se od snímku změnilo — zkontrolujte rozdíly.' : 'Saldo k datu mezizávěrky odpovídá snímku.'}
      </p>
      <table className="w-full text-sm" aria-label="Snímek salda">
        <thead className="text-xs uppercase tracking-wider text-text-muted">
          <tr>
            <th className="py-1 text-left">Dům</th>
            <th className="py-1 text-right">Počáteční podíl</th>
            <th className="py-1 text-right">Platby</th>
            <th className="py-1 text-right">Náklady</th>
            <th className="py-1 text-right">Saldo (snímek)</th>
            <th className="py-1 text-right">Saldo dnes</th>
            <th className="py-1 text-right">Rozdíl</th>
          </tr>
        </thead>
        <tbody className="divide-y divide-border">
          {(closing.houses ?? []).map((h) => (
            <tr key={h.houseId}>
              <td className="py-1">{h.houseName}</td>
              <td className="py-1 text-right">{kc(h.opening)}</td>
              <td className="py-1 text-right">{kc(h.payments)}</td>
              <td className="py-1 text-right">{kc(h.costs)}</td>
              <td className="py-1 text-right font-medium">{kc(h.saldo)}</td>
              <td className="py-1 text-right">{kc(h.currentSaldo)}</td>
              <td className={`py-1 text-right ${h.difference !== 0 ? 'bg-danger-light font-semibold text-danger' : 'text-text-muted'}`}>{kc(h.difference)}</td>
            </tr>
          ))}
        </tbody>
      </table>
      {isAdmin && closing.canDelete && (
        <div className="flex flex-wrap items-end gap-2 border-t border-border pt-3">
          <label className="text-xs text-text-secondary">
            <span className="mb-1 block">Důvod zrušení</span>
            <input value={reason} onChange={(e) => setReason(e.target.value)} className={inputCls} />
          </label>
          <button type="button" onClick={() => void remove()} disabled={!reason.trim()} className="rounded-xl border border-danger px-3 py-1.5 text-sm font-medium text-danger hover:bg-danger-light disabled:opacity-50">
            Zrušit mezizávěrku
          </button>
        </div>
      )}
      <Errors items={errors} />
    </section>
  );
}
