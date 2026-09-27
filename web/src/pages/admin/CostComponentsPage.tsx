import { useCallback, useState } from 'react';
import type { FormEvent } from 'react';
import { useApi } from '../../hooks/useApi';
import { Spinner } from '../../components/Spinner';
import { ReasonConfirmDialog } from '../../components/ReasonConfirmDialog';
import { ApiError } from '../../api/client';
import { getHouses } from '../../api/houses';
import {
  addParticipation,
  addRule,
  basisLabels,
  createCostComponent,
  deleteParticipation,
  deleteRule,
  endParticipation,
  getCostComponent,
  getCostComponents,
  getSegments,
  methodLabels,
  updateCostComponent,
  waterRoleLabels,
} from '../../api/costComponents';
import type {
  AllocationBasis,
  AllocationMethod,
  AllocationRule,
  AllocationSegment,
  CostComponent,
  WaterRole,
  CostComponentDetail,
  Participation,
} from '../../api/costComponents';
import type { House } from '../../types';
import { formatIsoDay, isoDayNumber, shiftIsoDate, todayIso } from '../../utils/date';
import { invalidNumberMessage, parseCzechNumber } from '../../utils/number';

const inputCls = 'border border-border rounded-xl px-3 py-2 text-sm bg-surface-raised focus:border-accent focus:ring-2 focus:ring-accent/20';
const primaryBtn = 'rounded-xl bg-accent px-4 py-2 text-sm font-medium text-white hover:bg-accent-hover disabled:opacity-50';
const methods: AllocationMethod[] = ['Metered', 'Equal', 'Ratio', 'Percent'];

/** Every reason the API gave (business rules answer with a list). */
function reasons(err: unknown): string[] {
  if (err instanceof ApiError && err.details.length > 0) return err.details;
  return [err instanceof Error ? err.message : 'Nastala neočekávaná chyba'];
}

function Errors({ items }: { items: string[] }) {
  if (items.length === 0) return null;
  return (
    <ul role="alert" className="space-y-1 rounded-xl bg-danger-light p-3 text-sm text-danger">
      {items.map((m) => <li key={m}>{m}</li>)}
    </ul>
  );
}

/** Admin: cost components, their allocation method over time and which houses take part (T02). */
export function CostComponentsPage() {
  const { data: components, loading, error, refetch } = useApi<CostComponent[]>(
    useCallback(() => getCostComponents(), []),
  );
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [showCreate, setShowCreate] = useState(false);
  const activeId = selectedId ?? components?.[0]?.id ?? null;

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="text-2xl font-bold text-text-primary">Nákladové složky</h1>
          <p className="mt-1 max-w-2xl text-sm text-text-muted">
            Co se rozpočítává, komu a jak. Metoda i účast domů platí vždy od data; změna se projeví až od svého data,
            dřívější období zůstává beze změny. Každá změna se zapíše do auditu.
          </p>
        </div>
        <button type="button" className={primaryBtn} onClick={() => setShowCreate((v) => !v)}>
          {showCreate ? 'Zavřít' : 'Nová složka'}
        </button>
      </div>

      {showCreate && (
        <CreateComponentForm
          onCreated={(c) => { setShowCreate(false); setSelectedId(c.id); refetch(); }}
        />
      )}

      {error && <div className="rounded-xl bg-danger-light p-4 text-sm text-danger">{error}</div>}

      {loading ? (
        <div className="flex justify-center p-8"><Spinner size="lg" /></div>
      ) : (
        <div className="overflow-x-auto rounded-2xl border border-border bg-surface-raised shadow-card">
          <table className="w-full text-sm">
            <thead className="bg-surface-sunken text-xs font-semibold uppercase tracking-wider text-text-muted">
              <tr>
                <th className="px-4 py-3 text-left">Složka</th>
                <th className="px-4 py-3 text-left">Kód</th>
                <th className="px-4 py-3 text-left">Od</th>
                <th className="px-4 py-3 text-left">Náklady</th>
                <th className="px-4 py-3 text-left">Metoda dnes</th>
                <th className="px-4 py-3 text-right">Domů dnes</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-border">
              {(components ?? []).map((c) => (
                <tr
                  key={c.id}
                  onClick={() => setSelectedId(c.id)}
                  className={`cursor-pointer hover:bg-surface-sunken/50 ${c.id === activeId ? 'bg-accent-light/40' : ''}`}
                >
                  <td className="px-4 py-3 font-medium">
                    {c.name}
                    {!c.active && <span className="ml-2 rounded-full bg-surface-sunken px-2 py-0.5 text-xs text-text-muted">neaktivní</span>}
                  </td>
                  <td className="px-4 py-3 font-mono text-xs">{c.code}</td>
                  <td className="px-4 py-3">{formatIsoDay(c.startDate)}</td>
                  <td className="px-4 py-3">{basisLabels[c.allocationBasis]}{c.waterRole !== 'None' && ` · ${waterRoleLabels[c.waterRole]}`}</td>
                  <td className="px-4 py-3">{c.currentMethod ? methodLabels[c.currentMethod] : '—'}</td>
                  <td className="px-4 py-3 text-right">{c.currentParticipants}</td>
                </tr>
              ))}
              {components && components.length === 0 && (
                <tr><td colSpan={6} className="px-4 py-8 text-center text-text-muted">Zatím žádné složky.</td></tr>
              )}
            </tbody>
          </table>
        </div>
      )}

      {activeId && <ComponentDetail key={activeId} id={activeId} onChanged={refetch} />}
    </div>
  );
}

