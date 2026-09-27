import { useCallback, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import type { FormEvent } from 'react';
import { useAuth } from '../auth/AuthContext';
import { useApi } from '../hooks/useApi';
import { Spinner } from '../components/Spinner';
import { ReasonConfirmDialog } from '../components/ReasonConfirmDialog';
import { ApiError } from '../api/client';
import { getCostComponents, methodLabels } from '../api/costComponents';
import type { CostComponent } from '../api/costComponents';
import { downloadDocument, getDocuments, getUnaccountedDocuments } from '../api/documents';
import type { UnaccountedDocument } from '../api/documents';
import {
  createCostEntry,
  createRecurringAdvances,
  deleteCostEntry,
  entryTypeLabels,
  getCostEntries,
  getCostEntryAllocation,
  paidFromLabels,
  periodicityLabels,
  updateCostEntry,
} from '../api/costEntries';
import type { AdvancePeriodicity, CostEntry, CostEntryAllocation, CostEntryType, PaidFrom } from '../api/costEntries';
import type { DocumentResponse } from '../types';
import { formatIsoDay, shiftIsoDate, todayIso } from '../utils/date';
import { invalidNumberMessage, parseCzechNumber } from '../utils/number';

const inputCls = 'border border-border rounded-lg px-2 py-1.5 text-sm bg-surface-raised focus:border-accent focus:ring-2 focus:ring-accent/20';
const primaryBtn = 'rounded-xl bg-accent px-3 py-1.5 text-sm font-medium text-white hover:bg-accent-hover disabled:opacity-50';
/** Editable form of a number: plain digits with a decimal comma. */
const inputNumber = (value: number) => String(value).replace('.', ',');
const kc = (v: number) => `${v.toLocaleString('cs-CZ', { minimumFractionDigits: 2, maximumFractionDigits: 2 })} Kč`;
const types: CostEntryType[] = ['Advance', 'Settlement', 'OneOff'];
const payments: PaidFrom[] = ['Bank', 'SupplierCredit', 'Cash', 'Other'];
const periodicities: AdvancePeriodicity[] = ['Monthly', 'Quarterly', 'HalfYearly', 'Yearly'];

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
 * Costs of components (T06): supplier advances, settlements and one-off costs with their allocation to houses.
 * Admin and Accountant read; only Admin adds, generates advances and deletes.
 */
export function CostsPage() {
  const { user } = useAuth();
  const isAdmin = user?.role === 'Admin';
  const { data: components, loading, error } = useApi<CostComponent[]>(useCallback(() => getCostComponents(), []));
  const [searchParams, setSearchParams] = useSearchParams();
  const selectedId = searchParams.get('component');
  const prefillDocument = searchParams.get('document') ?? undefined;
  const setSelectedId = (id: string) => setSearchParams({ component: id });
  const [version, setVersion] = useState(0);
  const component = components?.find((c) => c.id === selectedId) ?? components?.[0] ?? null;

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-bold text-text-primary">Náklady</h1>
        <p className="mt-1 max-w-3xl text-sm text-text-muted">
          Zálohy dodavatelům, vyúčtování a jednorázové náklady po složkách. Náklad se rozpočítá podle dní svého
          období mezi domy, které se na složce v tu dobu podílejí. Na tom, jak byl zaplacený (z účtu, z přeplatku
          u dodavatele, hotově), rozpočet nezávisí.
        </p>
      </div>

      {error && <div className="rounded-xl bg-danger-light p-4 text-sm text-danger">{error}</div>}
      {loading ? (
        <div className="flex justify-center p-8"><Spinner size="lg" /></div>
      ) : (
        <div className="flex flex-wrap gap-2" role="tablist" aria-label="Složky">
          {(components ?? []).map((c) => (
            <button
              key={c.id}
              type="button"
              role="tab"
              aria-selected={c.id === component?.id}
              onClick={() => setSelectedId(c.id)}
              className={`rounded-full px-3 py-1.5 text-sm ${c.id === component?.id ? 'bg-accent text-white' : 'bg-surface-sunken text-text-secondary hover:bg-surface-raised'}`}
            >
              {c.name}
            </button>
          ))}
          {components && components.length === 0 && <p className="text-sm text-text-muted">Nejdřív založte nákladové složky.</p>}
        </div>
      )}

      <UnaccountedDocuments
        key={version}
        onBook={(d) => setSearchParams(d.document.componentId
          ? { component: d.document.componentId, document: d.document.id }
          : { ...(component ? { component: component.id } : {}), document: d.document.id })}
      />

      {component && <ComponentCosts key={`${component.id}-${prefillDocument ?? ''}`} component={component} isAdmin={isAdmin} prefillDocument={prefillDocument}
        onChanged={() => { setVersion((v) => v + 1); if (prefillDocument) setSearchParams({ component: component.id }); }} />}
    </div>
  );
}

