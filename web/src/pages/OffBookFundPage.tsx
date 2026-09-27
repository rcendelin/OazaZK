import { useCallback, useState } from 'react';
import type { FormEvent } from 'react';
import { AlertTriangle } from 'lucide-react';
import { useAuth } from '../auth/AuthContext';
import { useApi } from '../hooks/useApi';
import { Spinner } from '../components/Spinner';
import { ApiError } from '../api/client';
import { getHouses } from '../api/houses';
import { addFundRecord, createOffBookFund, getOffBookFund, getOffBookFunds } from '../api/offBookFunds';
import type { FundRecordKind, OffBookFund, OffBookFundDetail } from '../api/offBookFunds';
import type { House } from '../types';
import { formatIsoDay, todayIso } from '../utils/date';
import { parseCzechNumber } from '../utils/number';

const inputCls = 'border border-border rounded-lg px-2 py-1.5 text-sm bg-surface-raised focus:border-accent focus:ring-2 focus:ring-accent/20';
const primaryBtn = 'rounded-xl bg-accent px-3 py-1.5 text-sm font-medium text-white hover:bg-accent-hover disabled:opacity-50';
const kc = (v: number) => `${v.toLocaleString('cs-CZ', { minimumFractionDigits: 2, maximumFractionDigits: 2 })} Kč`;
const kindLabels: Record<FundRecordKind, string> = { Call: 'Výzva', Contribution: 'Příspěvek', Expense: 'Výdaj', Settlement: 'Vyrovnání' };

function reasons(err: unknown): string[] {
  if (err instanceof ApiError && err.details.length > 0) return err.details;
  return [err instanceof Error ? err.message : 'Nastala neočekávaná chyba'];
}

/** The permanent warning on every screen of the module (T10). */
function OffBookWarning() {
  return (
    <div role="note" className="flex items-start gap-2 rounded-xl border border-warning bg-warning-light p-3 text-sm font-medium text-warning">
      <AlertTriangle size={18} className="mt-0.5 shrink-0" />
      Fond mimo účetnictví spolku – peníze nejsou na účtu spolku.
    </div>
  );
}

/** Off-book fund (T10): calls for contributions, who paid and who did not, expenses and reimbursements. */
export function OffBookFundPage() {
  const { user } = useAuth();
  const isAdmin = user?.role === 'Admin';
  const { data: funds, loading, error, refetch } = useApi<OffBookFund[]>(useCallback(() => getOffBookFunds(), []));
  const [selected, setSelected] = useState<string | null>(null);
  const fundId = selected ?? funds?.[0]?.id ?? null;

  return (
    <div className="space-y-6">
      <div className="space-y-2">
        <h1 className="text-2xl font-bold text-text-primary">Oddělený fond</h1>
        <OffBookWarning />
        <p className="max-w-3xl text-sm text-text-muted">
          Evidence neformálního fondu (např. na ohňostroje). Nic z něj se nepromítá do salda domů, mezizávěrek, pokladny ani
          hospodaření spolku.
        </p>
      </div>

      {error && <div className="rounded-xl bg-danger-light p-4 text-sm text-danger">{error}</div>}
      {loading ? <Spinner /> : (
        <div className="flex flex-wrap gap-2">
          {(funds ?? []).map((f) => (
            <button key={f.id} type="button" onClick={() => setSelected(f.id)}
              className={`rounded-full px-3 py-1.5 text-sm ${f.id === fundId ? 'bg-accent text-white' : 'bg-surface-sunken text-text-secondary'}`}>
              {f.name}
            </button>
          ))}
          {funds && funds.length === 0 && <p className="text-sm text-text-muted">Zatím žádný fond.</p>}
        </div>
      )}

      {isAdmin && <CreateFund onCreated={(f) => { setSelected(f.id); refetch(); }} />}
      {fundId && <FundDetail key={fundId} fundId={fundId} isAdmin={isAdmin} />}
    </div>
  );
}

