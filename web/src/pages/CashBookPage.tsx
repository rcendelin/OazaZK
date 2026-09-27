import { useCallback, useState } from 'react';
import type { FormEvent } from 'react';
import { ReceiptText } from 'lucide-react';
import { useAuth } from '../auth/AuthContext';
import { useApi } from '../hooks/useApi';
import { Spinner } from '../components/Spinner';
import { ApiError } from '../api/client';
import { getCostComponents } from '../api/costComponents';
import type { CostComponent } from '../api/costComponents';
import { getDocuments } from '../api/documents';
import { downloadLedgerExport } from '../api/ledger';
import { createCashBookEntry, getCashBook, stornoCashBookEntry, typeLabels } from '../api/cashBook';
import type { CashBook, CashBookEntry } from '../api/cashBook';
import type { DocumentResponse } from '../types';
import { formatIsoDay, todayIso } from '../utils/date';
import { parseCzechNumber } from '../utils/number';

const inputCls = 'border border-border rounded-lg px-2 py-1.5 text-sm bg-surface-raised focus:border-accent focus:ring-2 focus:ring-accent/20';
const primaryBtn = 'rounded-xl bg-accent px-3 py-1.5 text-sm font-medium text-white hover:bg-accent-hover disabled:opacity-50';
const btn = 'rounded-lg border border-border px-2.5 py-1 text-xs font-medium text-text-secondary hover:bg-surface-sunken';
const kc = (v: number) => `${v.toLocaleString('cs-CZ', { minimumFractionDigits: 2, maximumFractionDigits: 2 })} Kč`;
const categories = ['výběr z účtu', 'údržba okolí', 'materiál', 'služby', 'akce spolku', 'jiné'];

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

/**
 * Cash book (T09, R10): every member sees all cash movements — also expenses without a receipt — so there is
 * no suspicion of a „black fund“. Admin and Accountant write; nothing is deleted, a mistake is reversed by a storno.
 */
export function CashBookPage() {
  const { user, getAccessToken } = useAuth();
  const canWrite = user?.role === 'Admin' || user?.role === 'Accountant';
  const [range, setRange] = useState<{ from?: string; to?: string }>({});
  const [draft, setDraft] = useState({ from: '', to: '' });
  const { data: book, loading, error, refetch } = useApi<CashBook>(useCallback(() => getCashBook(range), [range]), [range]);

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-bold text-text-primary">Pokladna</h1>
        <p className="mt-1 max-w-3xl text-sm text-text-muted">
          Hotovost spolku: vklady (výběry z účtu) a výdaje, i ty bez dokladu. Záznamy vidí všichni členové a nedají se
          smazat — omyl se opraví stornem, které zůstane vidět.
        </p>
      </div>

      <form className="flex flex-wrap items-end gap-2" onSubmit={(e) => { e.preventDefault(); setRange({ from: draft.from || undefined, to: draft.to || undefined }); }}>
        <label className="text-xs text-text-secondary">
          <span className="mb-1 block">Od</span>
          <input type="date" value={draft.from} onChange={(e) => setDraft({ ...draft, from: e.target.value })} className={inputCls} />
        </label>
        <label className="text-xs text-text-secondary">
          <span className="mb-1 block">Do</span>
          <input type="date" value={draft.to} onChange={(e) => setDraft({ ...draft, to: e.target.value })} className={inputCls} />
        </label>
        <button type="submit" className={primaryBtn}>Zobrazit</button>
        {canWrite && (
          <>
            <button type="button" className={btn} onClick={() => void downloadLedgerExport('/cash-book/export', { ...range, format: 'xlsx' }, getAccessToken)}>Export XLSX</button>
            <button type="button" className={btn} onClick={() => void downloadLedgerExport('/cash-book/export', { ...range, format: 'pdf' }, getAccessToken)}>Export PDF</button>
          </>
        )}
      </form>

      {canWrite && <EntryForm onSaved={refetch} />}

      {error && <div className="rounded-xl bg-danger-light p-4 text-sm text-danger">{error}</div>}
      {loading ? (
        <div className="flex justify-center p-8"><Spinner size="lg" /></div>
      ) : book && (
        <section className="space-y-2" aria-label="Pokladní kniha">
          <dl className="grid grid-cols-2 gap-x-6 gap-y-1 text-sm sm:grid-cols-4">
            <div><dt className="text-xs text-text-muted">Počáteční zůstatek</dt><dd>{kc(book.openingBalance)}</dd></div>
            <div><dt className="text-xs text-text-muted">Příjmy</dt><dd className="text-success">{kc(book.deposits)}</dd></div>
            <div><dt className="text-xs text-text-muted">Výdaje</dt><dd className="text-danger">{kc(book.expenses)}</dd></div>
            <div><dt className="text-xs text-text-muted">Zůstatek v pokladně</dt><dd className="text-lg font-semibold">{kc(book.closingBalance)}</dd></div>
          </dl>
          <div className="overflow-x-auto rounded-2xl border border-border bg-surface-raised shadow-card">
            <table className="w-full text-sm" aria-label="Záznamy pokladny">
              <thead className="bg-surface-sunken text-xs font-semibold uppercase tracking-wider text-text-muted">
                <tr>
                  <th className="px-3 py-2 text-left">Datum</th>
                  <th className="px-3 py-2 text-left">Co</th>
                  <th className="px-3 py-2 text-left">Komu / od koho</th>
                  <th className="px-3 py-2 text-right">Příjem</th>
                  <th className="px-3 py-2 text-right">Výdaj</th>
                  <th className="px-3 py-2 text-right">Zůstatek</th>
                  {canWrite && <th className="px-3 py-2" />}
                </tr>
              </thead>
              <tbody className="divide-y divide-border">
                {book.entries.map((e) => <EntryRow key={e.id} entry={e} canWrite={canWrite} onStorno={refetch} />)}
                {book.entries.length === 0 && (
                  <tr><td colSpan={canWrite ? 7 : 6} className="px-3 py-6 text-center text-text-muted">V období nejsou žádné záznamy.</td></tr>
                )}
              </tbody>
            </table>
          </div>
        </section>
      )}
    </div>
  );
}