function ComponentCosts({ component, isAdmin, prefillDocument, onChanged }: {
  component: CostComponent;
  isAdmin: boolean;
  prefillDocument?: string;
  onChanged: () => void;
}) {
  const metered = component.allocationBasis === 'Metered';
  const [range, setRange] = useState({ from: shiftIsoDate(todayIso(), { months: -12 }), to: todayIso() });
  const [draft, setDraft] = useState(range);
  const { data: entries, loading, error, refetch } = useApi<CostEntry[]>(
    useCallback(() => getCostEntries(component.id, range.from, range.to), [component.id, range]), [component.id, range],
  );
  const { data: documents } = useApi<DocumentResponse[]>(useCallback(() => getDocuments(), []));
  const [openId, setOpenId] = useState<string | null>(null);
  const [rowErrors, setRowErrors] = useState<string[]>([]);
  const [editing, setEditing] = useState<CostEntry | null>(null);
  const [deleting, setDeleting] = useState<CostEntry | null>(null);
  const { getAccessToken } = useAuth();

  const total = (entries ?? []).reduce((sum, e) => sum + e.amount, 0);
  const quantity = (entries ?? []).reduce((sum, e) => sum + (e.quantityM3 ?? 0), 0);
  const documentName = (id: string | null) => documents?.find((d) => d.id === id)?.name ?? null;

  const remove = async (entry: CostEntry, reason: string | undefined) => {
    setDeleting(null);
    setRowErrors([]);
    try {
      await deleteCostEntry(component.id, entry.id, reason);
      if (editing?.id === entry.id) setEditing(null);
      refetch();
      onChanged();
    } catch (err) {
      setRowErrors(reasons(err));
    }
  };

  return (
    <section className="space-y-4" aria-label={`Náklady ${component.name}`}>
      <p className="text-sm text-text-secondary">
        {metered
          ? 'Složka se rozpočítává podle odečtů. Zde se zadávají faktury dodavatele s fakturovanými m³ — z nich se počítá cena za m³.'
          : `Metoda dnes: ${component.currentMethod ? methodLabels[component.currentMethod] : '—'}, domů: ${component.currentParticipants}.`}
      </p>

      <form className="flex flex-wrap items-end gap-2" onSubmit={(e) => { e.preventDefault(); setRange({ ...draft }); }}>
        <label className="text-xs text-text-secondary">
          <span className="mb-1 block">Od</span>
          <input type="date" value={draft.from} onChange={(e) => setDraft({ ...draft, from: e.target.value })} className={inputCls} />
        </label>
        <label className="text-xs text-text-secondary">
          <span className="mb-1 block">Do</span>
          <input type="date" value={draft.to} onChange={(e) => setDraft({ ...draft, to: e.target.value })} className={inputCls} />
        </label>
        <button type="submit" className={primaryBtn}>Zobrazit</button>
      </form>

      {error && <div className="rounded-xl bg-danger-light p-3 text-sm text-danger">{error}</div>}
      <Errors items={rowErrors} />
      {loading ? (
        <Spinner />
      ) : (
        <div className="overflow-x-auto rounded-2xl border border-border bg-surface-raised shadow-card">
          <table className="w-full text-sm" aria-label="Nákladové záznamy">
            <thead className="bg-surface-sunken text-xs font-semibold uppercase tracking-wider text-text-muted">
              <tr>
                <th className="px-3 py-2 text-left">Období</th>
                <th className="px-3 py-2 text-left">Druh</th>
                <th className="px-3 py-2 text-right">Částka</th>
                {metered && <th className="px-3 py-2 text-right">m³</th>}
                <th className="px-3 py-2 text-left">Dodavatel</th>
                <th className="px-3 py-2 text-left">Úhrada</th>
                <th className="px-3 py-2 text-left">Doklad</th>
                <th className="px-3 py-2" />
              </tr>
            </thead>
            <tbody className="divide-y divide-border">
              {(entries ?? []).map((e) => (
                <EntryRows
                  key={e.id}
                  entry={e}
                  metered={metered}
                  open={openId === e.id}
                  documentName={documentName(e.documentId)}
                  onToggle={() => setOpenId(openId === e.id ? null : e.id)}
                  onDownload={() => e.documentId && void downloadDocument(e.documentId, documentName(e.documentId) ?? 'doklad.pdf', getAccessToken)}
                  onEdit={isAdmin && !e.locked ? () => setEditing(e) : undefined}
                  onDelete={isAdmin && !e.locked ? () => setDeleting(e) : undefined}
                />
              ))}
              {entries && entries.length === 0 && (
                <tr><td colSpan={metered ? 8 : 7} className="px-3 py-6 text-center text-text-muted">V tomto období nejsou žádné náklady.</td></tr>
              )}
            </tbody>
            {entries && entries.length > 0 && (
              <tfoot className="border-t border-border font-semibold">
                <tr>
                  <td className="px-3 py-2" colSpan={2}>Celkem</td>
                  <td className="px-3 py-2 text-right">{kc(total)}</td>
                  {metered && <td className="px-3 py-2 text-right">{quantity.toLocaleString('cs-CZ')}</td>}
                  <td colSpan={4} className="px-3 py-2 text-xs font-normal text-text-muted">
                    {metered && quantity > 0 ? `průměrná cena ${kc(total / quantity)}/m³` : ''}
                  </td>
                </tr>
              </tfoot>
            )}
          </table>
        </div>
      )}

      {isAdmin && editing && (
        <EntryForm key={editing.id} componentId={component.id} metered={metered} documents={documents ?? []} entry={editing}
          onSaved={() => { setEditing(null); refetch(); onChanged(); }} onCancel={() => setEditing(null)} />
      )}

      {deleting && (
        <ReasonConfirmDialog
          title="Smazat náklad?"
          message={`Opravdu smazat záznam „${entryTypeLabels[deleting.type]}“ ${formatIsoDay(deleting.periodFrom)} – ${formatIsoDay(deleting.periodTo)} (${kc(deleting.amount)})?`}
          confirmLabel="Smazat"
          onConfirm={(reason) => void remove(deleting, reason)}
          onCancel={() => setDeleting(null)}
        />
      )}

      {isAdmin && (
        <div className="grid gap-4 lg:grid-cols-2">
          <EntryForm componentId={component.id} metered={metered} documents={documents ?? []} initialDocumentId={prefillDocument} onSaved={() => { refetch(); onChanged(); }} />
          {!metered && <RecurringForm componentId={component.id} onSaved={refetch} />}
        </div>
      )}
    </section>
  );
}

