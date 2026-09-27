import { useCallback, useState } from 'react';
import { useApi } from '../hooks/useApi';
import { Spinner } from '../components/Spinner';
import { methodLabels } from '../api/costComponents';
import { getWaterSettlement } from '../api/waterSettlement';
import type { WaterHouse, WaterInterval, WaterSettlement } from '../api/waterSettlement';
import { formatIsoDay, shiftIsoDate, todayIso } from '../utils/date';

const inputCls = 'border border-border rounded-lg px-2 py-1.5 text-sm bg-surface-raised focus:border-accent focus:ring-2 focus:ring-accent/20';
const kc = (v: number) => `${v.toLocaleString('cs-CZ', { minimumFractionDigits: 2, maximumFractionDigits: 2 })} Kč`;
const m3 = (v: number) => `${v.toLocaleString('cs-CZ', { maximumFractionDigits: 3 })} m³`;

/**
 * Water PVK and losses (T05): for every interval between main meter readings the consumption, the loss,
 * the price from the PVK invoices, the method used for the losses and each house's cost — so members can
 * see how their water and loss share came about.
 */
export function WaterPage() {
  const today = todayIso();
  const [range, setRange] = useState({ from: shiftIsoDate(today, { months: -6 }), to: today });
  const [draft, setDraft] = useState(range);
  const { data, loading, error } = useApi<WaterSettlement>(
    useCallback(() => getWaterSettlement(range.from, range.to), [range]),
  );

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-bold text-text-primary">Voda a ztráty</h1>
        <p className="mt-1 max-w-3xl text-sm text-text-muted">
          Za každý úsek mezi odečty hlavního vodoměru: kolik vody prošlo hlavním vodoměrem, kolik naměřily domy,
          ztráta (rozdíl), cena za m³ podle faktur PVK a náklad každého domu. Ztráta se dělí podle metody platné
          v daném úseku.
        </p>
      </div>

      <form className="flex flex-wrap items-end gap-2" onSubmit={(e) => { e.preventDefault(); setRange({ ...draft }); }}>
        <label className="text-xs text-text-secondary">
          <span className="mb-1 block">Od</span>
          <input type="date" value={draft.from} onChange={(e) => setDraft({ ...draft, from: e.target.value })} className={inputCls} />
        </label>
        <label className="text-xs text-text-secondary">
          <span className="mb-1 block">Do</span>
          <input type="date" value={draft.to} onChange={(e) => setDraft({ ...draft, to: e.target.value })} className={inputCls} />
        </label>
        <button type="submit" className="rounded-xl bg-accent px-3 py-1.5 text-sm font-medium text-white hover:bg-accent-hover">Zobrazit</button>
      </form>

      {error && <div className="rounded-xl bg-danger-light p-4 text-sm text-danger">{error}</div>}
      {loading ? (
        <div className="flex justify-center p-8"><Spinner size="lg" /></div>
      ) : data && (
        <>
          {data.intervals.length === 0 && (
            <p className="text-sm text-text-muted">V tomto období nejsou dva odečty hlavního vodoměru.</p>
          )}
          <div className="space-y-4">
            {data.intervals.map((i) => <IntervalCard key={i.from} interval={i} />)}
          </div>
          {data.totals.length > 0 && (
            <section aria-label="Součty za období" className="space-y-2">
              <h2 className="text-lg font-semibold text-text-primary">Součty za období</h2>
              <HousesTable houses={data.totals} />
            </section>
          )}
        </>
      )}
    </div>
  );
}

function IntervalCard({ interval: i }: { interval: WaterInterval }) {
  return (
    <section
      aria-label={`Úsek ${formatIsoDay(i.from)} – ${formatIsoDay(i.to)}`}
      className="space-y-3 rounded-2xl border border-border bg-surface-raised p-4 shadow-card"
    >
      <div className="flex flex-wrap items-baseline justify-between gap-2">
        <h2 className="font-semibold text-text-primary">{formatIsoDay(i.from)} – {formatIsoDay(i.to)} <span className="text-sm font-normal text-text-muted">({i.days} dní)</span></h2>
        {!i.allocated && <span className="rounded-full bg-warning-light px-2 py-0.5 text-xs font-medium text-warning">nerozpočteno</span>}
      </div>
      <dl className="grid grid-cols-2 gap-x-6 gap-y-1 text-sm sm:grid-cols-4">
        <div><dt className="text-xs text-text-muted">Hlavní vodoměr</dt><dd>{m3(i.mainConsumptionM3)}</dd></div>
        <div><dt className="text-xs text-text-muted">Domy celkem</dt><dd>{m3(i.housesConsumptionM3)}</dd></div>
        <div><dt className="text-xs text-text-muted">Ztráta</dt><dd className={i.lossM3 < 0 ? 'text-danger' : ''}>{m3(i.lossM3)}</dd></div>
        <div><dt className="text-xs text-text-muted">Cena za m³</dt><dd>{i.pricePerM3 === null ? '—' : kc(i.pricePerM3)}</dd></div>
        <div><dt className="text-xs text-text-muted">Voda domů</dt><dd>{kc(i.waterCost)}</dd></div>
        <div><dt className="text-xs text-text-muted">Ztráty</dt><dd>{kc(i.lossCost)}</dd></div>
        <div><dt className="text-xs text-text-muted">Fakturováno</dt><dd>{kc(i.invoicedAmount)}</dd></div>
        <div><dt className="text-xs text-text-muted">Rozdíl proti fakturám</dt><dd>{kc(i.difference)}</dd></div>
      </dl>
      {i.lossSegments.length > 0 && (
        <p className="text-xs text-text-secondary">
          Ztráta rozdělena: {i.lossSegments.map((s) =>
            `${formatIsoDay(s.from)} – ${formatIsoDay(s.to)} (${s.days} dní) ${kc(s.amount)} ${methodLabels[s.method].toLowerCase()} mezi ${s.participants} domů`,
          ).join('; ')}.
        </p>
      )}
      {i.warnings.length > 0 && (
        <ul role="alert" className="space-y-1 rounded-lg bg-warning-light p-2 text-sm text-warning">
          {i.warnings.map((w) => <li key={w}>{w}</li>)}
        </ul>
      )}
      <HousesTable houses={i.houses} />
    </section>
  );
}

function HousesTable({ houses }: { houses: WaterHouse[] }) {
  return (
    <table className="w-full text-sm">
      <thead className="text-xs uppercase tracking-wider text-text-muted">
        <tr>
          <th className="py-1 text-left">Dům</th>
          <th className="py-1 text-right">Spotřeba</th>
          <th className="py-1 text-right">Voda</th>
          <th className="py-1 text-right">Ztráta</th>
          <th className="py-1 text-right">Celkem</th>
        </tr>
      </thead>
      <tbody className="divide-y divide-border">
        {houses.map((h) => (
          <tr key={h.houseId}>
            <td className="py-1">{h.houseName}</td>
            <td className="py-1 text-right">
              {h.isEstimate && <span title="Hraniční odečet je dopočtený (odhad)" className="mr-1 text-text-muted">≈</span>}
              {m3(h.consumptionM3)}
            </td>
            <td className="py-1 text-right">{kc(h.waterCost)}</td>
            <td className="py-1 text-right">{kc(h.lossCost)}</td>
            <td className="py-1 text-right font-medium">{kc(h.waterCost + h.lossCost)}</td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}
