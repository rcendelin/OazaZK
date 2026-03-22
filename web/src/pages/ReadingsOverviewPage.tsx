import { useCallback, useMemo, useState } from 'react';
import { useAuth } from '../auth/AuthContext';
import { useApi } from '../hooks/useApi';
import { getReadings, getChartData } from '../api/readings';
import { getMeters } from '../api/meters';
import { getHouses } from '../api/houses';
import { Spinner } from '../components/Spinner';
import {
  LineChart, Line, XAxis, YAxis, Tooltip, CartesianGrid, ResponsiveContainer, ReferenceLine,
} from 'recharts';
import type { ReadingResponse, WaterMeter, House, ChartDataPoint } from '../types';

const czNum = (v: number, d = 1) =>
  new Intl.NumberFormat('cs-CZ', { minimumFractionDigits: d, maximumFractionDigits: d }).format(v);
const czDate = (s: string) => new Intl.DateTimeFormat('cs-CZ').format(new Date(s));

type PeriodPreset = '1m' | '6m' | '1y' | 'all' | 'custom';

function computeRange(preset: PeriodPreset, customFrom: string, customTo: string): { from: string; to: string; year: number; month: number } {
  const now = new Date();
  const to = customTo || now.toISOString().split('T')[0];
  let from: string;

  switch (preset) {
    case '1m': {
      const d = new Date(now.getFullYear(), now.getMonth() - 1, 1);
      from = d.toISOString().split('T')[0];
      break;
    }
    case '6m': {
      const d = new Date(now.getFullYear(), now.getMonth() - 6, 1);
      from = d.toISOString().split('T')[0];
      break;
    }
    case '1y': {
      const d = new Date(now.getFullYear() - 1, now.getMonth(), 1);
      from = d.toISOString().split('T')[0];
      break;
    }
    case 'all':
      from = '2020-01-01';
      break;
    case 'custom':
      from = customFrom || '2020-01-01';
      break;
    default:
      from = '2020-01-01';
  }

  return { from, to, year: now.getFullYear(), month: now.getMonth() + 1 };
}

interface MeterSummary {
  meter: WaterMeter;
  value: number | null;
  consumption: number | null;
  readingDate: string | null;
  source: string | null;
}