function EntryRows({ entry: e, metered, open, documentName, onToggle, onDownload, onEdit, onDelete }: {
  entry: CostEntry;
  metered: boolean;
  open: boolean;
  documentName: string | null;
  onToggle: () => void;
  onDownload: () => void;
  onEdit?: () => void;
  onDelete?: () => void;
}) {
  return (
    <>
      <tr className="align-top hover:bg-surface-sunken/50">
        <td className="whitespace-nowrap px-3 py-2">
          {formatIsoDay(e.periodFrom)} – {formatIsoDay(e.periodTo)}
          {e.postingDate && (
            <p className="text-xs text-warning" title="Období zasahuje do uzavřeného období — náklad se zaúčtoval jako oprava v prvním otevřeném dni.">
              oprava, zaúčtováno {formatIsoDay(e.postingDate)}
            </p>
          )}
          {e.note && <p className="max-w-[16rem] whitespace-normal text-xs text-text-muted">{e.note}</p>}
        </td>
        <td className="px-3 py-2">{entryTypeLabels[e.type]}</td>
        <td className="whitespace-nowrap px-3 py-2 text-right">{kc(e.amount)}</td>
        {metered && <td className="px-3 py-2 text-right">{e.quantityM3?.toLocaleString('cs-CZ') ?? '—'}</td>}
        <td className="px-3 py-2">{e.supplier ?? '—'}</td>
        <td className="px-3 py-2">{paidFromLabels[e.paidFrom]}</td>
        <td className="px-3 py-2">
          {e.documentId
            ? <button type="button" onClick={onDownload} className="text-xs font-medium text-accent hover:text-accent-hover">{documentName ?? 'PDF'}</button>
            : '—'}
        </td>
        <td className="whitespace-nowrap px-3 py-2 text-right">
          {!metered && (
            <button type="button" onClick={onToggle} className="text-xs font-medium text-accent hover:text-accent-hover">
              {open ? 'Skrýt rozpad' : 'Rozpad na domy'}
            </button>
          )}
          {onEdit && <button type="button" onClick={onEdit} className="ml-3 text-xs font-medium text-accent hover:text-accent-hover">Upravit</button>}
          {onDelete && <button type="button" onClick={onDelete} className="ml-3 text-xs font-medium text-text-muted hover:text-danger">Smazat</button>}
        </td>
      </tr>
      {open && (
        <tr>
          <td colSpan={metered ? 8 : 7} className="bg-surface-sunken/40 px-3 py-3">
            <Allocation entry={e} />
          </td>
        </tr>
      )}
    </>
  );
}