function CreateComponentForm({ onCreated }: { onCreated: (c: CostComponent) => void }) {
  const [name, setName] = useState('');
  const [code, setCode] = useState('');
  const [startDate, setStartDate] = useState('2023-11-01');
  const [basis, setBasis] = useState<AllocationBasis>('CostEntries');
  const [method, setMethod] = useState<AllocationMethod>('Equal');
  const [waterRole, setWaterRole] = useState<WaterRole>('None');
  const [note, setNote] = useState('');
  const [errors, setErrors] = useState<string[]>([]);
  const [busy, setBusy] = useState(false);

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    setBusy(true);
    setErrors([]);
    try {
      onCreated(await createCostComponent({ name, code, startDate, allocationBasis: basis, waterRole: basis === 'Metered' ? waterRole : 'None', method, note: note || undefined }));
    } catch (err) {
      setErrors(reasons(err));
    } finally {
      setBusy(false);
    }
  };

  return (
    <form onSubmit={(e) => void submit(e)} className="space-y-3 rounded-2xl border border-border bg-surface-raised p-4 shadow-card">
      <div className="flex flex-wrap items-end gap-3">
        <label className="text-sm text-text-secondary">
          <span className="mb-1 block">Název</span>
          <input value={name} onChange={(e) => setName(e.target.value)} placeholder="Elektřina – vodárna" className={inputCls} required />
        </label>
        <label className="text-sm text-text-secondary">
          <span className="mb-1 block">Kód</span>
          <input value={code} onChange={(e) => setCode(e.target.value.toUpperCase())} placeholder="ELEKTRINA_VODARNA" className={`${inputCls} font-mono`} required />
        </label>
        <label className="text-sm text-text-secondary">
          <span className="mb-1 block">Účtuje se od</span>
          <input type="date" value={startDate} onChange={(e) => setStartDate(e.target.value)} className={inputCls} required />
        </label>
        <label className="text-sm text-text-secondary">
          <span className="mb-1 block">Náklady</span>
          <select
            value={basis}
            onChange={(e) => {
              const next = e.target.value as AllocationBasis;
              setBasis(next);
              setMethod(next === 'Metered' ? 'Metered' : 'Equal');
            }}
            className={inputCls}
          >
            <option value="CostEntries">{basisLabels.CostEntries}</option>
            <option value="Metered">{basisLabels.Metered}</option>
          </select>
        </label>
        {basis === 'Metered' && (
          <label className="text-sm text-text-secondary">
            <span className="mb-1 block">Role ve vyúčtování vody</span>
            <select
              value={waterRole}
              onChange={(e) => {
                const next = e.target.value as WaterRole;
                setWaterRole(next);
                if (next === 'Losses') setMethod('Equal');
              }}
              className={inputCls}
            >
              {(['None', 'Consumption', 'Losses'] as WaterRole[]).map((r) => <option key={r} value={r}>{waterRoleLabels[r]}</option>)}
            </select>
          </label>
        )}
        <label className="text-sm text-text-secondary">
          <span className="mb-1 block">Metoda rozpočtu</span>
          <select value={method} onChange={(e) => setMethod(e.target.value as AllocationMethod)} className={inputCls}>
            {methods.map((m) => <option key={m} value={m}>{methodLabels[m]}</option>)}
          </select>
        </label>
        <label className="text-sm text-text-secondary">
          <span className="mb-1 block">Poznámka</span>
          <input value={note} onChange={(e) => setNote(e.target.value)} className={inputCls} />
        </label>
        <button type="submit" disabled={busy} className={primaryBtn}>Založit</button>
      </div>
      <Errors items={errors} />
    </form>
  );
}

