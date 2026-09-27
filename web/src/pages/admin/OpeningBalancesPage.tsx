import { useCallback, useState } from 'react';
import type { ReactNode } from 'react';
import { useApi } from '../../hooks/useApi';
import { Spinner } from '../../components/Spinner';
import { ApiError } from '../../api/client';
import { getHouses } from '../../api/houses';
import { getMeters } from '../../api/meters';
import { estimateReading } from '../../api/readings';
import type { ReadingEstimate } from '../../api/readings';
import { getCostComponents } from '../../api/costComponents';
import type { CostComponent } from '../../api/costComponents';
import {
  createOpeningBalance,
  deleteOpeningBalance,
  getOpeningBalances,
  getOwnershipPeriods,
  previewComponentCredit,
  startOwnership,
  updateOpeningBalance,
} from '../../api/openingBalances';
import type { ComponentCreditPreview, OpeningBalance, OwnershipPeriod, SaveOpeningBalance } from '../../api/openingBalances';
import type { House, WaterMeter } from '../../types';
import { formatIsoDay } from '../../utils/date';
import { parseCzechNumber } from '../../utils/number';
import { HelpDisclosure } from '../../components/help/HelpDisclosure';
import { HelpTerm } from '../../components/help/HelpTerm';
import type { TermId } from '../../content/help';
import { HouseTransferSection } from './HouseTransferSection';

const inputCls = 'border border-border rounded-lg px-2 py-1.5 text-sm bg-surface-raised focus:border-accent focus:ring-2 focus:ring-accent/20';
const primaryBtn = 'rounded-xl bg-accent px-3 py-1.5 text-sm font-medium text-white hover:bg-accent-hover disabled:opacity-50';
const linkBtn = 'text-xs font-medium text-accent hover:text-accent-hover disabled:opacity-50';

function reasons(err: unknown): string[] {
  if (err instanceof ApiError && err.details.length > 0) return err.details;
  return [err instanceof Error ? err.message : 'Nastala neočekávaná chyba'];
}

const fmtNumber = (value: number, decimals: number) =>
  value.toLocaleString('cs-CZ', { minimumFractionDigits: 0, maximumFractionDigits: decimals });
/** Editable form of a number: plain digits with a decimal comma (no grouping, ASCII minus). */
const inputNumber = (value: number) => String(value).replace('.', ',');

interface PageData {
  houses: House[];
  meters: WaterMeter[];
  components: CostComponent[];
  periods: OwnershipPeriod[];
  balances: OpeningBalance[];
}

const loadAll = async (): Promise<PageData> => {
  const [houses, meters, components, periods, balances] = await Promise.all([
    getHouses(), getMeters(), getCostComponents(), getOwnershipPeriods(), getOpeningBalances(),
  ]);
  return { houses, meters, components, periods, balances };
};

/**
 * Admin wizard „Počáteční stavy k datu“ (T03): the portal starts from known values at the accounting start
 * instead of reconstructing history (R3) — meter states (R4), fund shares (R5) and component credits (O2).
 */