/** Drill-down: how the entry splits into segments and houses, so everyone can see why a house has its cost. */
function Allocation({ entry }: { entry: CostEntry }) {
  const { data, loading, error } = useApi<CostEntryAllocation>(
    useCallback(() => getCostEntryAllocation(entry.componentId, entry.id), [entry.componentId, entry.id]), [entry.componentId, entry.id],
  );
  if (loading) return <Spinner />;
  if (error) return <p className="text-sm text-danger">{error}</p>;
  if (!data) return null;

  return (
    <div className="space-y-3 text-sm" aria-label="Rozpad nákladu">
      <table className="text-sm" aria-label="Součty za domy">
        <tbody>
          {data.houseTotals.map((s) => (
            <tr key={s.houseId}>
              <td className="pr-6 font-medium">{s.houseName}</td>
              <td className="text-right">{kc(s.amount)}</td>
            </tr>
          ))}
        </tbody>
      </table>
      <details>
        <summary className="cursor-pointer text-xs font-medium text-accent">Výpočet po úsecích</summary>
        <ul className="mt-2 space-y-1 text-xs text-text-secondary">
          {data.segments.map((s) => (
            <li key={s.from}>
              {formatIsoDay(s.from)} – {formatIsoDay(s.to)} ({s.days} dní z {data.entry.days}) = {kc(s.amount)},{' '}
              {methodLabels[s.method].toLowerCase()} mezi {s.shares.length} dom{s.shares.length === 1 ? '' : 'y'}:{' '}
              {s.shares.map((x) => `${x.houseName} ${kc(x.amount)}`).join(', ')}
            </li>
          ))}
        </ul>
      </details>
    </div>
  );
}

