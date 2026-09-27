import { useCallback, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import { useApi } from '../hooks/useApi';
import { useAuth } from '../auth/AuthContext';
import {
  getAllAdvances,
  createAdvance,
  createDoplatek,
  createPayout,
  deletePayment,
} from '../api/advances';
import { calculateAdvances } from '../api/advanceSettings';
import { getHouses } from '../api/houses';
import { Spinner } from '../components/Spinner';
import { ConfirmDialog } from '../components/ConfirmDialog';
import { HelpNote } from '../components/help/HelpNote';
import { HelpDisclosure } from '../components/help/HelpDisclosure';
import type { AdvancePayment, House, PaymentType } from '../types';
import type { AdvanceCalculation } from '../api/advanceSettings';
import { parseCzechNumber } from '../utils/number';
import { todayIso } from '../utils/date';

const fmt = (v: number | null | undefined) => {
  const n = typeof v === 'number' && !isNaN(v) ? v : 0;
  return new Intl.NumberFormat('cs-CZ', { minimumFractionDigits: 0, maximumFractionDigits: 0 }).format(n);
};
const fmtDate = (s: string | null | undefined) => {
  if (!s || s.startsWith('0001')) return '—';
  try { return new Intl.DateTimeFormat('cs-CZ').format(new Date(s)); } catch { return '—'; }
};

// OpeningBalance stays only to label records made before opening balances moved to Správa → Počáteční stavy.
const typeLabel: Record<PaymentType, string> = {
  Advance: 'Záloha',
  Doplatek: 'Doplatek',
  Payout: 'Výplata',
  OpeningBalance: 'Počáteční stav',
};
const typeChipCls: Record<PaymentType, string> = {
  Advance: 'bg-accent-light text-accent',
  Doplatek: 'bg-warning-light text-warning',
  Payout: 'bg-danger-light text-danger',
  OpeningBalance: 'bg-surface-sunken text-text-secondary',
};

/**
 * Platby: recording payments (advance / doplatek / payout) and the list of recorded payments.
 * The house saldo itself lives in the ledger (/saldo-domu).
 */
export function SaldoPage() {
  const { user } = useAuth();
  const isAdmin = user?.role === 'Admin';

  const { data: payments, loading, error, refetch: refetchPayments } = useApi<AdvancePayment[]>(
    useCallback(() => getAllAdvances(), []),
  );
  const { data: houses } = useApi<House[]>(useCallback(() => getHouses(), []));
  const { data: plan } = useApi<AdvanceCalculation | null>(
    useCallback(() => (isAdmin ? calculateAdvances() : Promise.resolve(null)), [isAdmin]),
  );

  const [msg, setMsg] = useState<{ type: 'ok' | 'err'; text: string } | null>(null);
  const [confirmDelete, setConfirmDelete] = useState<AdvancePayment | null>(null);

  if (loading) return <div className="flex justify-center p-12"><Spinner size="lg" /></div>;

  const activeHouses = houses?.filter((h) => h.isActive) ?? [];

  return (
    <div className="space-y-6">
      <div>
        <div className="flex flex-wrap items-center justify-between gap-3">
          <h1 className="text-2xl font-bold text-text-primary">Platby</h1>
          {isAdmin && (
            <Link to="/advances/import" className="rounded-xl bg-accent px-4 py-2 text-sm font-medium text-white hover:bg-accent-hover">
              Import z banky
            </Link>
          )}
        </div>
        <p className="mt-1 text-sm text-text-secondary">
          Zaznamenané zálohy, doplatky a výplaty přeplatků. Saldo domu najdete v části{' '}
          <Link to="/saldo-domu" className="text-accent hover:underline">Saldo domu</Link>.
        </p>
      </div>

      {msg && (
        <div className={`rounded-xl border p-3 ${msg.type === 'ok' ? 'border-success/20 bg-success-light' : 'border-danger/20 bg-danger-light'}`}>
          <p className={`text-sm ${msg.type === 'ok' ? 'text-success' : 'text-danger'}`}>{msg.text}</p>
        </div>
      )}
      {error && <div className="rounded-xl bg-danger-light p-4 text-sm text-danger">{error}</div>}

      {isAdmin && (
        <PaymentForm
          houses={activeHouses}
          plan={plan ?? null}
          onSaved={(text) => {
            setMsg({ type: 'ok', text });
            refetchPayments();
          }}
          onError={(text) => setMsg({ type: 'err', text })}
        />
      )}

      <PaymentsList
        payments={payments ?? []}
        onDelete={isAdmin ? (p) => setConfirmDelete(p) : undefined}
      />

      <ConfirmDialog
        isOpen={confirmDelete !== null}
        title="Smazat záznam?"
        message={
          confirmDelete
            ? `Opravdu smazat ${typeLabel[confirmDelete.type]} domu ${confirmDelete.houseName ?? ''} (${fmt(confirmDelete.amount)} Kč) z ${fmtDate(confirmDelete.paymentDate)}?`
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
            setMsg({ type: 'ok', text: 'Záznam smazán.' });
            refetchPayments();
          } catch (err) {
            setMsg({ type: 'err', text: err instanceof Error ? err.message : 'Smazání selhalo.' });
          }
        }}
      />
    </div>
  );
}