function CreateFund({ onCreated }: { onCreated: (f: OffBookFund) => void }) {
  const [name, setName] = useState('');
  const [manager, setManager] = useState('');
  const [account, setAccount] = useState('soukromý účet správce');
  const [errors, setErrors] = useState<string[]>([]);

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    setErrors([]);
    try {
      onCreated(await createOffBookFund({ name, managerName: manager, accountDescription: account || undefined }));
      setName('');
    } catch (err) {
      setErrors(reasons(err));
    }
  };

  return (
    <details className="rounded-2xl border border-border bg-surface-raised p-4 shadow-card">
      <summary className="cursor-pointer text-sm font-medium text-accent">Nový fond</summary>
      <form onSubmit={(e) => void submit(e)} className="mt-3 flex flex-wrap items-end gap-2" aria-label="Nový fond">
        <label className="text-xs text-text-secondary"><span className="mb-1 block">Název</span>
          <input value={name} onChange={(e) => setName(e.target.value)} placeholder="Fond na ohňostroje" className={inputCls} required /></label>
        <label className="text-xs text-text-secondary"><span className="mb-1 block">Správce</span>
          <input value={manager} onChange={(e) => setManager(e.target.value)} className={inputCls} required /></label>
        <label className="text-xs text-text-secondary"><span className="mb-1 block">Kde jsou peníze (bez čísla účtu)</span>
          <input value={account} onChange={(e) => setAccount(e.target.value)} className={inputCls} /></label>
        <button type="submit" className={primaryBtn}>Založit</button>
      </form>
      {errors.length > 0 && <ul role="alert" className="mt-2 text-sm text-danger">{errors.map((m) => <li key={m}>{m}</li>)}</ul>}
    </details>
  );
}

function FundDetail({ fundId, isAdmin }: { fundId: string; isAdmin: boolean }) {
  const { data, loading, error, refetch } = useApi<OffBookFundDetail>(useCallback(() => getOffBookFund(fundId), [fundId]));
  const { data: houses } = useApi<House[]>(useCallback(() => getHouses(), []));
  if (loading && !data) return <Spinner />;
  if (error) return <div className="rounded-xl bg-danger-light p-4 text-sm text-danger">{error}</div>;
  if (!data) return null;

  return (
    <section className="space-y-4" aria-label={`Fond ${data.fund.name}`}>
      <dl className="grid grid-cols-2 gap-x-6 gap-y-1 text-sm sm:grid-cols-4">
        <div><dt className="text-xs text-text-muted">Zůstatek fondu</dt><dd className="text-lg font-semibold">{kc(data.fund.balance)}</dd></div>
        <div><dt className="text-xs text-text-muted">Dluží se těm, kdo platili předem</dt><dd>{kc(data.fund.outstanding)}</dd></div>
        <div><dt className="text-xs text-text-muted">Správce</dt><dd>{data.fund.managerName}</dd></div>
        <div><dt className="text-xs text-text-muted">Peníze</dt><dd>{data.fund.accountDescription ?? '—'}</dd></div>
      </dl>

      {data.calls.map((call) => (
        <div key={call.id} className="rounded-2xl border border-border bg-surface-raised p-4 shadow-card" aria-label={`Výzva ${call.text || formatIsoDay(call.date)}`}>
          <p className="font-medium">
            Výzva {call.text && `„${call.text}“`} · {kc(call.amountPerHouse)} za dům{call.dueDate && ` do ${formatIsoDay(call.dueDate)}`}
          </p>
          <p className="text-sm text-text-secondary">
            Vybráno {kc(call.collected)} · {call.debtors === 0 ? 'všichni zaplatili' : `nezaplatil${call.debtors === 1 ? '' : 'o'} ${call.debtors} ${call.debtors === 1 ? 'dům' : 'domů'}`}
          </p>
          <ul className="mt-2 flex flex-wrap gap-2 text-xs">
            {call.houses.map((h) => (
              <li key={h.houseId} className={`rounded-full px-2 py-0.5 ${h.isPaid ? 'bg-success-light text-success' : 'bg-danger-light text-danger'}`}>
                {h.houseName}: {h.isPaid ? 'zaplaceno' : `chybí ${kc(h.expected - h.paid)}`}
              </li>
            ))}
          </ul>
        </div>
      ))}

      <table className="w-full text-sm" aria-label="Pohyby fondu">
        <thead className="text-xs uppercase tracking-wider text-text-muted">
          <tr><th className="py-1 text-left">Datum</th><th className="py-1 text-left">Druh</th><th className="py-1 text-left">Popis</th><th className="py-1 text-right">Částka</th></tr>
        </thead>
        <tbody className="divide-y divide-border">
          {data.records.map((r) => (
            <tr key={r.id}>
              <td className="py-1">{formatIsoDay(r.date)}</td>
              <td className="py-1">{kindLabels[r.kind]}</td>
              <td className="py-1">
                {r.houseName ?? r.text}
                {r.paidBy && ` · zaplatil předem ${r.paidBy}`}
                {r.paidTo && ` · vráceno ${r.paidTo}`}
                {r.kind === 'Expense' && !r.hasReceipt && ' · bez dokladu'}
              </td>
              <td className={`py-1 text-right ${r.kind === 'Contribution' ? 'text-success' : 'text-danger'}`}>{kc(r.amount)}</td>
            </tr>
          ))}
        </tbody>
      </table>

      {isAdmin && <RecordForm fund={data} houses={houses ?? []} onSaved={refetch} />}
    </section>
  );
}