function ComponentDetail({ id, onChanged }: { id: string; onChanged: () => void }) {
  const { data: detail, loading, error, refetch } = useApi<CostComponentDetail>(
    useCallback(() => getCostComponent(id), [id]), [id],
  );
  const { data: houses } = useApi<House[]>(useCallback(() => getHouses(), []));
  const [editing, setEditing] = useState(false);

  const changed = () => { refetch(); onChanged(); };

  if (loading && !detail) return <div className="flex justify-center p-8"><Spinner size="lg" /></div>;
  if (error) return <div className="rounded-xl bg-danger-light p-4 text-sm text-danger">{error}</div>;
  if (!detail) return null;

  const { component } = detail;
  return (
    <section className="space-y-5 rounded-2xl border border-border bg-surface-raised p-5 shadow-card" aria-label={`Detail složky ${component.name}`}>
      {editing ? (
        <EditComponentForm component={component} onSaved={() => { setEditing(false); changed(); }} onCancel={() => setEditing(false)} />
      ) : (
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div>
            <h2 className="text-lg font-semibold text-text-primary">
              {component.name}
              {!component.active && <span className="ml-2 rounded-full bg-surface-sunken px-2 py-0.5 text-xs font-normal text-text-muted">neaktivní</span>}
            </h2>
            <p className="text-sm text-text-muted">
              {basisLabels[component.allocationBasis]} · účtuje se od {formatIsoDay(component.startDate)}
              {component.note ? ` · ${component.note}` : ''}
              {detail.lastClosedDay ? ` · uzavřeno do ${formatIsoDay(detail.lastClosedDay)}` : ''}
            </p>
          </div>
          <button type="button" onClick={() => setEditing(true)} className="text-sm font-medium text-accent hover:text-accent-hover">
            Upravit složku
          </button>
        </div>
      )}

      <Timeline detail={detail} />

      <div className="grid gap-5 lg:grid-cols-2">
        <RulesSection detail={detail} onChanged={changed} />
        <ParticipationSection detail={detail} houses={houses ?? []} onChanged={changed} />
      </div>

      <SegmentsSection componentId={component.id} startDate={component.startDate} />
    </section>
  );
}

