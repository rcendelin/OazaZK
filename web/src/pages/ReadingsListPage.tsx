import { useCallback, useMemo, useRef, useState } from 'react';
import { useApi } from '../hooks/useApi';
import { getAllReadings, updateReading } from '../api/readings';
import { getMeters } from '../api/meters';
import { Spinner } from '../components/Spinner';
import type { ReadingResponse, WaterMeter } from '../types';

const czNum = (v: number, d = 1) =>
  new Intl.NumberFormat('cs-CZ', { minimumFractionDigits: d, maximumFractionDigits: d }).format(v);

const shortDate = (s: string) => {
  const d = new Date(s);
  return `${d.getDate()}.${d.getMonth() + 1}.${d.getFullYear()}`;
};

export function ReadingsListPage() {
  const { data: allReadings, loading, error, refetch } = useApi<ReadingResponse[]>(
    useCallback(() => getAllReadings(), []),
  );
  const { data: meters } = useApi<WaterMeter[]>(
    useCallback(() => getMeters(), []),
  );

  const [editCell, setEditCell] = useState<{ meterId: string; date: string } | null>(null);
  const [editValue, setEditValue] = useState('');
  const [editError, setEditError] = useState<string | null>(null);
  const [saveSuccess, setSaveSuccess] = useState<string | null>(null);
  const savingRef = useRef(false);

  // Sort meters: main first, then by name
  const sortedMeters = useMemo(() => {
    if (!meters) return [];
    return [...meters].sort((a, b) => {
      if (a.type === 'Main' && b.type !== 'Main') return -1;
      if (a.type !== 'Main' && b.type === 'Main') return 1;
      return (a.name || a.meterNumber).localeCompare(b.name || b.meterNumber, 'cs');
    });
  }, [meters]);

  // Collect unique dates (sorted chronologically)
  const allDates = useMemo(() => {
    if (!allReadings) return [];
    const dateSet = new Set<string>();
    for (const r of allReadings) {
      dateSet.add(r.readingDate.split('T')[0]);
    }
    return [...dateSet].sort((a, b) => new Date(a).getTime() - new Date(b).getTime());
  }, [allReadings]);

  // Build lookup: meterId -> date -> reading
  const readingMap = useMemo(() => {
    const map = new Map<string, Map<string, ReadingResponse>>();
    if (!allReadings) return map;
    for (const r of allReadings) {
      const dateKey = r.readingDate.split('T')[0];
      if (!map.has(r.meterId)) map.set(r.meterId, new Map());
      map.get(r.meterId)!.set(dateKey, r);
    }
    return map;
  }, [allReadings]);

  const startEdit = (meterId: string, date: string, currentValue: number) => {
    setEditCell({ meterId, date });
    setEditValue(String(currentValue).replace('.', ','));
    setEditError(null);
    setSaveSuccess(null);
  };

  const cancelEdit = () => {
    setEditCell(null);
    setEditValue('');
    setEditError(null);
  };

  const handleSave = async () => {
    if (!editCell || savingRef.current) return;
    savingRef.current = true;
    setEditError(null);

    const parsed = parseFloat(editValue.replace(',', '.'));
    if (isNaN(parsed) || parsed < 0) {
      setEditError('Neplatná hodnota');
      savingRef.current = false;
      return;
    }

    try {
      await updateReading(editCell.meterId, editCell.date, parsed);
      const meter = meters?.find((m) => m.id === editCell.meterId);
      setSaveSuccess(`${meter?.name || editCell.meterId} k ${shortDate(editCell.date)}: ${czNum(parsed)} m³`);
      setEditCell(null);
      setEditValue('');
      refetch();
    } catch (err) {
      setEditError(err instanceof Error ? err.message : 'Uložení selhalo');
    } finally {
      savingRef.current = false;
    }
  };

  if (loading) return <div className="flex justify-center p-12"><Spinner size="lg" /></div>;
  if (error) return <div className="bg-red-50 text-red-700 p-4 rounded-lg">{error}</div>;

  const isEditing = (meterId: string, date: string) =>
    editCell?.meterId === meterId && editCell?.date === date;

  return (
    <div className="space-y-4">
      <div>
        <h1 className="text-2xl font-bold text-gray-900">Seznam odečtů</h1>
        <p className="mt-1 text-sm text-gray-600">
          Všechny odečty — řádky = vodoměry, sloupce = data měření. Klikněte na hodnotu pro editaci.
        </p>
      </div>

      {saveSuccess && (
        <div className="rounded-md border border-green-200 bg-green-50 px-4 py-2">
          <p className="text-sm text-green-700">Uloženo: {saveSuccess}</p>
        </div>
      )}

      {allDates.length === 0 && (
        <div className="bg-gray-50 rounded-lg p-12 text-center">
          <p className="text-gray-400">Žádné odečty v systému. Importujte data nebo zadejte ručně.</p>
        </div>
      )}

      {allDates.length > 0 && (
        <div className="bg-white border rounded-lg overflow-hidden">
          <div className="overflow-x-auto">
            <table className="text-sm border-collapse">
              <thead>
                <tr className="bg-gray-50">
                  <th className="sticky left-0 z-10 bg-gray-50 px-3 py-2 text-left font-medium text-gray-700 border-b border-r min-w-[200px]">
                    Vodoměr
                  </th>
                  {allDates.map((date) => (
                    <th key={date} className="px-2 py-2 text-center font-medium text-gray-500 border-b whitespace-nowrap min-w-[90px]">
                      <div className="text-xs">{shortDate(date)}</div>
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {sortedMeters.map((meter) => {
                  const isMain = meter.type === 'Main';
                  const meterReadings = readingMap.get(meter.id);

                  return (
                    <tr key={meter.id} className={`${isMain ? 'bg-blue-50' : 'hover:bg-gray-50'}`}>
                      <td className={`sticky left-0 z-10 px-3 py-2 border-b border-r font-medium ${isMain ? 'bg-blue-50' : 'bg-white'}`}>
                        <div className="flex items-center gap-2">
                          <span className={`inline-block px-1.5 py-0.5 rounded text-xs font-medium ${isMain ? 'bg-blue-200 text-blue-800' : 'bg-gray-100 text-gray-600'}`}>
                            {isMain ? 'H' : 'I'}
                          </span>
                          <div>
                            <div className="text-gray-900">{meter.name || meter.meterNumber}</div>
                            <div className="text-xs text-gray-400">
                              {meter.meterNumber}
                              {meter.houseName ? ` · ${meter.houseName}` : ''}
                            </div>
                          </div>
                        </div>
                      </td>
                      {allDates.map((date) => {
                        const reading = meterReadings?.get(date);
                        const editing = isEditing(meter.id, date);

                        if (editing) {
                          return (
                            <td key={date} className="px-1 py-1 border-b bg-yellow-50">
                              <div className="flex flex-col items-center gap-0.5">
                                <input
                                  type="text"
                                  inputMode="decimal"
                                  value={editValue}
                                  onChange={(e) => setEditValue(e.target.value)}
                                  onKeyDown={(e) => {
                                    if (e.key === 'Enter') void handleSave();
                                    if (e.key === 'Escape') cancelEdit();
                                  }}
                                  className="w-20 border rounded px-1 py-0.5 text-xs text-right focus:ring-1 focus:ring-blue-500"
                                  autoFocus
                                />
                                <div className="flex gap-0.5">
                                  <button onClick={() => void handleSave()} className="text-blue-600 text-xs hover:underline">OK</button>
                                  <button onClick={cancelEdit} className="text-gray-400 text-xs hover:underline">×</button>
                                </div>
                                {editError && <span className="text-xs text-red-600">{editError}</span>}
                              </div>
                            </td>
                          );
                        }

                        if (!reading) {
                          return (
                            <td key={date} className="px-2 py-2 border-b text-center text-gray-200">
                              —
                            </td>
                          );
                        }

                        return (
                          <td
                            key={date}
                            className="px-2 py-2 border-b text-center cursor-pointer hover:bg-blue-50 group"
                            onClick={() => startEdit(meter.id, date, reading.value)}
                            title={`Klikněte pro editaci · Spotřeba: ${reading.consumption != null ? czNum(reading.consumption) + ' m³' : '—'} · ${reading.source === 'Import' ? 'Import' : 'Ruční'}`}
                          >
                            <span className="font-mono text-xs font-medium text-gray-900 group-hover:text-blue-600">
                              {czNum(reading.value)}
                            </span>
                            {reading.consumption != null && reading.consumption > 0 && (
                              <div className="text-xs text-gray-400">
                                +{czNum(reading.consumption)}
                              </div>
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
      )}

      <p className="text-xs text-gray-400">
        {sortedMeters.length} vodoměrů × {allDates.length} měření.
        Klikněte na hodnotu pro úpravu. Pod hodnotou je spotřeba (rozdíl oproti předchozímu měření).
        Tooltip ukazuje detail (spotřeba, zdroj).
      </p>
    </div>
  );
}