export function ReadingsOverviewPage() {
  const { user } = useAuth();
  const isAdmin = user?.role === 'Admin';

  const [preset, setPreset] = useState<PeriodPreset>('1m');
  const [customFrom, setCustomFrom] = useState('');
  const [customTo, setCustomTo] = useState('');
  const [selectedHouseId, setSelectedHouseId] = useState<string | undefined>(undefined);
  const [showChart, setShowChart] = useState(true);

  const range = useMemo(() => computeRange(preset, customFrom, customTo), [preset, customFrom, customTo]);

  // Fetch latest readings (current month for table)
  const fetchReadings = useCallback(() => getReadings(range.year, range.month), [range.year, range.month]);
  const fetchMeters = useCallback(() => getMeters(), []);
  const fetchHouses = useCallback(() => getHouses(), []);

  const effectiveHouseId = isAdmin ? selectedHouseId : user?.houseId ?? undefined;

  const fetchChart = useCallback(
    () => getChartData(effectiveHouseId, range.from, range.to),
    [effectiveHouseId, range.from, range.to],
  );

  const { data: readingsData, loading: readingsLoading, error: readingsError } = useApi(fetchReadings, [range.year, range.month]);
  const { data: meters } = useApi<WaterMeter[]>(fetchMeters, []);
  const { data: houses } = useApi<House[]>(fetchHouses, []);
  const { data: chartData, loading: chartLoading } = useApi(fetchChart, [effectiveHouseId, range.from, range.to]);

  const readings = readingsData?.readings ?? (Array.isArray(readingsData) ? readingsData as ReadingResponse[] : []);

  const meterSummaries = useMemo((): MeterSummary[] => {
    if (!meters) return [];
    const sorted = [...meters].sort((a, b) => {
      if (a.type === 'Main' && b.type !== 'Main') return -1;
      if (a.type !== 'Main' && b.type === 'Main') return 1;
      return (a.name || a.meterNumber).localeCompare(b.name || b.meterNumber, 'cs');
    });
    const filtered = isAdmin ? sorted : sorted.filter((m) => m.type === 'Main' || m.houseId === user?.houseId);
    return filtered.map((meter) => {
      const reading = readings.find((r) => r.meterId === meter.id);
      return {
        meter,
        value: reading?.value ?? null,
        consumption: reading?.consumption ?? null,
        readingDate: reading?.readingDate ?? null,
        source: reading?.source ?? null,
      };
    });
  }, [meters, readings, isAdmin, user?.houseId]);

  const mainMeter = meterSummaries.find((s) => s.meter.type === 'Main');
  const individualMeters = meterSummaries.filter((s) => s.meter.type === 'Individual');
  const totalConsumption = individualMeters.reduce((sum, s) => sum + (s.consumption ?? 0), 0);
  const mainConsumption = mainMeter?.consumption ?? null;
  const loss = mainConsumption !== null ? mainConsumption - totalConsumption : null;
  const hasAnyData = meterSummaries.some((s) => s.value !== null);

  // Chart data
  const chartPoints = useMemo(() => {
    if (!chartData?.dataPoints) return [];
    return chartData.dataPoints.map((dp: ChartDataPoint) => ({ name: dp.label, consumption: dp.consumption }));
  }, [chartData]);

  const avgConsumption = useMemo(() => {
    if (!chartPoints.length) return null;
    return chartPoints.reduce((s, p) => s + p.consumption, 0) / chartPoints.length;
  }, [chartPoints]);

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
        <h1 className="text-2xl font-bold text-gray-900">Odečty</h1>
        <p className="mt-1 text-sm text-gray-600">Přehled odečtů vodoměrů</p>
      </div>

      {/* Period filter — unified for table + chart */}
      <div className="flex flex-col gap-3 sm:flex-row sm:items-end sm:flex-wrap">
        <div>
          <label className="block text-sm font-medium text-gray-700 mb-1">Období</label>
          <div className="flex rounded-md border border-gray-300 overflow-hidden">
            {presetButtons.map((btn) => (
              <button
                key={btn.key}
                onClick={() => setPreset(btn.key)}
                className={`px-3 py-2 text-sm font-medium transition-colors whitespace-nowrap ${
                  preset === btn.key ? 'bg-blue-600 text-white' : 'bg-white text-gray-700 hover:bg-gray-50'
                }`}
              >
                {btn.label}
              </button>
            ))}
          </div>
        </div>

        {preset === 'custom' && (
          <div className="flex items-end gap-2">
            <div>
              <label className="block text-xs text-gray-500 mb-1">Od</label>
              <input type="date" value={customFrom} onChange={(e) => setCustomFrom(e.target.value)}
                className="border rounded-md px-2 py-2 text-sm" />
            </div>
            <div>
              <label className="block text-xs text-gray-500 mb-1">Do</label>
              <input type="date" value={customTo} onChange={(e) => setCustomTo(e.target.value)}
                className="border rounded-md px-2 py-2 text-sm" />
            </div>
          </div>
        )}

        {isAdmin && houses && (
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">Dům</label>
            <select value={selectedHouseId ?? ''}
              onChange={(e) => setSelectedHouseId(e.target.value || undefined)}
              className="border rounded-md px-3 py-2 text-sm">
              <option value="">Celkem (všechny domy)</option>
              {houses.map((h) => <option key={h.id} value={h.id}>{h.name}</option>)}
            </select>
          </div>
        )}

        <button onClick={() => setShowChart((p) => !p)}
          className="rounded-md bg-white px-4 py-2 text-sm font-medium text-gray-700 ring-1 ring-gray-300 hover:bg-gray-50 self-end">
          {showChart ? 'Skrýt graf' : 'Zobrazit graf'}
        </button>
      </div>

      {/* Chart */}
      {showChart && (
        <div className="rounded-lg bg-white p-5 border">
          <h3 className="text-lg font-semibold text-gray-900 mb-4">
            Graf spotřeby
            {chartData?.houseName ? ` — ${chartData.houseName}` : ''}
          </h3>
          {chartLoading && <div className="flex justify-center py-12"><Spinner size="lg" /></div>}
          {!chartLoading && chartPoints.length === 0 && (
            <p className="text-center py-12 text-sm text-gray-400">Žádná data pro vybrané období</p>
          )}
          {!chartLoading && chartPoints.length > 0 && (
            <ResponsiveContainer width="100%" height={300}>
              <LineChart data={chartPoints} margin={{ top: 5, right: 20, left: 10, bottom: 5 }}>
                <CartesianGrid strokeDasharray="3 3" stroke="#e5e7eb" />
                <XAxis dataKey="name" tick={{ fontSize: 12 }} angle={-45} textAnchor="end" height={60} />
                <YAxis tick={{ fontSize: 12 }} tickFormatter={(v: number) => czNum(v)}
                  label={{ value: 'm³', position: 'insideTopLeft', offset: -5, style: { fontSize: 12 } }} />
                <Tooltip formatter={(v) => [`${czNum(Number(v))} m³`, 'Spotřeba']} labelStyle={{ fontWeight: 'bold' }} />
                <Line type="monotone" dataKey="consumption" stroke="#2563eb" strokeWidth={2}
                  dot={{ fill: '#2563eb', r: 4 }} activeDot={{ r: 6 }} />
                {avgConsumption !== null && (
                  <ReferenceLine y={avgConsumption} stroke="#9ca3af" strokeDasharray="5 5"
                    label={{ value: `Prům. ${czNum(avgConsumption)}`, position: 'insideTopRight', style: { fontSize: 11, fill: '#9ca3af' } }} />
                )}
              </LineChart>
            </ResponsiveContainer>
          )}
        </div>
      )}

      {readingsLoading && <div className="flex justify-center py-12"><Spinner size="lg" /></div>}
      {readingsError && <div className="rounded-md bg-red-50 p-4"><p className="text-sm text-red-700">{readingsError}</p></div>}

      {!readingsLoading && !readingsError && (
        <>
          {/* Summary metrics */}
          {hasAnyData && (
            <div className="grid grid-cols-2 gap-4 sm:grid-cols-4">
              <div className="rounded-lg border bg-white p-4">
                <p className="text-xs text-gray-500">Hlavní vodoměr</p>
                <p className="text-xl font-bold">{mainMeter?.value != null ? czNum(mainMeter.value) : '—'} <span className="text-sm font-normal text-gray-400">m³</span></p>
              </div>
              <div className="rounded-lg border bg-white p-4">
                <p className="text-xs text-gray-500">Spotřeba hlavní</p>
                <p className="text-xl font-bold">{mainConsumption != null ? czNum(mainConsumption) : '—'} <span className="text-sm font-normal text-gray-400">m³</span></p>
              </div>
              <div className="rounded-lg border bg-white p-4">
                <p className="text-xs text-gray-500">Součet individuální</p>
                <p className="text-xl font-bold">{czNum(totalConsumption)} <span className="text-sm font-normal text-gray-400">m³</span></p>
              </div>
              {isAdmin && (
                <div className="rounded-lg border bg-white p-4">
                  <p className="text-xs text-gray-500">Ztráta na síti</p>
                  <p className={`text-xl font-bold ${loss != null && loss > 0 ? 'text-red-600' : 'text-green-600'}`}>
                    {loss != null ? czNum(loss) : '—'} <span className="text-sm font-normal text-gray-400">m³</span>
                  </p>
                </div>
              )}
            </div>
          )}

          {/* Meter readings table */}
          <div className="bg-white border rounded-lg overflow-hidden">
            <div className="overflow-x-auto">
              <table className="w-full text-sm">
                <thead>
                  <tr className="bg-gray-50 border-b">
                    <th className="text-left px-4 py-3 font-medium text-gray-700">Typ</th>
                    <th className="text-left px-4 py-3 font-medium text-gray-700">Vodoměr</th>
                    <th className="text-left px-4 py-3 font-medium text-gray-700">Domácnost</th>
                    <th className="text-right px-4 py-3 font-medium text-gray-700">Stav (m³)</th>
                    <th className="text-right px-4 py-3 font-medium text-gray-700">Spotřeba (m³)</th>
                    <th className="text-left px-4 py-3 font-medium text-gray-700">Datum odečtu</th>
                    <th className="text-left px-4 py-3 font-medium text-gray-700">Zdroj</th>
                  </tr>
                </thead>
                <tbody>
                  {mainMeter && (
                    <tr className="border-b bg-blue-50">
                      <td className="px-4 py-3"><span className="inline-block px-2 py-0.5 rounded-full text-xs font-medium bg-blue-200 text-blue-800">Hlavní</span></td>
                      <td className="px-4 py-3 font-medium">{mainMeter.meter.name || mainMeter.meter.meterNumber} <span className="text-gray-400 text-xs">({mainMeter.meter.meterNumber})</span></td>
                      <td className="px-4 py-3 text-gray-400">—</td>
                      <td className="px-4 py-3 text-right font-mono font-medium">{mainMeter.value != null ? czNum(mainMeter.value) : <span className="text-gray-300">—</span>}</td>
                      <td className="px-4 py-3 text-right font-mono">{mainMeter.consumption != null ? czNum(mainMeter.consumption) : <span className="text-gray-300">—</span>}</td>
                      <td className="px-4 py-3 text-gray-600 text-xs">{mainMeter.readingDate ? czDate(mainMeter.readingDate) : '—'}</td>
                      <td className="px-4 py-3">{mainMeter.source && <span className={`inline-block px-1.5 py-0.5 rounded text-xs ${mainMeter.source === 'Import' ? 'bg-purple-100 text-purple-700' : 'bg-gray-100 text-gray-600'}`}>{mainMeter.source === 'Import' ? 'Import' : 'Ruční'}</span>}</td>
                    </tr>
                  )}
                  {individualMeters.map((s) => (
                    <tr key={s.meter.id} className="border-b hover:bg-gray-50">
                      <td className="px-4 py-3"><span className="inline-block px-2 py-0.5 rounded-full text-xs font-medium bg-gray-100 text-gray-600">Individuální</span></td>
                      <td className="px-4 py-3 font-medium">{s.meter.name || s.meter.meterNumber} <span className="text-gray-400 text-xs">({s.meter.meterNumber})</span></td>
                      <td className="px-4 py-3 text-gray-600">{s.meter.houseName ?? '—'}</td>
                      <td className="px-4 py-3 text-right font-mono font-medium">{s.value != null ? czNum(s.value) : <span className="text-gray-300">—</span>}</td>
                      <td className="px-4 py-3 text-right font-mono">{s.consumption != null ? <span className={s.consumption > 0 ? '' : 'text-gray-300'}>{czNum(s.consumption)}</span> : <span className="text-gray-300">—</span>}</td>
                      <td className="px-4 py-3 text-gray-600 text-xs">{s.readingDate ? czDate(s.readingDate) : '—'}</td>
                      <td className="px-4 py-3">{s.source && <span className={`inline-block px-1.5 py-0.5 rounded text-xs ${s.source === 'Import' ? 'bg-purple-100 text-purple-700' : 'bg-gray-100 text-gray-600'}`}>{s.source === 'Import' ? 'Import' : 'Ruční'}</span>}</td>
                    </tr>
                  ))}
                  {individualMeters.length > 0 && hasAnyData && (
                    <tr className="bg-gray-50 font-semibold">
                      <td className="px-4 py-3" colSpan={3}>Celkem individuální</td>
                      <td className="px-4 py-3 text-right font-mono">—</td>
                      <td className="px-4 py-3 text-right font-mono">{czNum(totalConsumption)}</td>
                      <td className="px-4 py-3" colSpan={2}></td>
                    </tr>
                  )}
                  {isAdmin && loss != null && hasAnyData && (
                    <tr className={`font-semibold ${loss > 0 ? 'bg-red-50 text-red-700' : 'bg-green-50 text-green-700'}`}>
                      <td className="px-4 py-3" colSpan={3}>Ztráta na síti</td>
                      <td className="px-4 py-3 text-right font-mono">—</td>
                      <td className="px-4 py-3 text-right font-mono">{czNum(loss)}</td>
                      <td className="px-4 py-3" colSpan={2}></td>
                    </tr>
                  )}
                  {meterSummaries.length === 0 && <tr><td colSpan={7} className="px-4 py-8 text-center text-gray-400">Žádné vodoměry v systému</td></tr>}
                  {meterSummaries.length > 0 && !hasAnyData && <tr><td colSpan={7} className="px-4 py-8 text-center text-gray-400">Pro vybrané období nebyly nalezeny žádné odečty</td></tr>}
                </tbody>
              </table>
            </div>
          </div>
        </>
      )}
    </div>
  );
}