/** Name, active flag and note (code, start and basis never change). */
function EditComponentForm({ component, onSaved, onCancel }: { component: CostComponent; onSaved: () => void; onCancel: () => void }) {
  const [name, setName] = useState(component.name);
  const [active, setActive] = useState(component.active);
  const [note, setNote] = useState(component.note ?? '');
  const [errors, setErrors] = useState<string[]>([]);
  const [busy, setBusy] = useState(false);

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    setBusy(true);
    setErrors([]);
    try {
      await updateCostComponent(component.id, { name: name.trim(), active, note: note.trim() || undefined });
      onSaved();
    } catch (err) {
      setErrors(reasons(err));
    } finally {
      setBusy(false);
    }
  };

  return (
    <form onSubmit={(e) => void submit(e)} className="space-y-2" aria-label="Upravit složku">
      <div className="flex flex-wrap items-end gap-2">
        <label className="text-xs text-text-secondary">
          <span className="mb-1 block">Název</span>
          <input value={name} onChange={(e) => setName(e.target.value)} className={inputCls} required />
        </label>
        <label className="text-xs text-text-secondary">
          <span className="mb-1 block">Poznámka</span>
          <input value={note} onChange={(e) => setNote(e.target.value)} className={inputCls} />
        </label>
        <label className="flex items-center gap-1 pb-2.5 text-sm text-text-secondary">
          <input type="checkbox" checked={active} onChange={(e) => setActive(e.target.checked)} />
          aktivní
        </label>
        <button type="submit" disabled={busy || !name.trim()} className={primaryBtn}>Uložit složku</button>
        <button type="button" onClick={onCancel} className="pb-2 text-sm text-text-muted hover:text-text-secondary">Zrušit</button>
      </div>
      <p className="text-xs text-text-muted">Kód, datum startu a základ rozpočtu se nemění.</p>
      <Errors items={errors} />
    </form>
  );
}

/** Rules as a band on top, one bar per participation below, on a shared time axis. */
function Timeline({ detail }: { detail: CostComponentDetail }) {
  const today = todayIso();
  const ends = [
    today,
    ...detail.rules.flatMap((r) => [r.validFrom, r.validTo ?? today]),
    ...detail.participations.flatMap((p) => [p.validFrom, p.validTo ?? today]),
  ];
  const start = isoDayNumber(detail.component.startDate);
  const end = Math.max(...ends.map(isoDayNumber)) + 1;
  const span = Math.max(1, end - start);
  const pos = (from: string, to: string | null) => {
    const left = ((isoDayNumber(from) - start) / span) * 100;
    const right = ((to ? isoDayNumber(to) + 1 : end) - start) / span * 100;
    return { left: `${Math.max(0, left)}%`, width: `${Math.max(0.5, right - Math.max(0, left))}%` };
  };
  const ruleColors = ['bg-accent/25', 'bg-success/25', 'bg-warning/25', 'bg-danger/20'];

  return (
    <div className="space-y-1.5" aria-label="Časová osa">
      <div className="flex justify-between text-xs text-text-muted">
        <span>{formatIsoDay(detail.component.startDate)}</span>
        <span>{formatIsoDay(shiftIsoDate('1970-01-01', { days: end - 1 }))}</span>
      </div>
      <div className="relative h-7 rounded-lg bg-surface-sunken">
        {detail.rules.map((r, i) => (
          <div
            key={r.id}
            className={`absolute top-0 flex h-7 items-center overflow-hidden rounded-lg px-2 text-xs font-medium text-text-primary ${ruleColors[i % ruleColors.length]}`}
            style={pos(r.validFrom, r.validTo)}
            title={`${methodLabels[r.method]} od ${formatIsoDay(r.validFrom)}${r.reason ? ` — ${r.reason}` : ''}`}
          >
            {methodLabels[r.method]}
          </div>
        ))}
      </div>
      {detail.participations.map((p) => (
        <div key={p.id} className="flex items-center gap-2">
          <span className="w-28 shrink-0 truncate text-xs text-text-secondary">{p.houseName}</span>
          <div className="relative h-4 flex-1 rounded bg-surface-sunken">
            <div
              className="absolute top-0 h-4 rounded bg-accent/60"
              style={pos(p.validFrom, p.validTo)}
              title={`${formatIsoDay(p.validFrom)} – ${p.validTo ? formatIsoDay(p.validTo) : 'dosud'}`}
            />
          </div>
        </div>
      ))}
      {detail.participations.length === 0 && <p className="text-xs text-text-muted">Žádný dům se zatím neúčastní.</p>}
    </div>
  );
}

