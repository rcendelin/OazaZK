import { useCallback, useMemo, useRef, useState } from 'react';
import { useApi } from '../hooks/useApi';
import { getReadings, updateReading } from '../api/readings';
import { getMeters } from '../api/meters';
import { Spinner } from '../components/Spinner';
import type { ReadingResponse, WaterMeter } from '../types';

const czNum = (v: number, d = 1) =>
  new Intl.NumberFormat('cs-CZ', { minimumFractionDigits: d, maximumFractionDigits: d }).format(v);
const czDate = (s: string) => new Intl.DateTimeFormat('cs-CZ').format(new Date(s));

function getMonthOptions(): Array<{ label: string; year: number; month: number }> {
  const now = new Date();
  const options: Array<{ label: string; year: number; month: number }> = [];
  const fmt = new Intl.DateTimeFormat('cs-CZ', { year: 'numeric', month: 'long' });
  for (let i = 0; i < 36; i++) {
    const d = new Date(now.getFullYear(), now.getMonth() - i, 1);
    const label = fmt.format(d);
    options.push({ label: label.charAt(0).toUpperCase() + label.slice(1), year: d.getFullYear(), month: d.getMonth() + 1 });
  }
  return options;
}

export function ReadingsListPage() {
  const now = new Date();
  const [selectedYear, setSelectedYear] = useState(now.getFullYear());
  const [selectedMonth, setSelectedMonth] = useState(now.getMonth() + 1);
  const [filterMeter, setFilterMeter] = useState<string>('all');

  const monthOptions = useMemo(() => getMonthOptions(), []);

  const fetchReadings = useCallback(() => getReadings(selectedYear, selectedMonth), [selectedYear, selectedMonth]);
  const fetchMeters = useCallback(() => getMeters(), []);

  const { data: readingsData, loading, error, refetch } = useApi(fetchReadings, [selectedYear, selectedMonth]);
  const { data: meters } = useApi<WaterMeter[]>(fetchMeters, []);

  const readings: ReadingResponse[] = readingsData?.readings ?? (Array.isArray(readingsData) ? readingsData as ReadingResponse[] : []);

  // Editing state
  const [editKey, setEditKey] = useState<string | null>(null); // "meterId|date"
  const [editValue, setEditValue] = useState('');
  const [editError, setEditError] = useState<string | null>(null);
  const [editSuccess, setEditSuccess] = useState<string | null>(null);
  const savingRef = useRef(false);

  const meterLookup = useMemo(() => {
    const map = new Map<string, WaterMeter>();
    meters?.forEach((m) => map.set(m.id, m));
    return map;
  }, [meters]);

  const filteredReadings = useMemo(() => {
    let list = [...readings];
    if (filterMeter !== 'all') {
      list = list.filter((r) => r.meterId === filterMeter);
    }
    // Sort: main meter first, then by house, then by date
    list.sort((a, b) => {
      const mA = meterLookup.get(a.meterId);
      const mB = meterLookup.get(b.meterId);
      if (mA?.type === 'Main' && mB?.type !== 'Main') return -1;
      if (mA?.type !== 'Main' && mB?.type === 'Main') return 1;
      const nameA = a.houseName ?? a.meterNumber;
      const nameB = b.houseName ?? b.meterNumber;
      const cmp = nameA.localeCompare(nameB, 'cs');
      if (cmp !== 0) return cmp;
      return new Date(b.readingDate).getTime() - new Date(a.readingDate).getTime();
    });
    return list;
  }, [readings, filterMeter, meterLookup]);

  const handleMonthChange = (value: string) => {
    const [y, m] = value.split('-').map(Number);
    setSelectedYear(y);
    setSelectedMonth(m);
    setEditKey(null);
    setEditError(null);
    setEditSuccess(null);
  };

  const startEdit = (r: ReadingResponse) => {
    const key = `${r.meterId}|${r.readingDate}`;
    setEditKey(key);
    setEditValue(String(r.value).replace('.', ','));
    setEditError(null);
    setEditSuccess(null);
  };

  const cancelEdit = () => {
    setEditKey(null);
    setEditValue('');
    setEditError(null);
  };

  const handleSave = async (r: ReadingResponse) => {
    if (savingRef.current) return;
    savingRef.current = true;
    setEditError(null);
    setEditSuccess(null);

    const parsed = parseFloat(editValue.replace(',', '.'));
    if (isNaN(parsed) || parsed < 0) {
      setEditError('Neplatná hodnota.');
      savingRef.current = false;
      return;
    }

    try {
      const dateStr = new Date(r.readingDate).toISOString().split('T')[0];
      await updateReading(r.meterId, dateStr, parsed);
      setEditKey(null);
      setEditValue('');
      setEditSuccess(`Odečet ${r.meterNumber} k ${czDate(r.readingDate)} upraven na ${czNum(parsed)} m³.`);
      refetch();
    } catch (err) {
      setEditError(err instanceof Error ? err.message : 'Uložení selhalo.');
    } finally {
      savingRef.current = false;
    }
  };

  const readingKey = (r: ReadingResponse) => `${r.meterId}|${r.readingDate}`;

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-bold text-gray-900">Seznam odečtů</h1>
        <p className="mt-1 text-sm text-gray-600">Kompletní seznam všech odečtů s možností editace</p>
      </div>

      {/* Filters */}
      <div className="flex flex-col gap-4 sm:flex-row sm:items-end">
        <div>
          <label className="block text-sm font-medium text-gray-700 mb-1">Období</label>
          <select value={`${selectedYear}-${selectedMonth}`} onChange={(e) => handleMonthChange(e.target.value)}
            className="border rounded-md px-3 py-2 text-sm focus:ring-2 focus:ring-blue-500 focus:border-blue-500">
            {monthOptions.map((opt) => (
              <option key={`${opt.year}-${opt.month}`} value={`${opt.year}-${opt.month}`}>{opt.label}</option>
            ))}
          </select>
        </div>
        <div>
          <label className="block text-sm font-medium text-gray-700 mb-1">Vodoměr</label>
          <select value={filterMeter} onChange={(e) => setFilterMeter(e.target.value)}
            className="border rounded-md px-3 py-2 text-sm focus:ring-2 focus:ring-blue-500 focus:border-blue-500">
            <option value="all">Všechny vodoměry</option>
            {meters?.map((m) => (
              <option key={m.id} value={m.id}>
                {m.name || m.meterNumber} {m.type === 'Main' ? '(hlavní)' : m.houseName ? `(${m.houseName})` : ''}
              </option>
            ))}
          </select>
        </div>
      </div>

      {/* Messages */}
      {editSuccess && (
        <div className="rounded-md border border-green-200 bg-green-50 p-3">
          <p className="text-sm text-green-700">{editSuccess}</p>
        </div>
      )}

      {loading && <div className="flex justify-center py-12"><Spinner size="lg" /></div>}
      {error && <div className="rounded-md bg-red-50 p-4"><p className="text-sm text-red-700">{error}</p></div>}

      {!loading && !error && (
        <div className="bg-white border rounded-lg overflow-hidden">
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="bg-gray-50 border-b">
                  <th className="text-left px-4 py-3 font-medium text-gray-700">Vodoměr</th>
                  <th className="text-left px-4 py-3 font-medium text-gray-700">Domácnost</th>
                  <th className="text-left px-4 py-3 font-medium text-gray-700">Datum</th>
                  <th className="text-right px-4 py-3 font-medium text-gray-700">Stav (m³)</th>
                  <th className="text-right px-4 py-3 font-medium text-gray-700">Spotřeba (m³)</th>
                  <th className="text-left px-4 py-3 font-medium text-gray-700">Zdroj</th>
                  <th className="text-left px-4 py-3 font-medium text-gray-700">Importováno</th>
                  <th className="text-right px-4 py-3 font-medium text-gray-700">Akce</th>
                </tr>
              </thead>
              <tbody>
                {filteredReadings.map((r) => {
                  const key = readingKey(r);
                  const isEditing = editKey === key;
                  const meter = meterLookup.get(r.meterId);
                  const isMain = meter?.type === 'Main';

                  return (
                    <tr key={key} className={`border-b ${isMain ? 'bg-blue-50' : 'hover:bg-gray-50'} ${isEditing ? 'bg-yellow-50' : ''}`}>
                      <td className="px-4 py-2">
                        <div className="flex items-center gap-2">
                          <span className={`inline-block px-1.5 py-0.5 rounded text-xs font-medium ${isMain ? 'bg-blue-200 text-blue-800' : 'bg-gray-100 text-gray-600'}`}>
                            {isMain ? 'H' : 'I'}
                          </span>
                          <span className="font-medium">{meter?.name || r.meterNumber}</span>
                          <span className="text-gray-400 text-xs">({r.meterNumber})</span>
                        </div>
                      </td>
                      <td className="px-4 py-2 text-gray-600">{r.houseName ?? '—'}</td>
                      <td className="px-4 py-2 text-gray-600 text-xs">{czDate(r.readingDate)}</td>
                      <td className="px-4 py-2 text-right font-mono">
                        {isEditing ? (
                          <div className="flex items-center gap-1 justify-end">
                            <input type="text" inputMode="decimal" value={editValue}
                              onChange={(e) => setEditValue(e.target.value)}
                              onKeyDown={(e) => { if (e.key === 'Enter') void handleSave(r); if (e.key === 'Escape') cancelEdit(); }}
                              className="w-24 border rounded px-2 py-1 text-sm text-right focus:ring-2 focus:ring-blue-500"
                              autoFocus />
                          </div>
                        ) : (
                          <span className="font-medium">{czNum(r.value)}</span>
                        )}
                      </td>
                      <td className="px-4 py-2 text-right font-mono">
                        {r.consumption != null ? czNum(r.consumption) : <span className="text-gray-300">—</span>}
                      </td>
                      <td className="px-4 py-2">
                        <span className={`inline-block px-1.5 py-0.5 rounded text-xs ${r.source === 'Import' ? 'bg-purple-100 text-purple-700' : 'bg-gray-100 text-gray-600'}`}>
                          {r.source === 'Import' ? 'Import' : 'Ruční'}
                        </span>
                      </td>
                      <td className="px-4 py-2 text-gray-400 text-xs">{czDate(r.importedAt)}</td>
                      <td className="px-4 py-2 text-right">
                        {isEditing ? (
                          <div className="flex gap-1 justify-end">
                            <button onClick={() => void handleSave(r)}
                              className="bg-blue-600 text-white px-2 py-1 rounded text-xs hover:bg-blue-700">Uložit</button>
                            <button onClick={cancelEdit}
                              className="bg-gray-100 text-gray-700 px-2 py-1 rounded text-xs hover:bg-gray-200">Zrušit</button>
                          </div>
                        ) : (
                          <button onClick={() => startEdit(r)}
                            className="text-blue-600 hover:text-blue-800 text-xs font-medium">Upravit</button>
                        )}
                        {isEditing && editError && <p className="text-xs text-red-600 mt-1">{editError}</p>}
                      </td>
                    </tr>
                  );
                })}
                {filteredReadings.length === 0 && (
                  <tr><td colSpan={8} className="px-4 py-8 text-center text-gray-400">
                    Žádné odečty pro vybrané období{filterMeter !== 'all' ? ' a vodoměr' : ''}
                  </td></tr>
                )}
              </tbody>
            </table>
          </div>
        </div>
      )}

      <p className="text-xs text-gray-400">
        {filteredReadings.length} odečtů. Klikněte na "Upravit" pro opravu hodnoty.
        Spotřeba se přepočítá automaticky jako rozdíl oproti předchozímu odečtu.
      </p>
    </div>
  );
}