export function OpeningBalancesPage() {
  const [date, setDate] = useState('2023-11-01');
  const { data, loading, error, refetch } = useApi<PageData>(useCallback(() => loadAll(), []));
  const [startInfo, setStartInfo] = useState<string | null>(null);
  const [startErrors, setStartErrors] = useState<string[]>([]);
  const [starting, setStarting] = useState(false);

  const start = async () => {
    setStarting(true);
    setStartErrors([]);
    try {
      const result = await startOwnership(date);
      setStartInfo(result.created > 0
        ? `Založeno ${result.created} období vlastnictví od ${formatIsoDay(date)}.`
        : 'Všechny domy už období vlastnictví mají, nic se nezměnilo.');
      refetch();
    } catch (err) {
      setStartErrors(reasons(err));
    } finally {
      setStarting(false);
    }
  };

  if (loading && !data) return <div className="flex justify-center p-8"><Spinner size="lg" /></div>;
  if (error) return <div className="rounded-xl bg-danger-light p-4 text-sm text-danger">{error}</div>;
  if (!data) return null;

  const houseName = (id: string | null) => data.houses.find((h) => h.id === id)?.name ?? '—';
  const activeHouses = data.houses.filter((h) => h.isActive).sort((a, b) => a.name.localeCompare(b.name, 'cs'));
  const meters = [...data.meters].sort((a, b) =>
    (a.type === 'Main' ? '' : houseName(a.houseId)).localeCompare(b.type === 'Main' ? '' : houseName(b.houseId), 'cs'));
  const find = (type: OpeningBalance['type'], match: (b: OpeningBalance) => boolean) =>
    data.balances.find((b) => b.type === type && match(b));
  const housesWithoutPeriod = activeHouses.filter((h) => !data.periods.some((p) => p.houseId === h.id));

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-bold text-text-primary">Počáteční stavy</h1>
        <p className="mt-1 max-w-3xl text-sm text-text-muted">
          Portál nepřepočítává celou historii. Začíná od známých hodnot k datu startu účtování: stav každého
          vodoměru, podíl domu ve fondu spolku a přeplatek (kredit) složky u dodavatele. U každé hodnoty uveďte,
          odkud je, a u dopočtené hodnoty zaškrtněte „odhad“.
        </p>
        <HelpDisclosure sectionId="openingBalances" />
      </div>

      <section className="space-y-3 rounded-2xl border border-border bg-surface-raised p-4 shadow-card" aria-label="Start účtování">
        <div className="flex flex-wrap items-end gap-3">
          <label className="text-sm text-text-secondary">
            <span className="mb-1 block">Datum startu účtování</span>
            <input type="date" value={date} onChange={(e) => setDate(e.target.value)} className={inputCls} />
          </label>
          <button type="button" onClick={() => void start()} disabled={starting || !date} className={primaryBtn}>
            Start účtování k datu
          </button>
          <p className="text-xs text-text-muted">
            Každý dům bez období vlastnictví dostane období od tohoto data (vlastník = kontaktní osoba domu).
            <HelpTerm id="startUctovani" />
          </p>
        </div>
        {startInfo && <p role="status" className="text-sm text-success">{startInfo}</p>}
        <Errors items={startErrors} />
        {housesWithoutPeriod.length > 0 && (
          <p className="text-sm text-warning">
            Bez období vlastnictví: {housesWithoutPeriod.map((h) => h.name).join(', ')}. Stavy vodoměrů a fondu jim zatím nelze zadat.
          </p>
        )}
      </section>

      <Section title="Stavy vodoměrů" term="stavVodomeru" hint="m³, na tři desetinná místa. Stav se zapíše i jako odečet k datu.">
        {meters.map((m) => {
          const existing = find('MeterReading', (b) => b.meterId === m.id);
          return (
            <BalanceRow
              key={`${m.id}-${existing?.key ?? 'new'}`}
              label={m.type === 'Main' ? `Hlavní vodoměr ${m.meterNumber}` : houseName(m.houseId)}
              sublabel={m.type === 'Main' ? undefined : `vodoměr ${m.meterNumber}`}
              unit="m³"
              existing={existing}
              date={date}
              suggest={(d) => estimateReading(m.id, d)}
              request={(v) => ({ type: 'MeterReading', meterId: m.id, ...v })}
              onSaved={refetch}
            />
          );
        })}
      </Section>

      <Section title="Podíl ve fondu spolku" term="podilFondu" hint="Kč ke dni poslední roční závěrky. Kladná hodnota = dům má u spolku přeplatek, záporná = nedoplatek.">
        {activeHouses.map((h) => {
          const existing = find('FundShare', (b) => b.houseId === h.id);
          return (
            <BalanceRow
              key={`${h.id}-${existing?.key ?? 'new'}`}
              label={h.name}
              unit="Kč"
              existing={existing}
              date={date}
              request={(v) => ({ type: 'FundShare', houseId: h.id, ...v })}
              onSaved={refetch}
            />
          );
        })}
      </Section>

      <Section title="Kredit složky u dodavatele" term="kreditSlozky" hint="Kč, přeplatek zadejte záporně (např. −20 000). Rozdělí se mezi domy podle metody a účasti složky k datu.">
        {data.components.filter((c) => c.allocationBasis === 'CostEntries').map((c) => {
          const existing = find('ComponentCredit', (b) => b.componentId === c.id);
          return (
            <BalanceRow
              key={`${c.id}-${existing?.key ?? 'new'}`}
              label={c.name}
              unit="Kč"
              existing={existing}
              date={date}
              preview={(d, v) => previewComponentCredit(c.id, d, v)}
              request={(v) => ({ type: 'ComponentCredit', componentId: c.id, ...v })}
              onSaved={refetch}
            />
          );
        })}
        {data.components.length === 0 && <p className="px-3 py-2 text-sm text-text-muted">Nejdřív založte nákladové složky.</p>}
      </Section>

      <HouseTransferSection houses={activeHouses} onDone={refetch} />
    </div>
  );
}

function Section({ title, hint, term, children }: { title: string; hint: string; term: TermId; children: ReactNode }) {
  return (
    <section className="space-y-2" aria-label={title}>
      <div>
        <h2 className="text-lg font-semibold text-text-primary">{title}<HelpTerm id={term} /></h2>
        <p className="text-xs text-text-muted">{hint}</p>
      </div>
      <div className="divide-y divide-border rounded-2xl border border-border bg-surface-raised shadow-card">{children}</div>
    </section>
  );
}

function Errors({ items }: { items: string[] }) {
  if (items.length === 0) return null;
  return (
    <ul role="alert" className="space-y-1 rounded-lg bg-danger-light p-2 text-sm text-danger">
      {items.map((m) => <li key={m}>{m}</li>)}
    </ul>
  );
}

type RowValues = Pick<SaveOpeningBalance, 'date' | 'value' | 'isEstimate' | 'source' | 'reason'>;