function RulesSection({ detail, onChanged }: { detail: CostComponentDetail; onChanged: () => void }) {
  const [validFrom, setValidFrom] = useState(todayIso());
  const [method, setMethod] = useState<AllocationMethod>('Ratio');
  const [ratioSource, setRatioSource] = useState('');
  const [reason, setReason] = useState('');
  const [errors, setErrors] = useState<string[]>([]);
  const [busy, setBusy] = useState(false);
  const [deleting, setDeleting] = useState<AllocationRule | null>(null);

  const remove = async (rule: AllocationRule, deleteReason: string | undefined) => {
    setDeleting(null);
    setBusy(true);
    setErrors([]);
    try {
      await deleteRule(detail.component.id, rule.id, deleteReason);
      onChanged();
    } catch (err) {
      setErrors(reasons(err));
    } finally {
      setBusy(false);
    }
  };

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    setBusy(true);
    setErrors([]);
    try {
      await addRule(detail.component.id, { validFrom, method, ratioSource: ratioSource || undefined, reason });
      setReason('');
      onChanged();
    } catch (err) {
      setErrors(reasons(err));
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="space-y-3">
      <h3 className="font-semibold text-text-primary">Metoda rozpočtu</h3>
      <ul className="divide-y divide-border rounded-xl border border-border text-sm">
        {detail.rules.map((r) => (
          <li key={r.id} className="flex items-start gap-2 px-3 py-2">
            <div className="flex-1">
              <span className="font-medium">{methodLabels[r.method]}</span>
              <span className="text-text-secondary"> · {formatIsoDay(r.validFrom)} – {r.validTo ? formatIsoDay(r.validTo) : 'dosud'}</span>
              {r.ratioSource && <span className="text-text-secondary"> · váhy z {r.ratioSource}</span>}
              {r.reason && <p className="text-xs text-text-muted">{r.reason}</p>}
            </div>
            {detail.rules.length > 1 && (
              <button type="button" disabled={busy} onClick={() => setDeleting(r)} aria-label={`Smazat pravidlo ${methodLabels[r.method]} od ${formatIsoDay(r.validFrom)}`}
                className="text-xs font-medium text-text-muted hover:text-danger disabled:opacity-50">
                Smazat
              </button>
            )}
          </li>
        ))}
      </ul>
      <form onSubmit={(e) => void submit(e)} className="flex flex-wrap items-end gap-2" aria-label="Změnit metodu">
        <label className="text-xs text-text-secondary">
          <span className="mb-1 block">Nová metoda od</span>
          <input type="date" value={validFrom} onChange={(e) => setValidFrom(e.target.value)} className={inputCls} required />
        </label>
        <label className="text-xs text-text-secondary">
          <span className="mb-1 block">Metoda</span>
          <select value={method} onChange={(e) => setMethod(e.target.value as AllocationMethod)} className={inputCls}>
            {methods.map((m) => <option key={m} value={m}>{methodLabels[m]}</option>)}
          </select>
        </label>
        {method === 'Ratio' && (
          <label className="text-xs text-text-secondary">
            <span className="mb-1 block">Váhy podle (kód složky)</span>
            <input value={ratioSource} onChange={(e) => setRatioSource(e.target.value.toUpperCase())} placeholder="VODA_PVK" className={`${inputCls} font-mono`} />
          </label>
        )}
        <label className="text-xs text-text-secondary">
          <span className="mb-1 block">Důvod</span>
          <input value={reason} onChange={(e) => setReason(e.target.value)} placeholder="hlasování schůze 10/2026" className={inputCls} required />
        </label>
        <button type="submit" disabled={busy} className={primaryBtn}>Změnit metodu</button>
      </form>
      <Errors items={errors} />
      {deleting && (
        <ReasonConfirmDialog
          title="Smazat pravidlo?"
          message={`Pravidlo „${methodLabels[deleting.method]}“ od ${formatIsoDay(deleting.validFrom)} bylo zadané omylem? Po smazání se prodlouží předchozí pravidlo.`}
          confirmLabel="Smazat pravidlo"
          onConfirm={(r) => void remove(deleting, r)}
          onCancel={() => setDeleting(null)}
        />
      )}
    </div>
  );
}