function RecordForm({ fund, houses, onSaved }: { fund: OffBookFundDetail; houses: House[]; onSaved: () => void }) {
  const [kind, setKind] = useState<FundRecordKind>('Contribution');
  const [date, setDate] = useState(todayIso());
  const [amount, setAmount] = useState('');
  const [text, setText] = useState('');
  const [houseId, setHouseId] = useState('');
  const [callId, setCallId] = useState('');
  const [paidBy, setPaidBy] = useState('');
  const [expenseId, setExpenseId] = useState('');
  const [errors, setErrors] = useState<string[]>([]);
  const active = houses.filter((h) => h.isActive);

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    setErrors([]);
    try {
      await addFundRecord(fund.fund.id, {
        kind, date, amount: parseCzechNumber(amount), text: text || undefined,
        houseIds: kind === 'Call' ? active.map((h) => h.id) : undefined,
        houseId: kind === 'Contribution' ? houseId : undefined,
        callId: kind === 'Contribution' && callId ? callId : undefined,
        paidBy: kind === 'Expense' && paidBy ? paidBy : undefined,
        hasReceipt: kind === 'Expense' ? true : undefined,
        expenseId: kind === 'Settlement' ? expenseId : undefined,
      });
      setAmount('');
      setText('');
      onSaved();
    } catch (err) {
      setErrors(reasons(err));
    }
  };

  return (
    <form onSubmit={(e) => void submit(e)} className="space-y-2 rounded-2xl border border-border bg-surface-raised p-4 shadow-card" aria-label="Nový pohyb fondu">
      <div className="flex flex-wrap items-end gap-2">
        <label className="text-xs text-text-secondary"><span className="mb-1 block">Druh</span>
          <select value={kind} onChange={(e) => setKind(e.target.value as FundRecordKind)} className={inputCls}>
            {(Object.keys(kindLabels) as FundRecordKind[]).map((k) => <option key={k} value={k}>{kindLabels[k]}</option>)}
          </select></label>
        <label className="text-xs text-text-secondary"><span className="mb-1 block">Datum</span>
          <input type="date" value={date} onChange={(e) => setDate(e.target.value)} className={inputCls} /></label>
        <label className="text-xs text-text-secondary"><span className="mb-1 block">{kind === 'Call' ? 'Částka za dům (Kč)' : 'Částka (Kč)'}</span>
          <input value={amount} onChange={(e) => setAmount(e.target.value)} inputMode="decimal" className={`${inputCls} w-24 text-right`} required /></label>
        {kind === 'Contribution' && (
          <>
            <label className="text-xs text-text-secondary"><span className="mb-1 block">Dům</span>
              <select value={houseId} onChange={(e) => setHouseId(e.target.value)} className={inputCls} required>
                <option value="">Vyberte…</option>
                {active.map((h) => <option key={h.id} value={h.id}>{h.name}</option>)}
              </select></label>
            <label className="text-xs text-text-secondary"><span className="mb-1 block">Na výzvu</span>
              <select value={callId} onChange={(e) => setCallId(e.target.value)} className={inputCls}>
                <option value="">—</option>
                {fund.calls.map((c) => <option key={c.id} value={c.id}>{c.text || formatIsoDay(c.date)}</option>)}
              </select></label>
          </>
        )}
        {kind === 'Expense' && (
          <label className="text-xs text-text-secondary"><span className="mb-1 block">Zaplatil předem (prázdné = z fondu)</span>
            <input value={paidBy} onChange={(e) => setPaidBy(e.target.value)} className={inputCls} /></label>
        )}
        {kind === 'Settlement' && (
          <label className="text-xs text-text-secondary"><span className="mb-1 block">Výdaj</span>
            <select value={expenseId} onChange={(e) => setExpenseId(e.target.value)} className={inputCls} required>
              <option value="">Vyberte…</option>
              {fund.records.filter((r) => r.kind === 'Expense' && r.paidBy).map((r) => <option key={r.id} value={r.id}>{r.text} ({r.paidBy})</option>)}
            </select></label>
        )}
        {kind !== 'Contribution' && kind !== 'Settlement' && (
          <label className="min-w-[12rem] flex-1 text-xs text-text-secondary"><span className="mb-1 block">{kind === 'Call' ? 'Text výzvy (všem aktivním domům)' : 'Za co'}</span>
            <input value={text} onChange={(e) => setText(e.target.value)} className={`${inputCls} w-full`} /></label>
        )}
        <button type="submit" className={primaryBtn}>Zapsat</button>
      </div>
      {errors.length > 0 && <ul role="alert" className="text-sm text-danger">{errors.map((m) => <li key={m}>{m}</li>)}</ul>}
    </form>
  );
}
