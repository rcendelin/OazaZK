import { useCallback, useMemo, useState } from 'react';
import { useAuth } from '../auth/AuthContext';
import { useApi } from '../hooks/useApi';
import { getReadings } from '../api/readings';
import { getMeters } from '../api/meters';
import { ConsumptionChart } from '../components/ConsumptionChart';
import { Spinner } from '../components/Spinner';
import type { ReadingResponse, WaterMeter } from '../types';

const czNumber = (value: number, decimals = 1): string =>
  new Intl.NumberFormat('cs-CZ', { minimumFractionDigits: decimals, maximumFractionDigits: decimals }).format(value);

const czDate = (dateStr: string): string =>
  new Intl.DateTimeFormat('cs-CZ').format(new Date(dateStr));

function getMonthOptions(): Array<{ label: string; year: number; month: number }> {
  const now = new Date();
  const options: Array<{ label: string; year: number; month: number }> = [];
  const fmt = new Intl.DateTimeFormat('cs-CZ', { year: 'numeric', month: 'long' });
  for (let i = 0; i < 24; i++) {
    const d = new Date(now.getFullYear(), now.getMonth() - i, 1);
    const label = fmt.format(d);
    options.push({ label: label.charAt(0).toUpperCase() + label.slice(1), year: d.getFullYear(), month: d.getMonth() + 1 });
  }
  return options;
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

  const now = new Date();
  const [selectedYear, setSelectedYear] = useState(now.getFullYear());
  const [selectedMonth, setSelectedMonth] = useState(now.getMonth() + 1);
  const [showChart, setShowChart] = useState(true);

  const monthOptions = useMemo(() => getMonthOptions(), []);

  const fetchReadings = useCallback(() => getReadings(selectedYear, selectedMonth), [selectedYear, selectedMonth]);
  const fetchMeters = useCallback(() => getMeters(), []);

  const { data: readingsData, loading: readingsLoading, error: readingsError } = useApi(fetchReadings, [selectedYear, selectedMonth]);
  const { data: meters } = useApi<WaterMeter[]>(fetchMeters, []);

  const handleMonthChange = (value: string) => {
    const [y, m] = value.split('-').map(Number);
    setSelectedYear(y);
    setSelectedMonth(m);
  };

  // Build pivot: for each meter, find its reading in the selected month
  const readings = readingsData?.readings ?? (Array.isArray(readingsData) ? readingsData as ReadingResponse[] : []);

  const meterSummaries = useMemo((): MeterSummary[] => {
    if (!meters) return [];

    const sorted = [...meters].sort((a, b) => {
      if (a.type === 'Main' && b.type !== 'Main') return -1;
      if (a.type !== 'Main' && b.type === 'Main') return 1;
      return (a.name || a.meterNumber).localeCompare(b.name || b.meterNumber, 'cs');
    });

    // For member, filter to only their house's meters
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

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-bold text-gray-900">Odečty</h1>
        <p className="mt-1 text-sm text-gray-600">Přehled odečtů vodoměrů za vybrané období</p>
      </div>

      {/* Filters */}
      <div className="flex flex-col gap-4 sm:flex-row sm:items-end">
        <div className="flex-1 sm:max-w-xs">
          <label htmlFor="month-select" className="block text-sm font-medium text-gray-700">Období</label>
          <select id="month-select" value={`${selectedYear}-${selectedMonth}`} onChange={(e) => handleMonthChange(e.target.value)}
            className="mt-1 block w-full rounded-md border border-gray-300 bg-white px-3 py-2 text-sm shadow-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500">
            {monthOptions.map((opt) => (
              <option key={`${opt.year}-${opt.month}`} value={`${opt.year}-${opt.month}`}>{opt.label}</option>
            ))}
          </select>
        </div>
        <button onClick={() => setShowChart((p) => !p)}
          className="rounded-md bg-white px-4 py-2 text-sm font-medium text-gray-700 ring-1 ring-gray-300 hover:bg-gray-50">
          {showChart ? 'Skrýt graf' : 'Zobrazit graf'}
        </button>
      </div>

      {/* Chart */}
      {showChart && (
        <ConsumptionChart
          showHouseFilter={isAdmin}
          fixedHouseId={!isAdmin ? user?.houseId ?? undefined : undefined}
        />
      )}

      {readingsLoading && <div className="flex justify-center py-12"><Spinner size="lg" /></div>}
      {readingsError && <div className="rounded-md bg-red-50 p-4"><p className="text-sm text-red-700">{readingsError}</p></div>}

      {/* Summary table — pivot by meter */}
      {!readingsLoading && !readingsError && (
        <>
          {/* Summary metrics */}
          {hasAnyData && (
            <div className="grid grid-cols-2 gap-4 sm:grid-cols-4">
              <div className="rounded-lg border bg-white p-4">
                <p className="text-xs text-gray-500">Hlavní vodoměr</p>
                <p className="text-xl font-bold">{mainMeter?.value !== null ? czNumber(mainMeter!.value!) : '—'} <span className="text-sm font-normal text-gray-400">m³</span></p>
              </div>
              <div className="rounded-lg border bg-white p-4">
                <p className="text-xs text-gray-500">Spotřeba hlavní</p>
                <p className="text-xl font-bold">{mainConsumption !== null ? czNumber(mainConsumption) : '—'} <span className="text-sm font-normal text-gray-400">m³</span></p>
              </div>
              <div className="rounded-lg border bg-white p-4">
                <p className="text-xs text-gray-500">Součet individuální</p>
                <p className="text-xl font-bold">{czNumber(totalConsumption)} <span className="text-sm font-normal text-gray-400">m³</span></p>
              </div>
              {isAdmin && (
                <div className="rounded-lg border bg-white p-4">
                  <p className="text-xs text-gray-500">Ztráta na síti</p>
                  <p className={`text-xl font-bold ${loss !== null && loss > 0 ? 'text-red-600' : 'text-green-600'}`}>
                    {loss !== null ? czNumber(loss) : '—'} <span className="text-sm font-normal text-gray-400">m³</span>
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
                  {/* Main meter row */}
                  {mainMeter && (
                    <tr className="border-b bg-blue-50">
                      <td className="px-4 py-3">
                        <span className="inline-block px-2 py-0.5 rounded-full text-xs font-medium bg-blue-200 text-blue-800">Hlavní</span>
                      </td>
                      <td className="px-4 py-3 font-medium">
                        {mainMeter.meter.name || mainMeter.meter.meterNumber}
                        <span className="text-gray-400 text-xs ml-1">({mainMeter.meter.meterNumber})</span>
                      </td>
                      <td className="px-4 py-3 text-gray-400">—</td>
                      <td className="px-4 py-3 text-right font-mono font-medium">
                        {mainMeter.value !== null ? czNumber(mainMeter.value) : <span className="text-gray-300">—</span>}
                      </td>
                      <td className="px-4 py-3 text-right font-mono">
                        {mainMeter.consumption !== null ? czNumber(mainMeter.consumption) : <span className="text-gray-300">—</span>}
                      </td>
                      <td className="px-4 py-3 text-gray-600 text-xs">{mainMeter.readingDate ? czDate(mainMeter.readingDate) : '—'}</td>
                      <td className="px-4 py-3">
                        {mainMeter.source && (
                          <span className={`inline-block px-1.5 py-0.5 rounded text-xs ${mainMeter.source === 'Import' ? 'bg-purple-100 text-purple-700' : 'bg-gray-100 text-gray-600'}`}>
                            {mainMeter.source === 'Import' ? 'Import' : 'Ruční'}
                          </span>
                        )}
                      </td>
                    </tr>
                  )}

                  {/* Individual meters */}
                  {individualMeters.map((s) => (
                    <tr key={s.meter.id} className="border-b hover:bg-gray-50">
                      <td className="px-4 py-3">
                        <span className="inline-block px-2 py-0.5 rounded-full text-xs font-medium bg-gray-100 text-gray-600">Individuální</span>
                      </td>
                      <td className="px-4 py-3 font-medium">
                        {s.meter.name || s.meter.meterNumber}
                        <span className="text-gray-400 text-xs ml-1">({s.meter.meterNumber})</span>
                      </td>
                      <td className="px-4 py-3 text-gray-600">{s.meter.houseName ?? '—'}</td>
                      <td className="px-4 py-3 text-right font-mono font-medium">
                        {s.value !== null ? czNumber(s.value) : <span className="text-gray-300">—</span>}
                      </td>
                      <td className="px-4 py-3 text-right font-mono">
                        {s.consumption !== null ? (
                          <span className={s.consumption > 0 ? '' : 'text-gray-300'}>{czNumber(s.consumption)}</span>
                        ) : <span className="text-gray-300">—</span>}
                      </td>
                      <td className="px-4 py-3 text-gray-600 text-xs">{s.readingDate ? czDate(s.readingDate) : '—'}</td>
                      <td className="px-4 py-3">
                        {s.source && (
                          <span className={`inline-block px-1.5 py-0.5 rounded text-xs ${s.source === 'Import' ? 'bg-purple-100 text-purple-700' : 'bg-gray-100 text-gray-600'}`}>
                            {s.source === 'Import' ? 'Import' : 'Ruční'}
                          </span>
                        )}
                      </td>
                    </tr>
                  ))}

                  {/* Totals row */}
                  {individualMeters.length > 0 && hasAnyData && (
                    <tr className="bg-gray-50 font-semibold">
                      <td className="px-4 py-3" colSpan={3}>Celkem individuální</td>
                      <td className="px-4 py-3 text-right font-mono">—</td>
                      <td className="px-4 py-3 text-right font-mono">{czNumber(totalConsumption)}</td>
                      <td className="px-4 py-3" colSpan={2}></td>
                    </tr>
                  )}

                  {/* Loss row */}
                  {isAdmin && loss !== null && hasAnyData && (
                    <tr className={`font-semibold ${loss > 0 ? 'bg-red-50 text-red-700' : 'bg-green-50 text-green-700'}`}>
                      <td className="px-4 py-3" colSpan={3}>Ztráta na síti</td>
                      <td className="px-4 py-3 text-right font-mono">—</td>
                      <td className="px-4 py-3 text-right font-mono">{czNumber(loss)}</td>
                      <td className="px-4 py-3" colSpan={2}></td>
                    </tr>
                  )}

                  {meterSummaries.length === 0 && (
                    <tr><td colSpan={7} className="px-4 py-8 text-center text-gray-400">Žádné vodoměry v systému</td></tr>
                  )}
                  {meterSummaries.length > 0 && !hasAnyData && (
                    <tr><td colSpan={7} className="px-4 py-8 text-center text-gray-400">Pro vybrané období nebyly nalezeny žádné odečty</td></tr>
                  )}
                </tbody>
              </table>
            </div>
          </div>

          <p className="text-xs text-gray-400">
            {meterSummaries.length} vodoměrů celkem.
            Spotřeba = rozdíl oproti předchozímu odečtu.
            {isAdmin && ' Ztráta = spotřeba hlavního vodoměru − součet individuálních.'}
          </p>
        </>
      )}
    </div>
  );
}