function BalanceRow({ label, sublabel, unit, existing, date, suggest, preview, request, onSaved }: {
  label: string;
  sublabel?: string;
  unit: string;
  existing: OpeningBalance | undefined;
  date: string;
  suggest?: (date: string) => Promise<ReadingEstimate>;
  preview?: (date: string, value: number) => Promise<ComponentCreditPreview>;
  request: (values: RowValues) => SaveOpeningBalance;
  onSaved: () => void;
}) {
  const [value, setValue] = useState(existing ? inputNumber(existing.value) : '');
  const [source, setSource] = useState(existing?.source ?? '');
  const [isEstimate, setIsEstimate] = useState(existing?.isEstimate ?? false);
  const [reason, setReason] = useState('');
  const [errors, setErrors] = useState<string[]>([]);
  const [info, setInfo] = useState<string | null>(null);
  const [shares, setShares] = useState<ComponentCreditPreview | null>(null);
  const [busy, setBusy] = useState(false);
  const rowDate = existing?.date ?? date;
  const locked = existing?.locked ?? false;

  const run = async (action: () => Promise<void>) => {
    setBusy(true);
    setErrors([]);
    setInfo(null);
    try {
      await action();
    } catch (err) {
      setErrors(reasons(err));
    } finally {
      setBusy(false);
    }
  };

  const save = () => run(async () => {
    const body = request({ date: rowDate, value: parseCzechNumber(value), isEstimate, source, reason: reason || undefined });
    if (existing) await updateOpeningBalance(existing.key, body);
    else await createOpeningBalance(body);
    setInfo('Uloženo.');
    onSaved();
  });

  const remove = () => run(async () => {
    if (!existing) return;
    await deleteOpeningBalance(existing.key, reason || undefined);
    onSaved();
  });

  const suggestFromReadings = () => run(async () => {
    if (!suggest) return;
    const estimate = await suggest(rowDate);
    if (estimate.value === null) {
      setInfo(estimate.note || 'Vodoměr nemá žádné odečty.');
      return;
    }
    setValue(inputNumber(estimate.value));
    setIsEstimate(estimate.isEstimate);
    setSource(estimate.note);
    setInfo(`Návrh: ${estimate.note} Hodnotu můžete přijmout uložením, nebo přepsat.`);
  });

  const showPreview = () => run(async () => {
    if (!preview) return;
    setShares(await preview(rowDate, parseCzechNumber(value)));
  });

  return (
    <div className="space-y-2 px-3 py-3">
      <div className="flex flex-wrap items-end gap-2">
        <div className="w-44 shrink-0">
          <p className="text-sm font-medium text-text-primary">{label}</p>
          {sublabel && <p className="text-xs text-text-muted">{sublabel}</p>}
          <p className="text-xs text-text-muted">k {formatIsoDay(rowDate)}{locked ? ' · uzavřeno' : ''}</p>
        </div>
        <label className="text-xs text-text-secondary">
          <span className="mb-1 block">Hodnota ({unit})</span>
          <input value={value} onChange={(e) => setValue(e.target.value)} inputMode="decimal" aria-label={`Hodnota ${label}`} className={`${inputCls} w-32 text-right`} />
        </label>
        <label className="min-w-[14rem] flex-1 text-xs text-text-secondary">
          <span className="mb-1 block">Zdroj</span>
          <input value={source} onChange={(e) => setSource(e.target.value)} placeholder="např. odečet 22. 5. 2023, foto Jindra" aria-label={`Zdroj ${label}`} className={`${inputCls} w-full`} />
        </label>
        <label className="flex items-center gap-1 pb-2 text-xs text-text-secondary">
          <input type="checkbox" checked={isEstimate} onChange={(e) => setIsEstimate(e.target.checked)} />
          odhad
        </label>
        {locked && (
          <label className="text-xs text-text-secondary">
            <span className="mb-1 block">Důvod opravy</span>
            <input value={reason} onChange={(e) => setReason(e.target.value)} className={inputCls} />
          </label>
        )}
        <div className="flex items-center gap-3 pb-1.5">
          {suggest && <button type="button" onClick={() => void suggestFromReadings()} disabled={busy} className={linkBtn}>Navrhnout z odečtů</button>}
          {preview && <button type="button" onClick={() => void showPreview()} disabled={busy || !value} className={linkBtn}>Náhled rozdělení</button>}
          <button type="button" onClick={() => void save()} disabled={busy || !value || !source.trim()} className={primaryBtn}>
            {existing ? 'Uložit změnu' : 'Uložit'}
          </button>
          {existing && !locked && (
            <button type="button" onClick={() => void remove()} disabled={busy} className="text-xs font-medium text-text-muted hover:text-danger disabled:opacity-50">Smazat</button>
          )}
        </div>
      </div>
      {info && <p role="status" className="text-xs text-text-secondary">{info}</p>}
      {shares && (
        <table className="text-xs" aria-label={`Rozdělení ${label}`}>
          <tbody>
            {shares.shares.map((s) => (
              <tr key={s.houseId}>
                <td className="pr-4">{s.houseName}</td>
                <td className="text-right">{fmtNumber(s.amount, 2)} Kč</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      <Errors items={errors} />
    </div>
  );
}
