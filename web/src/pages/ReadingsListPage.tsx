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
  const [editDate, setEditDate] = useState('');
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
    setEditDate(date);
    setEditError(null);
    setSaveSuccess(null);
  };

  const cancelEdit = () => {
    setEditCell(null);
    setEditDate('');
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
      const dateChanged = editDate !== editCell.date;
      await updateReading(editCell.meterId, editCell.date, parsed, dateChanged ? editDate : undefined);
      const meter = meters?.find((m) => m.id === editCell.meterId);
      const dateInfo = dateChanged ? ` (datum změněno na ${shortDate(editDate)})` : '';
      setSaveSuccess(`${meter?.name || editCell.meterId}: ${czNum(parsed)} m³${dateInfo}`);
      setEditCell(null);
      setEditValue('');
      setEditDate('');
      refetch();
    } catch (err) {
      setEditError(err instanceof Error ? err.message : 'Uložení selhalo');
    } finally {
      savingRef.current = false;
    }
  };

  if (loading) return <div className="flex justify-center p-12"><Spinner size="lg" /></div>;
  if (error) return <div className="bg-danger-light text-danger p-4 rounded-2xl">{error}</div>;

  const isEditing = (meterId: string, date: string) =>
    editCell?.meterId === meterId && editCell?.date === date;

  return (
    <div className="space-y-4">
      <div>
        <h1 className="text-2xl font-bold text-text-primary">Seznam odečtů</h1>
        <p className="mt-1 text-sm text-text-secondary">
          Všechny odečty — řádky = vodoměry, sloupce = data měření. Klikněte na hodnotu pro editaci.
        </p>
      </div>

      {saveSuccess && (
        <div className="rounded-xl border border-success/20 bg-success-light px-4 py-2">
          <p className="text-sm text-success">Uloženo: {saveSuccess}</p>
        </div>
      )}

      {allDates.length === 0 && (
        <div className="bg-surface-sunken rounded-2xl p-12 text-center">
          <p className="text-text-muted">Žádné odečty v systému. Importujte data nebo zadejte ručně.</p>
        </div>
      )}

      {allDates.length > 0 && (
        <div className="bg-surface-raised border border-border rounded-2xl overflow-hidden shadow-card">
          <div className="overflow-x-auto">
            <table className="text-sm border-collapse">
              <thead>
                <tr className="bg-surface-sunken">
                  <th className="sticky left-0 z-10 bg-surface-sunken px-3 py-2 text-left text-xs font-semibold uppercase tracking-wider text-text-muted border-b border-r border-border min-w-[200px]">
                    Vodoměr
                  </th>
                  {allDates.map((date) => (
                    <th key={date} className="px-2 py-2 text-center text-xs font-semibold uppercase tracking-wider text-text-muted border-b border-border whitespace-nowrap min-w-[90px]">
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
                    <tr key={meter.id} className={`${isMain ? 'bg-accent-light' : 'hover:bg-surface-sunken/50'}`}>
                      <td className={`sticky left-0 z-10 px-3 py-2 border-b border-r border-border font-medium ${isMain ? 'bg-accent-light' : 'bg-surface-raised'}`}>
                        <div className="flex items-center gap-2">
                          <span className={`inline-block px-1.5 py-0.5 rounded text-xs font-medium ${isMain ? 'bg-accent-light text-accent' : 'bg-surface-sunken text-text-secondary'}`}>
                            {isMain ? 'H' : 'I'}
                          </span>
                          <div>
                            <div className="text-text-primary">{meter.name || meter.meterNumber}</div>
                            <div className="text-xs text-text-muted">
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
                            <td key={date} className="px-1 py-1 border-b border-border bg-warning-light">
                              <div className="flex flex-col items-center gap-1">
                                <input
                                  type="text"
                                  inputMode="decimal"
                                  value={editValue}
                                  onChange={(e) => setEditValue(e.target.value)}
                                  onKeyDown={(e) => {
                                    if (e.key === 'Enter') void handleSave();
                                    if (e.key === 'Escape') cancelEdit();
                                  }}
                                  placeholder="m³"
                                  className="w-20 border border-border rounded-xl px-1 py-0.5 text-xs text-right bg-surface-raised focus:ring-1 focus:ring-accent"
                                  autoFocus
                                />
                                <input
                                  type="date"
                                  value={editDate}
                                  onChange={(e) => setEditDate(e.target.value)}
                                  className="w-24 border border-border rounded-xl px-1 py-0.5 text-xs bg-surface-raised focus:ring-1 focus:ring-accent"
                                />
                                <div className="flex gap-1">
                                  <button onClick={() => void handleSave()} className="bg-accent text-white px-1.5 py-0.5 rounded-xl text-xs hover:bg-accent-hover">OK</button>
                                  <button onClick={cancelEdit} className="text-text-muted text-xs hover:underline">×</button>
                                </div>
                                {editError && <span className="text-xs text-danger">{editError}</span>}
                              </div>
                            </td>
                          );
                        }

                        if (!reading) {
                          return (
                            <td key={date} className="px-2 py-2 border-b border-border text-center text-text-muted">
                              —
                            </td>
                          );
                        }

                        return (
                          <td
                            key={date}
                            className="px-2 py-2 border-b border-border text-center cursor-pointer hover:bg-accent-light group"
                            onClick={() => startEdit(meter.id, date, reading.value)}
                            title={`Klikněte pro editaci · Spotřeba: ${reading.consumption != null ? czNum(reading.consumption) + ' m³' : '—'} · ${reading.source === 'Import' ? 'Import' : 'Ruční'}`}
                          >
                            <span className="font-mono text-xs font-medium text-text-primary group-hover:text-accent">
                              {czNum(reading.value)}
                            </span>
                            {reading.consumption != null && reading.consumption > 0 && (
                              <div className="text-xs text-text-muted">
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

      <p className="text-xs text-text-muted">
        {sortedMeters.length} vodoměrů × {allDates.length} měření.
        Klikněte na hodnotu pro úpravu. Pod hodnotou je spotřeba (rozdíl oproti předchozímu měření).
        Tooltip ukazuje detail (spotřeba, zdroj).
      </p>
    </div>
  );
}