function EntryRow({ entry: e, canWrite, onStorno }: { entry: CashBookEntry; canWrite: boolean; onStorno: () => void }) {
  const [open, setOpen] = useState(false);
  const [reason, setReason] = useState('');
  const [errors, setErrors] = useState<string[]>([]);
  const stornoed = e.correctedBy !== null;

  const storno = async () => {
    setErrors([]);
    try {
      await stornoCashBookEntry(e.id, reason);
      onStorno();
    } catch (err) {
      setErrors(reasons(err));
    }
  };

  return (
    <>
      <tr className={`align-top ${stornoed ? 'text-text-muted line-through' : ''}`}>
        <td className="whitespace-nowrap px-3 py-2">{formatIsoDay(e.date)}</td>
        <td className="px-3 py-2">
          <span className="text-xs text-text-muted">{typeLabels[e.type]} · {e.category}{e.componentName ? ` · náklad ${e.componentName}` : ''}</span>
          <p className="flex items-center gap-1">
            {e.description}
            {e.type === 'Expense' && !e.hasReceipt && (
              <span title="Výdaj bez dokladu" aria-label="bez dokladu" className="inline-flex items-center gap-0.5 rounded-full bg-warning-light px-1.5 text-xs text-warning">
                <ReceiptText size={12} /> bez dokladu
              </span>
            )}
          </p>
          {e.bankTransactionRef && <p className="text-xs text-text-muted">pohyb v bance: {e.bankTransactionRef}</p>}
        </td>
        <td className="px-3 py-2">{e.counterparty ?? '—'}</td>
        <td className="whitespace-nowrap px-3 py-2 text-right text-success">{e.effect > 0 ? kc(e.effect) : ''}</td>
        <td className="whitespace-nowrap px-3 py-2 text-right text-danger">{e.effect < 0 ? kc(-e.effect) : ''}</td>
        <td className="whitespace-nowrap px-3 py-2 text-right font-medium">{kc(e.balance)}</td>
        {canWrite && (
          <td className="whitespace-nowrap px-3 py-2 text-right">
            {e.type !== 'Correction' && !stornoed && (
              <button type="button" onClick={() => setOpen((v) => !v)} className="text-xs font-medium text-text-muted hover:text-danger">Storno</button>
            )}
          </td>
        )}
      </tr>
      {open && (
        <tr>
          <td colSpan={7} className="bg-surface-sunken/40 px-3 py-2">
            <div className="flex flex-wrap items-end gap-2">
              <label className="text-xs text-text-secondary">
                <span className="mb-1 block">Důvod storna</span>
                <input value={reason} onChange={(ev) => setReason(ev.target.value)} className={inputCls} />
              </label>
              <button type="button" onClick={() => void storno()} disabled={!reason.trim()} className="rounded-xl border border-danger px-3 py-1.5 text-sm font-medium text-danger hover:bg-danger-light disabled:opacity-50">
                Stornovat {kc(e.amount)}
              </button>
            </div>
            <Errors items={errors} />
          </td>
        </tr>
      )}
    </>
  );
}

