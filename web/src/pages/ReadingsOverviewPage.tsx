import { useCallback, useMemo, useState } from 'react';
import { useAuth } from '../auth/AuthContext';
import { useApi } from '../hooks/useApi';
import { getAllReadings } from '../api/readings';
import { getMeters } from '../api/meters';
import { Spinner } from '../components/Spinner';
import {
  LineChart, Line, XAxis, YAxis, Tooltip, CartesianGrid, ResponsiveContainer, Legend,
} from 'recharts';
import type { ReadingResponse, WaterMeter } from '../types';

const czNum = (v: number, d = 1) =>
  new Intl.NumberFormat('cs-CZ', { minimumFractionDigits: d, maximumFractionDigits: d }).format(v);

type PeriodPreset = '1m' | '6m' | '1y' | 'all' | 'custom';

const COLORS = ['#4f46e5', '#71717a', '#3f3f46', '#a1a1aa', '#6366f1', '#52525b', '#818cf8', '#27272a', '#a78bfa', '#d4d4d8'];

function dateToKey(d: string): string { return d.split('T')[0]; }

function filterByRange(readings: ReadingResponse[], preset: PeriodPreset, customFrom: string, customTo: string): ReadingResponse[] {
  const now = new Date();
  let from: Date;
  let to: Date = customTo ? new Date(customTo) : now;

  switch (preset) {
    case '1m': from = new Date(now.getFullYear(), now.getMonth() - 1, 1); break;
    case '6m': from = new Date(now.getFullYear(), now.getMonth() - 6, 1); break;
    case '1y': from = new Date(now.getFullYear() - 1, now.getMonth(), 1); break;
    case 'all': from = new Date(2020, 0, 1); break;
    case 'custom': from = customFrom ? new Date(customFrom) : new Date(2020, 0, 1); break;
    default: from = new Date(2020, 0, 1);
  }

  return readings.filter((r) => {
    const rd = new Date(r.readingDate);
    return rd >= from && rd <= to;
  });
}