// ───────────────────── Payment form ─────────────────────

type Kind = 'advance' | 'doplatek' | 'payout';

function PaymentForm({
  houses,
  plan,
  onSaved,
  onError,
}: {
  houses: House[];
  plan: AdvanceCalculation | null;
  onSaved: (text: string) => void;
  onError: (text: string) => void;
}) {
  const now = new Date();
  const [kind, setKind] = useState<Kind>('advance');
  const [houseId, setHouseId] = useState('');
  const [year, setYear] = useState(now.getFullYear());
  const [month, setMonth] = useState(now.getMonth() + 1);
  const [date, setDate] = useState(todayIso());
  const [water, setWater] = useState('');
  const [elec, setElec] = useState('');
  const [common, setCommon] = useState('');
  const [amount, setAmount] = useState('');
  const [note, setNote] = useState('');
  const savingRef = useRef(false);

  const num = parseCzechNumber;
  const componentTotal = num(water) + num(elec) + num(common);

  const prefillFromPlan = () => {
    const h = plan?.houses.find((x) => x.houseId === houseId);
    if (!h) { onError('Pro tento dům není předepsaná záloha.'); return; }
    setWater(String(h.actual.water));
    setElec(String(h.actual.electricity));
    setCommon(String(h.actual.common));
  };

  const reset = () => { setWater(''); setElec(''); setCommon(''); setAmount(''); setNote(''); };

  const submit = async () => {
    if (savingRef.current) return;
    if (!houseId) { onError('Vyberte domácnost.'); return; }
    savingRef.current = true;
    try {
      if (kind === 'advance' || kind === 'doplatek') {
        if (componentTotal <= 0) { onError('Zadejte alespoň jednu nenulovou částku.'); return; }
        const body = {
          houseId,
          waterAmount: num(water), electricityAmount: num(elec), commonAmount: num(common),
          paymentDate: new Date(date).toISOString(),
        };
        if (kind === 'advance') {
          await createAdvance({ ...body, year, month });
          onSaved(`Záloha za ${year}-${String(month).padStart(2, '0')} uložena.`);
        } else {
          await createDoplatek({ ...body, note: note || undefined });
          onSaved('Doplatek uložen.');
        }
      } else {
        if (num(amount) <= 0) { onError('Zadejte částku výplaty.'); return; }
        await createPayout({ houseId, amount: num(amount), paymentDate: new Date(date).toISOString(), note: note || undefined });
        onSaved('Výplata přeplatku uložena.');
      }
      reset();
    } catch (err) {
      onError(err instanceof Error ? err.message : 'Uložení selhalo.');
    } finally {
      savingRef.current = false;
    }
  };

  const inputCls = 'w-full border border-border rounded-xl px-3 py-2 text-sm bg-surface-raised focus:border-accent focus:ring-2 focus:ring-accent/20';
  const componentKind = kind === 'advance' || kind === 'doplatek';

  const tabs: { k: Kind; label: string }[] = [
    { k: 'advance', label: 'Měsíční záloha' },
    { k: 'doplatek', label: 'Doplatek' },
    { k: 'payout', label: 'Výplata přeplatku' },
  ];

  return (
    <div className="bg-surface-raised border border-border rounded-2xl p-6 shadow-card space-y-4">
      <h2 className="text-lg font-semibold">Zaznamenat platbu</h2>
      <HelpNote sectionId="paymentTypes" />
      <HelpDisclosure sectionId="paymentTypes" />
      <p className="text-xs text-text-muted">
        Počáteční stavy domů se zadávají v části Správa →{' '}
        <Link to="/admin/opening-balances" className="text-accent hover:underline">Počáteční stavy</Link>.
      </p>

      <div className="flex flex-wrap gap-2">
        {tabs.map((t) => (
          <button
            key={t.k}
            onClick={() => setKind(t.k)}
            className={`px-4 py-2 rounded-xl text-sm font-medium ${kind === t.k ? 'bg-accent text-white' : 'bg-surface-sunken text-text-secondary'}`}
          >
            {t.label}
          </button>
        ))}
      </div>

      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-3">
        <div className="lg:col-span-2">
          <label className="block text-sm font-medium text-text-secondary mb-1">Domácnost</label>
          <select value={houseId} onChange={(e) => setHouseId(e.target.value)} className={inputCls}>
            <option value="">— vyberte —</option>
            {houses.map((h) => <option key={h.id} value={h.id}>{h.name}</option>)}
          </select>
        </div>

        {kind === 'advance' && (
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
        )}

        <div className={kind === 'advance' ? 'lg:col-span-4' : 'lg:col-span-2'}>
          <label className="block text-sm font-medium text-text-secondary mb-1">Datum</label>
          <input type="date" value={date} onChange={(e) => setDate(e.target.value)} className={`${inputCls} max-w-xs`} />
        </div>
      </div>

      {componentKind ? (
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
      ) : (
        <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
          <div>
            <label className="block text-sm font-medium text-text-secondary mb-1">
              Vyplacená částka (Kč)
            </label>
            <input type="text" inputMode="decimal" value={amount} onChange={(e) => setAmount(e.target.value)} placeholder="0" className={inputCls} />
          </div>
        </div>
      )}

      {kind !== 'advance' && (
        <div>
          <label className="block text-sm font-medium text-text-secondary mb-1">Poznámka (volitelné)</label>
          <input type="text" value={note} onChange={(e) => setNote(e.target.value)} maxLength={500} className={inputCls} />
        </div>
      )}

      <div className="flex flex-wrap items-center gap-2">
        <button onClick={submit} className="bg-accent text-white px-4 py-2 rounded-xl hover:bg-accent-hover text-sm font-medium">
          Uložit{componentKind ? ` (${fmt(componentTotal)} Kč)` : ''}
        </button>
        {kind === 'advance' && (
          <button onClick={prefillFromPlan} disabled={!plan} className="bg-surface-sunken text-text-secondary px-3 py-2 rounded-xl hover:bg-surface-sunken text-sm disabled:opacity-50">
            Předvyplnit předepsanou zálohu
          </button>
        )}
      </div>
    </div>
  );
}