function EntryForm({ onSaved }: { onSaved: () => void }) {
  const [type, setType] = useState<'Deposit' | 'Expense'>('Expense');
  const [date, setDate] = useState(todayIso());
  const [amount, setAmount] = useState('');
  const [category, setCategory] = useState('údržba okolí');
  const [description, setDescription] = useState('');
  const [counterparty, setCounterparty] = useState('');
  const [hasReceipt, setHasReceipt] = useState(true);
  const [documentId, setDocumentId] = useState('');
  const [bankRef, setBankRef] = useState('');
  const [componentId, setComponentId] = useState('');
  const [errors, setErrors] = useState<string[]>([]);
  const [busy, setBusy] = useState(false);
  const { data: components } = useApi<CostComponent[]>(useCallback(() => getCostComponents(), []));
  const { data: documents } = useApi<DocumentResponse[]>(useCallback(() => getDocuments(), []));

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    setBusy(true);
    setErrors([]);
    try {
      await createCashBookEntry({
        date, type, amount: parseCzechNumber(amount), category, description,
        counterparty: counterparty || undefined,
        hasReceipt: type === 'Deposit' ? true : hasReceipt,
        documentId: documentId || undefined,
        bankTransactionRef: type === 'Deposit' ? bankRef || undefined : undefined,
        componentId: type === 'Expense' ? componentId || undefined : undefined,
      });
      setAmount('');
      setDescription('');
      setCounterparty('');
      setBankRef('');
      onSaved();
    } catch (err) {
      setErrors(reasons(err));
    } finally {
      setBusy(false);
    }
  };

  return (
    <form onSubmit={(e) => void submit(e)} className="space-y-2 rounded-2xl border border-border bg-surface-raised p-4 shadow-card" aria-label="Nový záznam pokladny">
      <div className="flex flex-wrap items-end gap-2">
        <label className="text-xs text-text-secondary">
          <span className="mb-1 block">Druh</span>
          <select
            value={type}
            onChange={(e) => {
              const next = e.target.value as 'Deposit' | 'Expense';
              setType(next);
              setCategory(next === 'Deposit' ? 'výběr z účtu' : 'údržba okolí');
            }}
            className={inputCls}
          >
            <option value="Expense">Výdaj</option>
            <option value="Deposit">Vklad</option>
          </select>
        </label>
        <label className="text-xs text-text-secondary">
          <span className="mb-1 block">Datum</span>
          <input type="date" value={date} onChange={(e) => setDate(e.target.value)} className={inputCls} required />
        </label>
        <label className="text-xs text-text-secondary">
          <span className="mb-1 block">Částka (Kč)</span>
          <input value={amount} onChange={(e) => setAmount(e.target.value)} inputMode="decimal" className={`${inputCls} w-24 text-right`} required />
        </label>
        <label className="text-xs text-text-secondary">
          <span className="mb-1 block">Kategorie</span>
          <input value={category} onChange={(e) => setCategory(e.target.value)} list="cash-categories" className={inputCls} />
          <datalist id="cash-categories">{categories.map((c) => <option key={c} value={c} />)}</datalist>
        </label>
        <label className="min-w-[12rem] flex-1 text-xs text-text-secondary">
          <span className="mb-1 block">Co</span>
          <input value={description} onChange={(e) => setDescription(e.target.value)} className={`${inputCls} w-full`} required />
        </label>
        <label className="text-xs text-text-secondary">
          <span className="mb-1 block">{type === 'Deposit' ? 'Od koho' : 'Komu'}</span>
          <input value={counterparty} onChange={(e) => setCounterparty(e.target.value)} className={inputCls} />
        </label>
        {type === 'Expense' ? (
          <>
            <label className="flex items-center gap-1 pb-2 text-xs text-text-secondary">
              <input type="checkbox" checked={hasReceipt} onChange={(e) => setHasReceipt(e.target.checked)} />
              mám doklad
            </label>
            <label className="text-xs text-text-secondary">
              <span className="mb-1 block">Společný náklad složky</span>
              <select value={componentId} onChange={(e) => setComponentId(e.target.value)} className={inputCls}>
                <option value="">—</option>
                {(components ?? []).filter((c) => c.allocationBasis === 'CostEntries').map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
              </select>
            </label>
          </>
        ) : (
          <label className="text-xs text-text-secondary">
            <span className="mb-1 block">Pohyb v bance</span>
            <input value={bankRef} onChange={(e) => setBankRef(e.target.value)} placeholder="ID pohybu / výpis" className={inputCls} />
          </label>
        )}
        <label className="text-xs text-text-secondary">
          <span className="mb-1 block">Doklad (PDF)</span>
          <select value={documentId} onChange={(e) => setDocumentId(e.target.value)} className={`${inputCls} max-w-[12rem]`}>
            <option value="">—</option>
            {(documents ?? []).map((d) => <option key={d.id} value={d.id}>{d.name}</option>)}
          </select>
        </label>
        <button type="submit" disabled={busy} className={primaryBtn}>Zapsat</button>
      </div>
      {type === 'Expense' && !hasReceipt && <p className="text-xs text-warning">Výdaj bez dokladu: vyplňte, co se zaplatilo a komu.</p>}
      <Errors items={errors} />
    </form>
  );
}
