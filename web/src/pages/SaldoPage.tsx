import { useCallback, useRef, useState } from 'react';
import { useApi } from '../hooks/useApi';
import { useAuth } from '../auth/AuthContext';
import {
  getSaldo,
  getAllAdvances,
  createAdvance,
  createDoplatek,
  deletePayment,
} from '../api/advances';
import { calculateAdvances } from '../api/advanceSettings';
import { getHouses } from '../api/houses';
import { Spinner } from '../components/Spinner';
import { ConfirmDialog } from '../components/ConfirmDialog';
import type { HouseSaldo, AdvancePayment, House } from '../types';
import type { AdvanceCalculation } from '../api/advanceSettings';

const fmt = (v: number | null | undefined) => {
  const n = typeof v === 'number' && !isNaN(v) ? v : 0;
  return new Intl.NumberFormat('cs-CZ', { minimumFractionDigits: 0, maximumFractionDigits: 0 }).format(n);
};
const fmtDate = (s: string | null | undefined) => {
  if (!s || s.startsWith('0001')) return '—';
  try { return new Intl.DateTimeFormat('cs-CZ').format(new Date(s)); } catch { return '—'; }
};
const isoToday = () => new Date().toISOString().slice(0, 10);

/** Coloured saldo cell: positive = nedoplatek (red), negative = přeplatek (green). */
function SaldoValue({ value }: { value: number }) {
  if (Math.abs(value) < 0.5) return <span className="text-text-muted">0</span>;
  const owes = value > 0;
  return (
    <span className={owes ? 'text-danger font-semibold' : 'text-success font-semibold'}>
      {owes ? `+${fmt(value)}` : fmt(value)}
    </span>
  );
}

export function SaldoPage() {
  const { user } = useAuth();
  const isAdmin = user?.role === 'Admin';
  const isMember = user?.role === 'Member';
  const memberHouseId = user?.houseId ?? undefined;

  const { data: saldos, loading: saldoLoading, refetch: refetchSaldo } = useApi<HouseSaldo[]>(
    useCallback(() => getSaldo(isMember ? memberHouseId : undefined), [isMember, memberHouseId]),
  );
  const { data: payments, refetch: refetchPayments } = useApi<AdvancePayment[]>(
    useCallback(() => (isAdmin ? getAllAdvances() : Promise.resolve([])), [isAdmin]),
  );
  const { data: houses } = useApi<House[]>(useCallback(() => getHouses(), []));
  const { data: plan } = useApi<AdvanceCalculation>(
    useCallback(() => (isAdmin ? calculateAdvances() : Promise.resolve(null as unknown as AdvanceCalculation)), [isAdmin]),
  );

  const [expanded, setExpanded] = useState<string | null>(null);
  const [msg, setMsg] = useState<{ type: 'ok' | 'err'; text: string } | null>(null);
  const [confirmDelete, setConfirmDelete] = useState<AdvancePayment | null>(null);

  if (saldoLoading) return <div className="flex justify-center p-12"><Spinner size="lg" /></div>;

  const activeHouses = houses?.filter((h) => h.isActive) ?? [];

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-bold text-text-primary">Saldo a platby</h1>
        <p className="mt-1 text-sm text-text-secondary">
          Saldo každé domácnosti rozdělené na vodu, elektřinu vodárny a společný základ.
          Kladné saldo = nedoplatek, záporné = přeplatek.
        </p>
      </div>

      {msg && (
        <div className={`rounded-xl border p-3 ${msg.type === 'ok' ? 'border-success/20 bg-success-light' : 'border-danger/20 bg-danger-light'}`}>
          <p className={`text-sm ${msg.type === 'ok' ? 'text-success' : 'text-danger'}`}>{msg.text}</p>
        </div>
      )}

      {/* ═══ Saldo overview ═══ */}
      <SaldoTable
        saldos={saldos ?? []}
        expanded={expanded}
        onToggle={(id) => setExpanded(expanded === id ? null : id)}
      />

      {/* ═══ Admin: record a payment / doplatek ═══ */}
      {isAdmin && (
        <PaymentForm
          houses={activeHouses}
          plan={plan ?? null}
          saldos={saldos ?? []}
          onSaved={(text) => {
            setMsg({ type: 'ok', text });
            refetchSaldo();
            refetchPayments();
          }}
          onError={(text) => setMsg({ type: 'err', text })}
        />
      )}

      {/* ═══ Admin: recorded payments ═══ */}
      {isAdmin && payments && payments.length > 0 && (
        <PaymentsList payments={payments} onDelete={(p) => setConfirmDelete(p)} />
      )}

      <ConfirmDialog
        isOpen={confirmDelete !== null}
        title="Smazat platbu?"
        message={
          confirmDelete
            ? `Opravdu smazat ${confirmDelete.type === 'Doplatek' ? 'doplatek' : 'zálohu'} domu ${confirmDelete.houseName ?? ''} (${fmt(confirmDelete.amount)} Kč) z ${fmtDate(confirmDelete.paymentDate)}?`
            : ''
        }
        confirmLabel="Smazat"
        confirmVariant="danger"
        onCancel={() => setConfirmDelete(null)}
        onConfirm={async () => {
          const p = confirmDelete;
          setConfirmDelete(null);
          if (!p) return;
          try {
            await deletePayment(p.houseId, p.rowKey);
            setMsg({ type: 'ok', text: 'Platba smazána.' });
            refetchSaldo();
            refetchPayments();
          } catch (err) {
            setMsg({ type: 'err', text: err instanceof Error ? err.message : 'Smazání selhalo.' });
          }
        }}
      />
    </div>
  );
}