function ParticipationSection({ detail, houses, onChanged }: { detail: CostComponentDetail; houses: House[]; onChanged: () => void }) {
  const componentId = detail.component.id;
  const [houseId, setHouseId] = useState('');
  const [validFrom, setValidFrom] = useState(detail.component.startDate);
  const [validTo, setValidTo] = useState('');
  const [weight, setWeight] = useState('');
  const [errors, setErrors] = useState<string[]>([]);
  const [busy, setBusy] = useState(false);
  const [deleting, setDeleting] = useState<Participation | null>(null);
  const showWeight = detail.rules.some((r) => r.method === 'Percent' || (r.method === 'Ratio' && !r.ratioSource));

  const run = async (action: () => Promise<unknown>) => {
    setBusy(true);
    setErrors([]);
    try {
      await action();
      onChanged();
    } catch (err) {
      setErrors(reasons(err));
    } finally {
      setBusy(false);
    }
  };

  const submit = (e: FormEvent) => {
    e.preventDefault();
    const parsedWeight = weight.trim() ? parseCzechNumber(weight) : undefined;
    if (parsedWeight === null) { setErrors([invalidNumberMessage('Váha / %')]); return; }
    void run(() => addParticipation(componentId, {
      houseId,
      validFrom,
      validTo: validTo || undefined,
      weight: parsedWeight,
    }));
  };

  return (
    <div className="space-y-3">
      <h3 className="font-semibold text-text-primary">Účast domů</h3>
      <ul className="divide-y divide-border rounded-xl border border-border text-sm">
        {detail.participations.map((p) => (
          <ParticipationRow
            key={p.id}
            participation={p}
            busy={busy}
            onEnd={(to) => void run(() => endParticipation(componentId, p.id, { validTo: to }))}
            onDelete={() => setDeleting(p)}
          />
        ))}
        {detail.participations.length === 0 && <li className="px-3 py-2 text-text-muted">Žádná účast.</li>}
      </ul>
      <form onSubmit={submit} className="flex flex-wrap items-end gap-2" aria-label="Přidat účast">
        <label className="text-xs text-text-secondary">
          <span className="mb-1 block">Dům</span>
          <select value={houseId} onChange={(e) => setHouseId(e.target.value)} className={inputCls} required>
            <option value="">Vyberte…</option>
            {houses.filter((h) => h.isActive).map((h) => <option key={h.id} value={h.id}>{h.name}</option>)}
          </select>
        </label>
        <label className="text-xs text-text-secondary">
          <span className="mb-1 block">Od</span>
          <input type="date" value={validFrom} onChange={(e) => setValidFrom(e.target.value)} className={inputCls} required />
        </label>
        <label className="text-xs text-text-secondary">
          <span className="mb-1 block">Do (nepovinné)</span>
          <input type="date" value={validTo} onChange={(e) => setValidTo(e.target.value)} className={inputCls} />
        </label>
        {showWeight && (
          <label className="text-xs text-text-secondary">
            <span className="mb-1 block">Váha / %</span>
            <input value={weight} onChange={(e) => setWeight(e.target.value)} inputMode="decimal" className={`${inputCls} w-24`} />
          </label>
        )}
        <button type="submit" disabled={busy || !houseId} className={primaryBtn}>Přidat účast</button>
      </form>
      <Errors items={errors} />
      {deleting && (
        <ReasonConfirmDialog
          title="Smazat účast?"
          message={`Smazat účast domu ${deleting.houseName} (${formatIsoDay(deleting.validFrom)} – ${deleting.validTo ? formatIsoDay(deleting.validTo) : 'dosud'})? Mazat se má jen účast zadaná omylem — konec účasti nastavte tlačítkem „Ukončit k datu“.`}
          confirmLabel="Smazat účast"
          onConfirm={(reason) => {
            const target = deleting;
            setDeleting(null);
            void run(() => deleteParticipation(componentId, target.id, reason));
          }}
          onCancel={() => setDeleting(null)}
        />
      )}
    </div>
  );
}