export function ReadingsOverviewPage() {
  const { user } = useAuth();
  const isAdmin = user?.role === 'Admin';

  const [preset, setPreset] = useState<PeriodPreset>('all');
  const [customFrom, setCustomFrom] = useState('');
  const [customTo, setCustomTo] = useState('');
  const [showChart, setShowChart] = useState(true);
  const [showMainMeter, setShowMainMeter] = useState(true);

  const { data: allReadings, loading, error } = useApi<ReadingResponse[]>(
    useCallback(() => getAllReadings(), []),
  );
  const { data: meters } = useApi<WaterMeter[]>(useCallback(() => getMeters(), []));

  // Filter readings by range
  const filtered = useMemo(() => {
    if (!allReadings) return [];
    let list = filterByRange(allReadings, preset, customFrom, customTo);
    // For member: filter to own house + main meter
    if (!isAdmin && user?.houseId) {
      const myMeterIds = new Set(meters?.filter((m) => m.houseId === user.houseId || m.type === 'Main').map((m) => m.id) ?? []);
      list = list.filter((r) => myMeterIds.has(r.meterId));
    }
    return list;
  }, [allReadings, preset, customFrom, customTo, isAdmin, user?.houseId, meters]);

  // Sorted meters
  const sortedMeters = useMemo(() => {
    if (!meters) return [];
    const relevant = isAdmin ? meters : meters.filter((m) => m.houseId === user?.houseId || m.type === 'Main');
    return [...relevant].sort((a, b) => {
      if (a.type === 'Main' && b.type !== 'Main') return -1;
      if (a.type !== 'Main' && b.type === 'Main') return 1;
      return (a.name || a.meterNumber).localeCompare(b.name || b.meterNumber, 'cs');
    });
  }, [meters, isAdmin, user?.houseId]);

  // Meters visible in chart/table (filtered by showMainMeter toggle)
  const visibleMeters = useMemo(() => {
    if (showMainMeter) return sortedMeters;
    return sortedMeters.filter((m) => m.type !== 'Main');
  }, [sortedMeters, showMainMeter]);

  // Build reading lookup: meterId -> dateKey -> reading
  const readingMap = useMemo(() => {
    const map = new Map<string, Map<string, ReadingResponse>>();
    for (const r of filtered) {
      const dk = dateToKey(r.readingDate);
      if (!map.has(r.meterId)) map.set(r.meterId, new Map());
      map.get(r.meterId)!.set(dk, r);
    }
    return map;
  }, [filtered]);

  // Unique dates (sorted)
  const allDates = useMemo(() => {
    const dateSet = new Set<string>();
    for (const r of filtered) dateSet.add(dateToKey(r.readingDate));
    return [...dateSet].sort((a, b) => new Date(a).getTime() - new Date(b).getTime());
  }, [filtered]);

  // Last reading per meter (for summary)
  const latestPerMeter = useMemo(() => {
    const map = new Map<string, ReadingResponse>();
    for (const m of sortedMeters) {
      const mReadings = readingMap.get(m.id);
      if (!mReadings) continue;
      let latest: ReadingResponse | null = null;
      for (const r of mReadings.values()) {
        if (!latest || new Date(r.readingDate) > new Date(latest.readingDate)) latest = r;
      }
      if (latest) map.set(m.id, latest);
    }
    return map;
  }, [sortedMeters, readingMap]);

  const mainMeter = sortedMeters.find((m) => m.type === 'Main');
  const individualMeters = sortedMeters.filter((m) => m.type === 'Individual');
  const mainLatest = mainMeter ? latestPerMeter.get(mainMeter.id) : undefined;
  const totalIndividualConsumption = individualMeters.reduce((sum, m) => sum + (latestPerMeter.get(m.id)?.consumption ?? 0), 0);
  const mainConsumption = mainLatest?.consumption ?? null;
  const loss = mainConsumption != null ? mainConsumption - totalIndividualConsumption : null;

  // ─── Chart data: multi-line, numeric X axis (timestamp) ───
  const chartData = useMemo(() => {
    if (allDates.length === 0) return [];
    return allDates.map((date) => {
      const ts = new Date(date).getTime();
      const point: Record<string, number> = { ts };
      for (const m of visibleMeters) {
        const r = readingMap.get(m.id)?.get(date);
        if (r != null) {
          point[m.id] = r.value;
        }
      }
      return point;
    });
  }, [allDates, visibleMeters, readingMap]);

  const formatXTick = (ts: number) => {
    const d = new Date(ts);
    return new Intl.DateTimeFormat('cs-CZ', { day: 'numeric', month: 'numeric', year: '2-digit' }).format(d);
  };

  const presetButtons: { key: PeriodPreset; label: string }[] = [
    { key: '1m', label: 'Poslední měsíc' },
    { key: '6m', label: 'Půl roku' },
    { key: '1y', label: 'Rok' },
    { key: 'all', label: 'Od začátku' },
    { key: 'custom', label: 'Vlastní' },
  ];

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-bold text-text-primary">Odečty</h1>
        <p className="mt-1 text-sm text-text-muted">Přehled odečtů vodoměrů</p>
      </div>

      {/* Period filter */}
      <div className="flex flex-col gap-3 sm:flex-row sm:items-end sm:flex-wrap">
        <div>
          <label className="block text-sm font-medium text-text-secondary mb-1">Období</label>
          <div className="flex rounded-xl border border-border-strong overflow-hidden">
            {presetButtons.map((btn) => (
              <button key={btn.key} onClick={() => setPreset(btn.key)}
                className={`px-3 py-2 text-sm font-medium whitespace-nowrap ${preset === btn.key ? 'bg-accent text-white' : 'bg-surface-raised text-text-secondary hover:bg-surface-sunken/50'}`}>
                {btn.label}
              </button>
            ))}
          </div>
        </div>
        {preset === 'custom' && (
          <div className="flex items-end gap-2">
            <div>
              <label className="block text-xs text-text-muted mb-1">Od</label>
              <input type="date" value={customFrom} onChange={(e) => setCustomFrom(e.target.value)} className="border border-border rounded-xl px-2 py-2 text-sm bg-surface-raised focus:border-accent focus:ring-2 focus:ring-accent/20" />
            </div>
            <div>
              <label className="block text-xs text-text-muted mb-1">Do</label>
              <input type="date" value={customTo} onChange={(e) => setCustomTo(e.target.value)} className="border border-border rounded-xl px-2 py-2 text-sm bg-surface-raised focus:border-accent focus:ring-2 focus:ring-accent/20" />
            </div>
          </div>
        )}
        <div className="flex gap-2 self-end">
          <button onClick={() => setShowMainMeter((p) => !p)}
            className={`rounded-xl px-4 py-2 text-sm font-medium border transition-colors ${showMainMeter ? 'bg-accent text-white border-accent' : 'bg-surface-raised text-text-secondary border-border-strong hover:bg-surface-sunken/50'}`}>
            {showMainMeter ? 'Skrýt společný' : 'Zobrazit společný'}
          </button>
          <button onClick={() => setShowChart((p) => !p)}
            className="rounded-xl bg-surface-raised px-4 py-2 text-sm font-medium text-text-secondary border border-border-strong hover:bg-surface-sunken/50">
            {showChart ? 'Skrýt graf' : 'Zobrazit graf'}
          </button>
        </div>
      </div>

      {loading && <div className="flex justify-center py-12"><Spinner size="lg" /></div>}
      {error && <div className="rounded-xl bg-danger-light p-4"><p className="text-sm text-danger">{error}</p></div>}

      {/* Multi-line chart */}
      {showChart && !loading && chartData.length > 0 && (
        <div className="rounded-2xl bg-surface-raised p-5 border border-border shadow-card">
          <h3 className="text-lg font-semibold text-text-primary mb-4">Stav vodoměrů (m³)</h3>
          <ResponsiveContainer width="100%" height={350}>
            <LineChart data={chartData} margin={{ top: 5, right: 20, left: 10, bottom: 5 }}>
              <CartesianGrid strokeDasharray="3 3" stroke="#e4e4e7" />
              <XAxis dataKey="ts" type="number" scale="time" domain={['dataMin', 'dataMax']}
                tickFormatter={formatXTick} tick={{ fontSize: 11 }} angle={-45} textAnchor="end" height={60} />
              <YAxis tick={{ fontSize: 12 }} tickFormatter={(v: number) => czNum(v)} />
              <Tooltip
                labelFormatter={(ts) => new Intl.DateTimeFormat('cs-CZ').format(new Date(Number(ts)))}
                formatter={(v, name) => {
                  const meter = visibleMeters.find((m) => m.id === String(name));
                  return [`${czNum(Number(v))} m³`, meter?.name || meter?.meterNumber || String(name)];
                }}
              />
              <Legend formatter={(value: string) => {
                const meter = visibleMeters.find((m) => m.id === value);
                return meter?.name || meter?.meterNumber || value;
              }} />
              {visibleMeters.map((meter, idx) => (
                <Line
                  key={meter.id}
                  type="monotone"
                  dataKey={meter.id}
                  stroke={COLORS[idx % COLORS.length]}
                  strokeWidth={meter.type === 'Main' ? 3 : 1.5}
                  strokeDasharray={meter.type === 'Main' ? undefined : undefined}
                  dot={{ r: 3 }}
                  connectNulls
                />
              ))}
            </LineChart>
          </ResponsiveContainer>
        </div>
      )}

      {!loading && !error && (
        <>
          {/* Summary metrics */}
          {latestPerMeter.size > 0 && (
            <div className="grid grid-cols-2 gap-4 sm:grid-cols-4">
              <div className="rounded-2xl border border-border bg-surface-raised p-4 shadow-card">
                <p className="text-xs text-text-muted">Hlavní vodoměr</p>
                <p className="text-xl font-bold">{mainLatest?.value != null ? czNum(mainLatest.value) : '—'} <span className="text-sm font-normal text-text-muted">m³</span></p>
              </div>
              <div className="rounded-2xl border border-border bg-surface-raised p-4 shadow-card">
                <p className="text-xs text-text-muted">Spotřeba hlavní</p>
                <p className="text-xl font-bold">{mainConsumption != null ? czNum(mainConsumption) : '—'} <span className="text-sm font-normal text-text-muted">m³</span></p>
              </div>
              <div className="rounded-2xl border border-border bg-surface-raised p-4 shadow-card">
                <p className="text-xs text-text-muted">Součet individuální</p>
                <p className="text-xl font-bold">{czNum(totalIndividualConsumption)} <span className="text-sm font-normal text-text-muted">m³</span></p>
              </div>
              {isAdmin && (
                <div className="rounded-2xl border border-border bg-surface-raised p-4 shadow-card">
                  <p className="text-xs text-text-muted">Ztráta na síti</p>
                  <p className={`text-xl font-bold ${loss != null && loss > 0 ? 'text-danger' : 'text-success'}`}>
                    {loss != null ? czNum(loss) : '—'} <span className="text-sm font-normal text-text-muted">m³</span>
                  </p>
                </div>
              )}
            </div>
          )}

          {/* Pivot table: rows = meters, columns = dates */}
          {allDates.length > 0 ? (
            <div className="bg-surface-raised border border-border rounded-2xl overflow-hidden shadow-card">
              <div className="overflow-x-auto">
                <table className="text-sm border-collapse">
                  <thead>
                    <tr className="bg-surface-sunken">
                      <th className="sticky left-0 z-10 bg-surface-sunken px-3 py-2 text-left text-xs font-semibold uppercase tracking-wider text-text-muted border-b border-r border-border min-w-[200px]">Vodoměr</th>
                      {allDates.map((date) => (
                        <th key={date} className="px-2 py-2 text-center text-xs font-semibold uppercase tracking-wider text-text-muted border-b border-border whitespace-nowrap min-w-[80px]">
                          <div className="text-xs">{new Intl.DateTimeFormat('cs-CZ', { day: 'numeric', month: 'numeric', year: 'numeric' }).format(new Date(date))}</div>
                        </th>
                      ))}
                    </tr>
                  </thead>
                  <tbody>
                    {visibleMeters.map((meter, idx) => {
                      const isMain = meter.type === 'Main';
                      const meterReadings = readingMap.get(meter.id);
                      return (
                        <tr key={meter.id} className={isMain ? 'bg-accent-light' : 'hover:bg-surface-sunken/50'}>
                          <td className={`sticky left-0 z-10 px-3 py-2 border-b border-r border-border ${isMain ? 'bg-accent-light' : 'bg-surface-raised'}`}>
                            <div className="flex items-center gap-2">
                              <span className={`inline-block w-3 h-3 rounded-full`} style={{ backgroundColor: COLORS[idx % COLORS.length] }} />
                              <div>
                                <div className="font-medium text-text-primary">{meter.name || meter.meterNumber}</div>
                                <div className="text-xs text-text-muted">{meter.meterNumber}{meter.houseName ? ` · ${meter.houseName}` : ''}</div>
                              </div>
                            </div>
                          </td>
                          {allDates.map((date) => {
                            const reading = meterReadings?.get(date);
                            if (!reading) {
                              return <td key={date} className="px-2 py-2 border-b border-border text-center text-text-muted">—</td>;
                            }
                            return (
                              <td key={date} className="px-2 py-2 border-b border-border text-center">
                                <span className="font-mono text-xs font-medium text-text-primary">{czNum(reading.value)}</span>
                                {reading.consumption != null && reading.consumption > 0 && (
                                  <div className="text-xs text-text-muted">+{czNum(reading.consumption)}</div>
                                )}
                              </td>
                            );
                          })}
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              </div>
            </div>
          ) : (
            <div className="bg-surface-sunken rounded-2xl p-12 text-center">
              <p className="text-text-muted">Pro vybrané období nebyly nalezeny žádné odečty.</p>
            </div>
          )}

          <p className="text-xs text-text-muted">
            {visibleMeters.length} vodoměrů × {allDates.length} měření. Barva bodu v grafu odpovídá barvě vodoměru v tabulce.
          </p>
        </>
      )}
    </div>
  );
}