// ───────────────────── Saldo table ─────────────────────

function SaldoTable({
  saldos,
  expanded,
  onToggle,
}: {
  saldos: HouseSaldo[];
  expanded: string | null;
  onToggle: (houseId: string) => void;
}) {
  if (saldos.length === 0) {
    return (
      <div className="bg-surface-raised border border-border rounded-2xl p-6 shadow-card text-sm text-text-muted">
        Zatím nejsou žádná data pro výpočet salda.
      </div>
    );
  }

  const totals = saldos.reduce(
    (acc, s) => ({
      water: acc.water + s.water.saldo,
      elec: acc.elec + s.electricity.saldo,
      common: acc.common + s.common.saldo,
      total: acc.total + s.totalSaldo,
    }),
    { water: 0, elec: 0, common: 0, total: 0 },
  );

  return (
    <div className="bg-surface-raised border border-border rounded-2xl overflow-hidden shadow-card">
      <div className="px-6 py-4 border-b border-border">
        <h2 className="text-lg font-semibold">Saldo domácností</h2>
        <p className="text-xs text-text-muted mt-0.5">Klikněte na dům pro rozpad podle vyúčtovacích období.</p>
      </div>
      <div className="overflow-x-auto">
        <table className="w-full text-sm">
          <thead>
            <tr className="bg-surface-sunken border-b border-border text-xs text-text-muted uppercase">
              <th className="text-left px-4 py-3">Domácnost</th>
              <th className="text-right px-3 py-3 bg-accent-light border-l border-border">Voda saldo</th>
              <th className="text-right px-3 py-3 bg-warning-light border-l border-border">Elektřina saldo</th>
              <th className="text-right px-3 py-3 bg-surface-sunken border-l border-border">Společný saldo</th>
              <th className="text-right px-4 py-3 bg-success-light border-l border-border font-bold">Celkem saldo</th>
            </tr>
          </thead>
          <tbody>
            {saldos.map((s) => (
              <SaldoRow key={s.houseId} saldo={s} expanded={expanded === s.houseId} onToggle={() => onToggle(s.houseId)} />
            ))}
            <tr className="bg-surface-sunken font-semibold border-t-2">
              <td className="px-4 py-3">Celkem</td>
              <td className="px-3 py-3 text-right font-mono bg-accent-light/50 border-l border-border"><SaldoValue value={totals.water} /></td>
              <td className="px-3 py-3 text-right font-mono bg-warning-light/50 border-l border-border"><SaldoValue value={totals.elec} /></td>
              <td className="px-3 py-3 text-right font-mono bg-surface-sunken border-l border-border"><SaldoValue value={totals.common} /></td>
              <td className="px-4 py-3 text-right font-mono bg-success-light/50 border-l border-border"><SaldoValue value={totals.total} /></td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>
  );
}