// ───────────────────── Payments list ─────────────────────

function PaymentsList({ payments, onDelete }: { payments: AdvancePayment[]; onDelete?: (p: AdvancePayment) => void }) {
  const sorted = [...payments].sort((a, b) => (a.paymentDate < b.paymentDate ? 1 : -1));
  const isComponent = (p: AdvancePayment) => p.type === 'Advance' || p.type === 'Doplatek';
  return (
    <div className="bg-surface-raised border border-border rounded-2xl overflow-hidden shadow-card">
      <div className="px-6 py-4 border-b border-border">
        <h2 className="text-lg font-semibold">Zaznamenané platby</h2>
      </div>
      {sorted.length === 0 ? (
        <p className="px-6 py-6 text-sm text-text-muted">Zatím nejsou zaznamenané žádné platby.</p>
      ) : (
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
                {onDelete && <th className="px-2 py-3"></th>}
              </tr>
            </thead>
            <tbody>
              {sorted.map((p) => (
                <tr key={`${p.houseId}-${p.rowKey}`} className="border-b border-border hover:bg-surface-sunken/50">
                  <td className="px-4 py-2.5 whitespace-nowrap">{fmtDate(p.paymentDate)}</td>
                  <td className="px-2 py-2.5">{p.houseName}</td>
                  <td className="px-2 py-2.5">
                    <span className={`text-xs px-1.5 py-0.5 rounded ${typeChipCls[p.type]}`}>{typeLabel[p.type]}</span>
                  </td>
                  <td className="px-2 py-2.5 text-right font-mono">{isComponent(p) ? fmt(p.waterAmount) : '—'}</td>
                  <td className="px-2 py-2.5 text-right font-mono">{isComponent(p) ? fmt(p.electricityAmount) : '—'}</td>
                  <td className="px-2 py-2.5 text-right font-mono">{isComponent(p) ? fmt(p.commonAmount) : '—'}</td>
                  <td className="px-2 py-2.5 text-right font-mono font-semibold">{fmt(p.amount)}</td>
                  <td className="px-2 py-2.5 text-text-muted max-w-[12rem] truncate">
                    {p.isFundTransfer && (
                      <span className="mr-1 rounded bg-accent/10 px-1.5 py-0.5 text-xs font-medium text-accent">
                        Z fondu
                      </span>
                    )}
                    {p.isFromBank && (
                      <span className="mr-1 rounded bg-success-light px-1.5 py-0.5 text-xs font-medium text-success">
                        Z banky
                      </span>
                    )}
                    {p.note}
                  </td>
                  {onDelete && (
                    <td className="px-2 py-2.5 text-right">
                      <button onClick={() => onDelete(p)} className="text-xs text-text-muted hover:text-danger">Smazat</button>
                    </td>
                  )}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