function ParticipationRow({ participation: p, busy, onEnd, onDelete }: {
  participation: Participation;
  busy: boolean;
  onEnd: (validTo: string) => void;
  onDelete: () => void;
}) {
  const [endDate, setEndDate] = useState(p.validTo ?? todayIso());
  return (
    <li className="flex flex-wrap items-center gap-2 px-3 py-2">
      <span className="font-medium">{p.houseName}</span>
      <span className="text-text-secondary">
        {formatIsoDay(p.validFrom)} – {p.validTo ? formatIsoDay(p.validTo) : 'dosud'}
        {p.weight !== null && ` · váha ${p.weight.toLocaleString('cs-CZ')}`}
      </span>
      <span className="ml-auto flex items-center gap-2">
        <input
          type="date"
          value={endDate}
          onChange={(e) => setEndDate(e.target.value)}
          aria-label={`Poslední den účasti ${p.houseName}`}
          className="rounded-lg border border-border bg-surface-raised px-2 py-1 text-xs"
        />
        <button type="button" disabled={busy} onClick={() => onEnd(endDate)} className="text-xs font-medium text-accent hover:text-accent-hover disabled:opacity-50">
          Ukončit k datu
        </button>
        <button type="button" disabled={busy} onClick={onDelete} className="text-xs font-medium text-text-muted hover:text-danger disabled:opacity-50">
          Smazat
        </button>
      </span>
    </li>
  );
}

function SegmentsSection({ componentId, startDate }: { componentId: string; startDate: string }) {
  const today = todayIso();
  const [range, setRange] = useState({ from: startDate > shiftIsoDate(today, { months: -12 }) ? startDate : shiftIsoDate(today, { months: -12 }), to: today });
  const [draft, setDraft] = useState(range);
  const { data: segments, loading, error } = useApi<AllocationSegment[]>(
    useCallback(() => getSegments(componentId, range.from, range.to), [componentId, range]), [componentId, range],
  );

  return (
    <div className="space-y-3">
      <h3 className="font-semibold text-text-primary">Úseky rozpočtu</h3>
      <p className="text-xs text-text-muted">Úsek je část období, ve které se nemění metoda ani účastníci. Vyúčtování přes více úseků se dělí podle dní.</p>
      <form
        className="flex flex-wrap items-end gap-2"
        onSubmit={(e) => { e.preventDefault(); setRange({ ...draft }); }}
      >
        <label className="text-xs text-text-secondary">
          <span className="mb-1 block">Od</span>
          <input type="date" value={draft.from} onChange={(e) => setDraft({ ...draft, from: e.target.value })} className={inputCls} />
        </label>
        <label className="text-xs text-text-secondary">
          <span className="mb-1 block">Do</span>
          <input type="date" value={draft.to} onChange={(e) => setDraft({ ...draft, to: e.target.value })} className={inputCls} />
        </label>
        <button type="submit" className={primaryBtn}>Zobrazit úseky</button>
      </form>
      {error && <div className="rounded-xl bg-danger-light p-3 text-sm text-danger">{error}</div>}
      {loading ? (
        <Spinner />
      ) : (
        <table className="w-full text-sm" aria-label="Úseky">
          <thead className="text-xs uppercase tracking-wider text-text-muted">
            <tr>
              <th className="py-2 text-left">Úsek</th>
              <th className="py-2 text-right">Dní</th>
              <th className="py-2 text-left pl-4">Metoda</th>
              <th className="py-2 text-left">Domy</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-border">
            {(segments ?? []).map((s) => (
              <tr key={s.from}>
                <td className="py-2 whitespace-nowrap">{formatIsoDay(s.from)} – {formatIsoDay(s.to)}</td>
                <td className="py-2 text-right">{s.days}</td>
                <td className="py-2 pl-4">{s.method ? methodLabels[s.method] : '—'}</td>
                <td className="py-2">{s.participants.map((p) => p.houseName).join(', ') || '—'}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  );
}