function SaldoRow({ saldo, expanded, onToggle }: { saldo: HouseSaldo; expanded: boolean; onToggle: () => void }) {
  return (
    <>
      <tr className="border-b border-border hover:bg-surface-sunken/50 cursor-pointer" onClick={onToggle}>
        <td className="px-4 py-3 font-medium">
          <span className="text-text-muted mr-1">{expanded ? '▾' : '▸'}</span>
          {saldo.houseName}
        </td>
        <td className="px-3 py-3 text-right font-mono bg-accent-light/30 border-l border-border"><SaldoValue value={saldo.water.saldo} /></td>
        <td className="px-3 py-3 text-right font-mono bg-warning-light/30 border-l border-border"><SaldoValue value={saldo.electricity.saldo} /></td>
        <td className="px-3 py-3 text-right font-mono bg-surface-sunken border-l border-border"><SaldoValue value={saldo.common.saldo} /></td>
        <td className="px-4 py-3 text-right font-mono bg-success-light/30 border-l border-border"><SaldoValue value={saldo.totalSaldo} /></td>
      </tr>
      {expanded && (
        <tr className="bg-surface-sunken/40 border-b border-border">
          <td colSpan={5} className="px-4 py-3">
            <div className="text-xs text-text-secondary space-y-2">
              <div className="grid grid-cols-3 gap-3">
                {([
                  ['Voda', saldo.water, 'text-accent'],
                  ['Elektřina vodárna', saldo.electricity, 'text-warning'],
                  ['Společný základ', saldo.common, 'text-text-secondary'],
                ] as const).map(([label, c, cls]) => (
                  <div key={label} className="rounded-lg bg-surface-raised border border-border p-2">
                    <p className={`font-medium ${cls}`}>{label}</p>
                    <p className="mt-0.5">Předpis: <span className="font-mono">{fmt(c.charged)}</span> Kč</p>
                    <p>Zaplaceno: <span className="font-mono">{fmt(c.paid)}</span> Kč</p>
                    <p>Saldo: <SaldoValue value={c.saldo} /> Kč</p>
                  </div>
                ))}
              </div>
              {saldo.periods.length > 0 && (
                <table className="w-full mt-2">
                  <thead>
                    <tr className="text-[10px] text-text-muted uppercase">
                      <th className="text-left py-1">Období</th>
                      <th className="text-right py-1">Voda</th>
                      <th className="text-right py-1">Elektřina</th>
                      <th className="text-right py-1">Společný</th>
                    </tr>
                  </thead>
                  <tbody>
                    {saldo.periods.map((p) => (
                      <tr key={p.periodId} className="border-t border-border/60">
                        <td className="py-1">
                          {p.periodName}
                          {!p.closed && <span className="ml-1 text-[10px] text-accent">(otevřené)</span>}
                        </td>
                        <td className="py-1 text-right font-mono"><SaldoValue value={p.water.saldo} /></td>
                        <td className="py-1 text-right font-mono"><SaldoValue value={p.electricity.saldo} /></td>
                        <td className="py-1 text-right font-mono"><SaldoValue value={p.common.saldo} /></td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              )}
            </div>
          </td>
        </tr>
      )}
    </>
  );
}

// ───────────────────── Payment form ─────────────────────

function PaymentForm({
  houses,
  plan,
  saldos,
  onSaved,
  onError,
}: {
  houses: House[];
  plan: AdvanceCalculation | null;
  saldos: HouseSaldo[];
  onSaved: (text: string) => void;
  onError: (text: string) => void;
}) {
  const now = new Date();
  const [kind, setKind] = useState<'advance' | 'doplatek'>('advance');
  const [houseId, setHouseId] = useState('');
  const [year, setYear] = useState(now.getFullYear());
  const [month, setMonth] = useState(now.getMonth() + 1);
  const [date, setDate] = useState(isoToday());
  const [water, setWater] = useState('');
  const [elec, setElec] = useState('');
  const [common, setCommon] = useState('');
  const [note, setNote] = useState('');
  const savingRef = useRef(false);

  const num = (s: string) => parseFloat(s.replace(',', '.')) || 0;
  const total = num(water) + num(elec) + num(common);

  const prefillFromPlan = () => {
    const h = plan?.houses.find((x) => x.houseId === houseId);
    if (!h) { onError('Pro tento dům nejsou v plánu doporučené zálohy.'); return; }
    setWater(String(h.actual.water));
    setElec(String(h.actual.electricity));
    setCommon(String(h.actual.common));
  };

  const prefillFromSaldo = () => {
    const s = saldos.find((x) => x.houseId === houseId);
    if (!s) { onError('Pro tento dům není saldo.'); return; }
    // Only fill components that are owed (positive saldo).
    setWater(s.water.saldo > 0 ? String(Math.round(s.water.saldo)) : '0');
    setElec(s.electricity.saldo > 0 ? String(Math.round(s.electricity.saldo)) : '0');
    setCommon(s.common.saldo > 0 ? String(Math.round(s.common.saldo)) : '0');
  };

  const reset = () => { setWater(''); setElec(''); setCommon(''); setNote(''); };

  const submit = async () => {
    if (savingRef.current) return;
    if (!houseId) { onError('Vyberte domácnost.'); return; }
    if (total <= 0) { onError('Zadejte alespoň jednu nenulovou částku.'); return; }
    savingRef.current = true;
    try {
      if (kind === 'advance') {
        await createAdvance({
          houseId, year, month,
          waterAmount: num(water), electricityAmount: num(elec), commonAmount: num(common),
          paymentDate: new Date(date).toISOString(),
        });
        onSaved(`Záloha za ${year}-${String(month).padStart(2, '0')} uložena.`);
      } else {
        await createDoplatek({
          houseId,
          waterAmount: num(water), electricityAmount: num(elec), commonAmount: num(common),
          paymentDate: new Date(date).toISOString(),
          note: note || undefined,
        });
        onSaved('Doplatek uložen.');
      }
      reset();
    } catch (err) {
      onError(err instanceof Error ? err.message : 'Uložení selhalo.');
    } finally {
      savingRef.current = false;
    }
  };

  const inputCls = 'w-full border border-border rounded-xl px-3 py-2 text-sm bg-surface-raised focus:border-accent focus:ring-2 focus:ring-accent/20';

  return (
    <div className="bg-surface-raised border border-border rounded-2xl p-6 shadow-card space-y-4">
      <h2 className="text-lg font-semibold">Zaznamenat platbu</h2>

      <div className="flex gap-2">
        <button
          onClick={() => setKind('advance')}
          className={`px-4 py-2 rounded-xl text-sm font-medium ${kind === 'advance' ? 'bg-accent text-white' : 'bg-surface-sunken text-text-secondary'}`}
        >
          Měsíční záloha
        </button>
        <button
          onClick={() => setKind('doplatek')}
          className={`px-4 py-2 rounded-xl text-sm font-medium ${kind === 'doplatek' ? 'bg-accent text-white' : 'bg-surface-sunken text-text-secondary'}`}
        >
          Doplatek
        </button>
      </div>

      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-3">
        <div className="lg:col-span-2">
          <label className="block text-sm font-medium text-text-secondary mb-1">Domácnost</label>
          <select value={houseId} onChange={(e) => setHouseId(e.target.value)} className={inputCls}>
            <option value="">— vyberte —</option>
            {houses.map((h) => <option key={h.id} value={h.id}>{h.name}</option>)}
          </select>
        </div>

        {kind === 'advance' ? (
          <>
            <div>
              <label className="block text-sm font-medium text-text-secondary mb-1">Rok</label>
              <input type="number" value={year} onChange={(e) => setYear(parseInt(e.target.value) || year)} className={inputCls} />
            </div>
            <div>
              <label className="block text-sm font-medium text-text-secondary mb-1">Měsíc</label>
              <input type="number" min={1} max={12} value={month} onChange={(e) => setMonth(parseInt(e.target.value) || month)} className={inputCls} />
            </div>
          </>
        ) : (
          <div className="lg:col-span-2">
            <label className="block text-sm font-medium text-text-secondary mb-1">Datum platby</label>
            <input type="date" value={date} onChange={(e) => setDate(e.target.value)} className={inputCls} />
          </div>
        )}
      </div>

      {kind === 'advance' && (
        <div>
          <label className="block text-sm font-medium text-text-secondary mb-1">Datum platby</label>
          <input type="date" value={date} onChange={(e) => setDate(e.target.value)} className={`${inputCls} max-w-xs`} />
        </div>
      )}

      <div className="grid grid-cols-1 sm:grid-cols-3 gap-3">
        <div>
          <label className="block text-sm font-medium text-accent mb-1">Voda (Kč)</label>
          <input type="text" inputMode="decimal" value={water} onChange={(e) => setWater(e.target.value)} placeholder="0" className={inputCls} />
        </div>
        <div>
          <label className="block text-sm font-medium text-warning mb-1">Elektřina vodárna (Kč)</label>
          <input type="text" inputMode="decimal" value={elec} onChange={(e) => setElec(e.target.value)} placeholder="0" className={inputCls} />
        </div>
        <div>
          <label className="block text-sm font-medium text-text-secondary mb-1">Společný základ (Kč)</label>
          <input type="text" inputMode="decimal" value={common} onChange={(e) => setCommon(e.target.value)} placeholder="0" className={inputCls} />
        </div>
      </div>

      {kind === 'doplatek' && (
        <div>
          <label className="block text-sm font-medium text-text-secondary mb-1">Poznámka (volitelné)</label>
          <input type="text" value={note} onChange={(e) => setNote(e.target.value)} maxLength={500} className={inputCls} />
        </div>
      )}

      <div className="flex flex-wrap items-center gap-2">
        <button onClick={submit} className="bg-accent text-white px-4 py-2 rounded-xl hover:bg-accent-hover text-sm font-medium">
          Uložit ({fmt(total)} Kč)
        </button>
        {kind === 'advance' && (
          <button onClick={prefillFromPlan} className="bg-surface-sunken text-text-secondary px-3 py-2 rounded-xl hover:bg-surface-sunken text-sm">
            Předvyplnit dle plánu
          </button>
        )}
        {kind === 'doplatek' && (
          <button onClick={prefillFromSaldo} className="bg-surface-sunken text-text-secondary px-3 py-2 rounded-xl hover:bg-surface-sunken text-sm">
            Předvyplnit dle nedoplatku
          </button>
        )}
      </div>
    </div>
  );
}

// ───────────────────── Payments list ─────────────────────

function PaymentsList({ payments, onDelete }: { payments: AdvancePayment[]; onDelete: (p: AdvancePayment) => void }) {
  const sorted = [...payments].sort((a, b) => (a.paymentDate < b.paymentDate ? 1 : -1));
  return (
    <div className="bg-surface-raised border border-border rounded-2xl overflow-hidden shadow-card">
      <div className="px-6 py-4 border-b border-border">
        <h2 className="text-lg font-semibold">Zaznamenané platby</h2>
      </div>
      <div className="overflow-x-auto">
        <table className="w-full text-sm">
          <thead>
            <tr className="bg-surface-sunken border-b border-border text-xs text-text-muted uppercase">
              <th className="text-left px-4 py-3">Datum</th>
              <th className="text-left px-2 py-3">Domácnost</th>
              <th className="text-left px-2 py-3">Typ</th>
              <th className="text-right px-2 py-3">Voda</th>
              <th className="text-right px-2 py-3">Elektřina</th>
              <th className="text-right px-2 py-3">Společný</th>
              <th className="text-right px-2 py-3 font-bold">Celkem</th>
              <th className="text-left px-2 py-3">Poznámka</th>
              <th className="px-2 py-3"></th>
            </tr>
          </thead>
          <tbody>
            {sorted.map((p) => (
              <tr key={`${p.houseId}-${p.rowKey}`} className="border-b border-border hover:bg-surface-sunken/50">
                <td className="px-4 py-2.5 whitespace-nowrap">{fmtDate(p.paymentDate)}</td>
                <td className="px-2 py-2.5">{p.houseName}</td>
                <td className="px-2 py-2.5">
                  <span className={`text-xs px-1.5 py-0.5 rounded ${p.type === 'Doplatek' ? 'bg-warning-light text-warning' : 'bg-accent-light text-accent'}`}>
                    {p.type === 'Doplatek' ? 'Doplatek' : 'Záloha'}
                  </span>
                </td>
                <td className="px-2 py-2.5 text-right font-mono">{fmt(p.waterAmount)}</td>
                <td className="px-2 py-2.5 text-right font-mono">{fmt(p.electricityAmount)}</td>
                <td className="px-2 py-2.5 text-right font-mono">{fmt(p.commonAmount)}</td>
                <td className="px-2 py-2.5 text-right font-mono font-semibold">{fmt(p.amount)}</td>
                <td className="px-2 py-2.5 text-text-muted max-w-[12rem] truncate">{p.note}</td>
                <td className="px-2 py-2.5 text-right">
                  <button onClick={() => onDelete(p)} className="text-xs text-text-muted hover:text-danger">Smazat</button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}