/** „Přidat náklad“, or „Upravit náklad“ with `entry`. */
function EntryForm({ componentId, metered, documents, initialDocumentId, entry, onSaved, onCancel }: {
  componentId: string;
  metered: boolean;
  documents: DocumentResponse[];
  initialDocumentId?: string;
  entry?: CostEntry;
  onSaved: () => void;
  onCancel?: () => void;
}) {
  const [type, setType] = useState<CostEntryType>(entry?.type ?? (metered ? 'OneOff' : 'Settlement'));
  const [from, setFrom] = useState(entry?.periodFrom ?? '');
  const [to, setTo] = useState(entry?.periodTo ?? '');
  const [amount, setAmount] = useState(entry ? inputNumber(entry.amount) : '');
  const [quantity, setQuantity] = useState(entry?.quantityM3 != null ? inputNumber(entry.quantityM3) : '');
  const [supplier, setSupplier] = useState(entry?.supplier ?? '');
  const [paidFrom, setPaidFrom] = useState<PaidFrom>(entry?.paidFrom ?? 'Bank');
  const [documentId, setDocumentId] = useState(entry?.documentId ?? initialDocumentId ?? '');
  const [note, setNote] = useState(entry?.note ?? '');
  const [reason, setReason] = useState('');
  const [errors, setErrors] = useState<string[]>([]);
  const [info, setInfo] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const title = entry ? 'Upravit náklad' : metered ? 'Přidat fakturu' : 'Přidat náklad';

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    setErrors([]);
    setInfo(null);
    const parsedAmount = parseCzechNumber(amount);
    const parsedQuantity = metered ? parseCzechNumber(quantity) : undefined;
    if (parsedAmount === null || parsedQuantity === null) {
      setErrors([
        ...(parsedAmount === null ? [invalidNumberMessage('Částka (Kč)')] : []),
        ...(parsedQuantity === null ? [invalidNumberMessage('Množství (m³)')] : []),
      ]);
      return;
    }
    setBusy(true);
    try {
      const body = {
        type,
        periodFrom: from,
        periodTo: to || from,
        amount: parsedAmount,
        quantityM3: parsedQuantity,
        supplier: supplier || undefined,
        paidFrom,
        documentId: documentId || undefined,
        note: note || undefined,
        reason: reason.trim() || undefined,
      };
      const saved = entry ? await updateCostEntry(componentId, entry.id, body) : await createCostEntry(componentId, body);
      if (!entry) {
        setAmount('');
        setQuantity('');
        setNote('');
        setReason('');
        setInfo(saved.postingDate
          ? `Náklad uložen jako oprava — období zasahuje do uzavřeného období, zaúčtuje se k ${formatIsoDay(saved.postingDate)}.`
          : 'Náklad uložen.');
      }
      onSaved();
    } catch (err) {
      setErrors(reasons(err));
    } finally {
      setBusy(false);
    }
  };

  return (
    <form onSubmit={(e) => void submit(e)} className="space-y-2 rounded-2xl border border-border bg-surface-raised p-4 shadow-card" aria-label={entry ? 'Upravit náklad' : 'Přidat náklad'}>
      <h2 className="font-semibold text-text-primary">{title}</h2>
      <div className="flex flex-wrap items-end gap-2">
        {!metered && (
          <label className="text-xs text-text-secondary">
            <span className="mb-1 block">Druh</span>
            <select value={type} onChange={(e) => setType(e.target.value as CostEntryType)} className={inputCls}>
              {types.map((t) => <option key={t} value={t}>{entryTypeLabels[t]}</option>)}
            </select>
          </label>
        )}
        <label className="text-xs text-text-secondary">
          <span className="mb-1 block">Období od</span>
          <input type="date" value={from} onChange={(e) => setFrom(e.target.value)} className={inputCls} required />
        </label>
        <label className="text-xs text-text-secondary">
          <span className="mb-1 block">do</span>
          <input type="date" value={to} onChange={(e) => setTo(e.target.value)} className={inputCls} />
        </label>
        <label className="text-xs text-text-secondary">
          <span className="mb-1 block">Částka (Kč)</span>
          <input value={amount} onChange={(e) => setAmount(e.target.value)} inputMode="decimal" className={`${inputCls} w-28 text-right`} required />
        </label>
        {metered && (
          <label className="text-xs text-text-secondary">
            <span className="mb-1 block">Množství (m³)</span>
            <input value={quantity} onChange={(e) => setQuantity(e.target.value)} inputMode="decimal" className={`${inputCls} w-24 text-right`} required />
          </label>
        )}
        <label className="text-xs text-text-secondary">
          <span className="mb-1 block">Dodavatel</span>
          <input value={supplier} onChange={(e) => setSupplier(e.target.value)} className={`${inputCls} w-28`} />
        </label>
        <label className="text-xs text-text-secondary">
          <span className="mb-1 block">Úhrada</span>
          <select value={paidFrom} onChange={(e) => setPaidFrom(e.target.value as PaidFrom)} className={inputCls}>
            {payments.map((p) => <option key={p} value={p}>{paidFromLabels[p]}</option>)}
          </select>
        </label>
        <label className="text-xs text-text-secondary">
          <span className="mb-1 block">Doklad (PDF v Dokumentech)</span>
          <select value={documentId} onChange={(e) => setDocumentId(e.target.value)} className={`${inputCls} max-w-[14rem]`}>
            <option value="">—</option>
            {documents.map((d) => <option key={d.id} value={d.id}>{d.name}</option>)}
          </select>
        </label>
        <label className="text-xs text-text-secondary">
          <span className="mb-1 block">Poznámka</span>
          <input value={note} onChange={(e) => setNote(e.target.value)} className={inputCls} />
        </label>
        <label className="text-xs text-text-secondary">
          <span className="mb-1 block">Důvod (u opravy za mezizávěrkou)</span>
          <input value={reason} onChange={(e) => setReason(e.target.value)} placeholder="např. pozdě doručená faktura" className={inputCls} />
        </label>
        <button type="submit" disabled={busy} className={primaryBtn}>{entry ? 'Uložit změnu' : 'Přidat'}</button>
        {onCancel && <button type="button" onClick={onCancel} className="pb-1.5 text-xs font-medium text-text-muted hover:text-text-secondary">Zrušit</button>}
      </div>
      {!metered && type === 'Settlement' && (
        <p className="text-xs text-text-muted">Vyúčtování: doplatek zadejte kladně, přeplatek záporně.</p>
      )}
      <p className="text-xs text-text-muted">
        Zasahuje-li období do uzavřeného období (mezizávěrky), uloží se náklad jako oprava v prvním otevřeném dni — pak je důvod povinný.
      </p>
      {info && <p role="status" className="text-sm text-success">{info}</p>}
      <Errors items={errors} />
    </form>
  );
}

function RecurringForm({ componentId, onSaved }: { componentId: string; onSaved: () => void }) {
  const [amount, setAmount] = useState('');
  const [periodicity, setPeriodicity] = useState<AdvancePeriodicity>('Monthly');
  const [from, setFrom] = useState('');
  const [to, setTo] = useState('');
  const [supplier, setSupplier] = useState('');
  const [paidFrom, setPaidFrom] = useState<PaidFrom>('Bank');
  const [errors, setErrors] = useState<string[]>([]);
  const [info, setInfo] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    setErrors([]);
    setInfo(null);
    const parsedAmount = parseCzechNumber(amount);
    if (parsedAmount === null) { setErrors([invalidNumberMessage('Částka (Kč)')]); return; }
    setBusy(true);
    try {
      const created = await createRecurringAdvances(componentId, {
        amount: parsedAmount, periodicity, from, to, supplier: supplier || undefined, paidFrom,
      });
      setInfo(`Vytvořeno ${created.length} záloh.`);
      onSaved();
    } catch (err) {
      setErrors(reasons(err));
    } finally {
      setBusy(false);
    }
  };

  return (
    <form onSubmit={(e) => void submit(e)} className="space-y-2 rounded-2xl border border-border bg-surface-raised p-4 shadow-card" aria-label="Opakovaná záloha">
      <h2 className="font-semibold text-text-primary">Opakovaná záloha</h2>
      <div className="flex flex-wrap items-end gap-2">
        <label className="text-xs text-text-secondary">
          <span className="mb-1 block">Částka (Kč)</span>
          <input value={amount} onChange={(e) => setAmount(e.target.value)} inputMode="decimal" className={`${inputCls} w-24 text-right`} required />
        </label>
        <label className="text-xs text-text-secondary">
          <span className="mb-1 block">Perioda</span>
          <select value={periodicity} onChange={(e) => setPeriodicity(e.target.value as AdvancePeriodicity)} className={inputCls}>
            {periodicities.map((p) => <option key={p} value={p}>{periodicityLabels[p]}</option>)}
          </select>
        </label>
        <label className="text-xs text-text-secondary">
          <span className="mb-1 block">Od</span>
          <input type="date" value={from} onChange={(e) => setFrom(e.target.value)} className={inputCls} required />
        </label>
        <label className="text-xs text-text-secondary">
          <span className="mb-1 block">Do</span>
          <input type="date" value={to} onChange={(e) => setTo(e.target.value)} className={inputCls} required />
        </label>
        <label className="text-xs text-text-secondary">
          <span className="mb-1 block">Dodavatel</span>
          <input value={supplier} onChange={(e) => setSupplier(e.target.value)} className={`${inputCls} w-24`} />
        </label>
        <label className="text-xs text-text-secondary">
          <span className="mb-1 block">Úhrada</span>
          <select value={paidFrom} onChange={(e) => setPaidFrom(e.target.value as PaidFrom)} className={inputCls}>
            {payments.map((p) => <option key={p} value={p}>{paidFromLabels[p]}</option>)}
          </select>
        </label>
        <button type="submit" disabled={busy} className={primaryBtn}>Vytvořit zálohy</button>
      </div>
      {info && <p role="status" className="text-sm text-success">{info}</p>}
      <Errors items={errors} />
    </form>
  );
}

/** „Dokumenty bez zaúčtování“ (T11): uploaded invoices no cost entry refers to yet. Admin and Accountant. */
function UnaccountedDocuments({ onBook }: { onBook: (d: UnaccountedDocument) => void }) {
  const { user } = useAuth();
  const canSee = user?.role === 'Admin' || user?.role === 'Accountant';
  const { data } = useApi<UnaccountedDocument[]>(
    useCallback(() => (canSee ? getUnaccountedDocuments() : Promise.resolve([])), [canSee]), [canSee],
  );
  if (!data || data.length === 0) return null;
  return (
    <section className="space-y-2 rounded-2xl border border-warning bg-warning-light/40 p-4" aria-label="Dokumenty bez zaúčtování">
      <h2 className="font-semibold text-text-primary">Dokumenty bez zaúčtování ({data.length})</h2>
      <ul className="divide-y divide-border text-sm">
        {data.map((d) => (
          <li key={d.document.id} className="flex flex-wrap items-center justify-between gap-2 py-1.5">
            <span>
              {d.document.name}
              <span className="text-text-muted"> · nahráno {formatIsoDay(d.document.uploadedAt)}{d.componentName ? ` · ${d.componentName}` : ''}</span>
            </span>
            {user?.role === 'Admin' && (
              <button type="button" onClick={() => onBook(d)} className="text-xs font-medium text-accent hover:text-accent-hover">Zaúčtovat</button>
            )}
          </li>
        ))}
      </ul>
    </section>
  );
}

