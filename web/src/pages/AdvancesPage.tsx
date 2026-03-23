import { useCallback, useRef, useState } from 'react';
import { useApi } from '../hooks/useApi';
import { useAuth } from '../auth/AuthContext';
import { getAdvanceSettings, updateAdvanceSettings, calculateAdvances } from '../api/advanceSettings';
import { getHouses } from '../api/houses';
import { Spinner } from '../components/Spinner';
import type { AdvanceSettingsData, AdvanceCalculation, HouseAdvanceOverride } from '../api/advanceSettings';
import type { House } from '../types';

const fmt = (v: number | null | undefined) => {
  const n = typeof v === 'number' && !isNaN(v) ? v : 0;
  return new Intl.NumberFormat('cs-CZ', { minimumFractionDigits: 0, maximumFractionDigits: 0 }).format(n);
};
const fmtD = (v: number | null | undefined, d = 1) => {
  const n = typeof v === 'number' && !isNaN(v) ? v : 0;
  return new Intl.NumberFormat('cs-CZ', { minimumFractionDigits: d, maximumFractionDigits: d }).format(n);
};
const fmtDate = (s: string | null | undefined) => {
  if (!s || s.startsWith('0001')) return '—';
  try { return new Intl.DateTimeFormat('cs-CZ').format(new Date(s)); } catch { return '—'; }
};

export function AdvancesPage() {
  const { user } = useAuth();
  const isAdmin = user?.role === 'Admin';

  const { data: settings, loading: sLoading, refetch: refetchSettings } = useApi<AdvanceSettingsData>(
    useCallback(() => getAdvanceSettings(), []),
  );
  const { data: calc, loading: cLoading, refetch: refetchCalc } = useApi<AdvanceCalculation>(
    useCallback(() => calculateAdvances(), []),
  );
  const { data: houses } = useApi<House[]>(useCallback(() => getHouses(), []));

  // ── Settings edit ──
  const [editing, setEditing] = useState(false);
  const [form, setForm] = useState<AdvanceSettingsData | null>(null);
  const [coeffs, setCoeffs] = useState<Record<string, string>>({});
  const [msg, setMsg] = useState<{ type: 'ok' | 'err'; text: string } | null>(null);
  const savingRef = useRef(false);

  // ── Per-house override edit ──
  const [editingHouse, setEditingHouse] = useState<string | null>(null);
  const [houseForm, setHouseForm] = useState<{ water: string; elec: string; common: string }>({ water: '', elec: '', common: '' });

  const startEdit = () => {
    if (!settings) return;
    setForm({ ...settings });
    const c: Record<string, string> = {};
    for (const [k, v] of Object.entries(settings.electricityCoefficients)) c[k] = String(v).replace('.', ',');
    setCoeffs(c);
    setEditing(true);
    setMsg(null);
  };

  const handleSave = async () => {
    if (!form || savingRef.current) return;
    savingRef.current = true;
    setMsg(null);
    const parsedCoeffs: Record<string, number> = {};
    for (const [k, v] of Object.entries(coeffs)) {
      const p = parseFloat(v.replace(',', '.'));
      if (!isNaN(p)) parsedCoeffs[k] = p;
    }
    try {
      await updateAdvanceSettings({ ...form, electricityCoefficients: parsedCoeffs });
      setMsg({ type: 'ok', text: 'Nastavení uloženo.' });
      setEditing(false);
      refetchSettings();
      refetchCalc();
    } catch (err) {
      setMsg({ type: 'err', text: err instanceof Error ? err.message : 'Uložení selhalo.' });
    } finally {
      savingRef.current = false;
    }
  };

  const startHouseEdit = (houseId: string) => {
    const h = calc?.houses.find((x) => x.houseId === houseId);
    if (!h) return;
    setHouseForm({
      water: String(h.actual.water),
      elec: String(h.actual.electricity),
      common: String(h.actual.common),
    });
    setEditingHouse(houseId);
  };

  const saveHouseOverride = async () => {
    if (!settings || !editingHouse || savingRef.current) return;
    savingRef.current = true;
    const override: HouseAdvanceOverride = {
      waterAdvance: parseFloat(houseForm.water) || 0,
      electricityAdvance: parseFloat(houseForm.elec) || 0,
      commonAdvance: parseFloat(houseForm.common) || 0,
    };
    const newOverrides = { ...settings.houseOverrides, [editingHouse]: override };
    try {
      await updateAdvanceSettings({ ...settings, houseOverrides: newOverrides });
      setEditingHouse(null);
      refetchSettings();
      refetchCalc();
    } catch (err) {
      setMsg({ type: 'err', text: err instanceof Error ? err.message : 'Uložení selhalo.' });
    } finally {
      savingRef.current = false;
    }
  };

  const resetHouseOverride = async (houseId: string) => {
    if (!settings || savingRef.current) return;
    savingRef.current = true;
    const newOverrides = { ...settings.houseOverrides };
    delete newOverrides[houseId];
    try {
      await updateAdvanceSettings({ ...settings, houseOverrides: newOverrides });
      refetchSettings();
      refetchCalc();
    } catch (err) {
      setMsg({ type: 'err', text: err instanceof Error ? err.message : 'Reset selhal.' });
    } finally {
      savingRef.current = false;
    }
  };

  const coeffSum = Object.values(coeffs).reduce((s, v) => s + (parseFloat(v.replace(',', '.')) || 0), 0);
  const activeHouses = houses?.filter((h) => h.isActive) ?? [];

  if (sLoading || cLoading) return <div className="flex justify-center p-12"><Spinner size="lg" /></div>;

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-bold text-gray-900">Zálohy</h1>
        <p className="mt-1 text-sm text-gray-600">Měsíční zálohy pro jednotlivé domácnosti — oddělené složky</p>
      </div>

      {msg && (
        <div className={`rounded-md border p-3 ${msg.type === 'ok' ? 'border-green-200 bg-green-50' : 'border-red-200 bg-red-50'}`}>
          <p className={`text-sm ${msg.type === 'ok' ? 'text-green-700' : 'text-red-700'}`}>{msg.text}</p>
        </div>
      )}

      {/* ═══ Global settings ═══ */}
      <div className="bg-white border rounded-lg p-6">
        <div className="flex items-center justify-between mb-4">
          <h2 className="text-lg font-semibold">Nastavení</h2>
          {isAdmin && !editing && (
            <button onClick={startEdit} className="bg-blue-600 text-white px-4 py-2 rounded-lg hover:bg-blue-700 text-sm font-medium">
              Upravit
            </button>
          )}
        </div>

        {!editing ? (
          <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
            <div className="p-3 bg-blue-50 rounded-lg">
              <p className="text-xs font-medium text-blue-600 uppercase">Cena vody</p>
              <p className="text-xl font-bold mt-1">{fmtD(settings?.waterPricePerM3, 2)} <span className="text-sm font-normal text-gray-500">Kč/m³</span></p>
              <p className="text-xs text-gray-400 mt-0.5">Platnost: {fmtDate(settings?.waterPriceValidFrom)} — {settings?.waterPriceValidTo ? fmtDate(settings.waterPriceValidTo) : '∞'}</p>
            </div>
            <div className="p-3 bg-yellow-50 rounded-lg">
              <p className="text-xs font-medium text-yellow-700 uppercase">Elektřina vodárna</p>
              <p className="text-xl font-bold mt-1">{fmt(settings?.monthlyElectricityCost)} <span className="text-sm font-normal text-gray-500">Kč/měsíc celkem</span></p>
            </div>
            <div className="p-3 bg-gray-50 rounded-lg">
              <p className="text-xs font-medium text-gray-600 uppercase">Společný základ</p>
              <p className="text-xl font-bold mt-1">{fmt(settings?.monthlyCommonBaseFee)} <span className="text-sm font-normal text-gray-500">Kč/dům/měsíc</span></p>
            </div>
            <div className="p-3 bg-red-50 rounded-lg">
              <p className="text-xs font-medium text-red-600 uppercase">Průměrná ztráta</p>
              <p className="text-xl font-bold mt-1">{fmtD(calc?.monthlyLossM3)} <span className="text-sm font-normal text-gray-500">m³/měsíc</span></p>
            </div>
          </div>
        ) : form && (
          <div className="space-y-5">
            <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">Cena vody (Kč/m³)</label>
                <input type="number" step="0.01" value={form.waterPricePerM3}
                  onChange={(e) => setForm({ ...form, waterPricePerM3: parseFloat(e.target.value) || 0 })}
                  className="w-full border rounded-lg px-3 py-2 text-sm" />
              </div>
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">Platnost od</label>
                <input type="date" value={form.waterPriceValidFrom?.split('T')[0] ?? ''}
                  onChange={(e) => setForm({ ...form, waterPriceValidFrom: e.target.value })}
                  className="w-full border rounded-lg px-3 py-2 text-sm" />
              </div>
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">Platnost do (prázdné = ∞)</label>
                <input type="date" value={form.waterPriceValidTo?.split('T')[0] ?? ''}
                  onChange={(e) => setForm({ ...form, waterPriceValidTo: e.target.value || null })}
                  className="w-full border rounded-lg px-3 py-2 text-sm" />
              </div>
            </div>
            <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">Elektřina vodárna — celkem Kč/měsíc</label>
                <input type="number" step="1" value={form.monthlyElectricityCost}
                  onChange={(e) => setForm({ ...form, monthlyElectricityCost: parseFloat(e.target.value) || 0 })}
                  className="w-full border rounded-lg px-3 py-2 text-sm" />
              </div>
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">Společný základ Kč/dům/měsíc</label>
                <input type="number" step="1" value={form.monthlyCommonBaseFee}
                  onChange={(e) => setForm({ ...form, monthlyCommonBaseFee: parseFloat(e.target.value) || 0 })}
                  className="w-full border rounded-lg px-3 py-2 text-sm" />
              </div>
            </div>

            <div>
              <h3 className="text-sm font-semibold text-gray-700 mb-2">Koeficienty elektřiny (součet = 100%)</h3>
              <div className="grid grid-cols-2 md:grid-cols-4 gap-3">
                {activeHouses.map((h) => (
                  <div key={h.id}>
                    <label className="block text-xs text-gray-500 mb-0.5">{h.name}</label>
                    <div className="flex items-center gap-1">
                      <input type="text" inputMode="decimal" value={coeffs[h.id] ?? ''}
                        onChange={(e) => setCoeffs({ ...coeffs, [h.id]: e.target.value })}
                        placeholder="0" className="w-full border rounded px-2 py-1.5 text-sm text-right" />
                      <span className="text-xs text-gray-400">%</span>
                    </div>
                  </div>
                ))}
              </div>
              <p className={`text-xs mt-1 ${Math.abs(coeffSum - 100) > 0.1 ? 'text-red-600 font-medium' : 'text-green-600'}`}>
                Součet: {fmtD(coeffSum)}% {Math.abs(coeffSum - 100) > 0.1 ? '(musí být 100%)' : '✓'}
              </p>
            </div>

            <div className="flex gap-2">
              <button onClick={handleSave} className="bg-blue-600 text-white px-4 py-2 rounded-lg hover:bg-blue-700 text-sm font-medium">Uložit</button>
              <button onClick={() => setEditing(false)} className="bg-gray-100 text-gray-700 px-4 py-2 rounded-lg hover:bg-gray-200 text-sm">Zrušit</button>
            </div>
          </div>
        )}
      </div>

      {/* ═══ Per-house advances table ═══ */}
      {calc && (
        <div className="bg-white border rounded-lg overflow-hidden">
          <div className="px-6 py-4 border-b">
            <h2 className="text-lg font-semibold">Přehled záloh za jednotlivé domy</h2>
            <p className="text-xs text-gray-500 mt-0.5">Doporučené zálohy se počítají z průměrné spotřeby za poslední 3 odečty. Klikněte na dům pro nastavení skutečné výše.</p>
          </div>
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="bg-gray-50 border-b text-xs text-gray-500 uppercase">
                  <th className="text-left px-4 py-3">Domácnost</th>
                  <th className="text-right px-2 py-3">m³/měs</th>
                  <th className="text-right px-2 py-3">Ztráta m³</th>
                  <th className="text-right px-2 py-3">Podíl</th>
                  <th className="text-center px-2 py-3 bg-blue-50 border-l" colSpan={2}>Voda Kč</th>
                  <th className="text-center px-2 py-3 bg-yellow-50 border-l" colSpan={2}>Elektřina Kč</th>
                  <th className="text-center px-2 py-3 bg-gray-100 border-l" colSpan={2}>Společný Kč</th>
                  <th className="text-right px-3 py-3 bg-green-50 border-l font-bold">Celkem Kč</th>
                  {isAdmin && <th className="px-2 py-3"></th>}
                </tr>
                <tr className="bg-gray-50 border-b text-[10px] text-gray-400">
                  <th></th><th></th><th></th><th></th>
                  <th className="px-2 py-1 bg-blue-50 border-l text-right">Dopor.</th>
                  <th className="px-2 py-1 bg-blue-50 text-right">Aktuální</th>
                  <th className="px-2 py-1 bg-yellow-50 border-l text-right">Dopor.</th>
                  <th className="px-2 py-1 bg-yellow-50 text-right">Aktuální</th>
                  <th className="px-2 py-1 bg-gray-100 border-l text-right">Dopor.</th>
                  <th className="px-2 py-1 bg-gray-100 text-right">Aktuální</th>
                  <th className="px-2 py-1 bg-green-50 border-l"></th>
                  {isAdmin && <th></th>}
                </tr>
              </thead>
              <tbody>
                {calc.houses.map((h) => (
                  <tr key={h.houseId} className="border-b hover:bg-gray-50">
                    <td className="px-4 py-3">
                      <span className="font-medium">{h.houseName}</span>
                      {h.hasOverride && <span className="ml-1 text-[10px] text-amber-600 font-medium">upraven</span>}
                    </td>
                    <td className="px-2 py-3 text-right font-mono">{fmtD(h.avgMonthlyM3)}</td>
                    <td className="px-2 py-3 text-right font-mono text-red-500">{fmtD(h.lossShareM3)}</td>
                    <td className="px-2 py-3 text-right font-mono text-gray-500">{fmtD(h.sharePercent)}%</td>

                    <td className="px-2 py-3 text-right font-mono bg-blue-50/50 border-l text-gray-400">{fmt(h.recommended.water)}</td>
                    <td className="px-2 py-3 text-right font-mono bg-blue-50/50 font-semibold">
                      {editingHouse === h.houseId
                        ? <input type="number" value={houseForm.water} onChange={(e) => setHouseForm({ ...houseForm, water: e.target.value })}
                            className="w-16 border rounded px-1 py-0.5 text-right text-sm" />
                        : fmt(h.actual.water)}
                    </td>

                    <td className="px-2 py-3 text-right font-mono bg-yellow-50/50 border-l text-gray-400">{fmt(h.recommended.electricity)}</td>
                    <td className="px-2 py-3 text-right font-mono bg-yellow-50/50 font-semibold">
                      {editingHouse === h.houseId
                        ? <input type="number" value={houseForm.elec} onChange={(e) => setHouseForm({ ...houseForm, elec: e.target.value })}
                            className="w-16 border rounded px-1 py-0.5 text-right text-sm" />
                        : fmt(h.actual.electricity)}
                    </td>

                    <td className="px-2 py-3 text-right font-mono bg-gray-50 border-l text-gray-400">{fmt(h.recommended.common)}</td>
                    <td className="px-2 py-3 text-right font-mono bg-gray-50 font-semibold">
                      {editingHouse === h.houseId
                        ? <input type="number" value={houseForm.common} onChange={(e) => setHouseForm({ ...houseForm, common: e.target.value })}
                            className="w-16 border rounded px-1 py-0.5 text-right text-sm" />
                        : fmt(h.actual.common)}
                    </td>

                    <td className="px-3 py-3 text-right font-mono font-bold bg-green-50/50 border-l text-green-800">{fmt(h.actual.total)}</td>

                    {isAdmin && (
                      <td className="px-2 py-3 text-right">
                        {editingHouse === h.houseId ? (
                          <div className="flex gap-1">
                            <button onClick={saveHouseOverride} className="text-xs bg-blue-600 text-white px-2 py-1 rounded hover:bg-blue-700">Uložit</button>
                            <button onClick={() => setEditingHouse(null)} className="text-xs text-gray-500 hover:text-gray-700">×</button>
                          </div>
                        ) : (
                          <div className="flex gap-1">
                            <button onClick={() => startHouseEdit(h.houseId)} className="text-xs text-blue-600 hover:text-blue-800">Upravit</button>
                            {h.hasOverride && (
                              <button onClick={() => resetHouseOverride(h.houseId)} className="text-xs text-gray-400 hover:text-red-600">Reset</button>
                            )}
                          </div>
                        )}
                      </td>
                    )}
                  </tr>
                ))}

                {/* Totals */}
                <tr className="bg-gray-50 font-semibold border-t-2">
                  <td className="px-4 py-3">Celkem</td>
                  <td className="px-2 py-3 text-right font-mono">{fmtD(calc.houses.reduce((s, h) => s + h.avgMonthlyM3, 0))}</td>
                  <td className="px-2 py-3 text-right font-mono text-red-500">{fmtD(calc.houses.reduce((s, h) => s + h.lossShareM3, 0))}</td>
                  <td className="px-2 py-3"></td>
                  <td className="px-2 py-3 text-right font-mono bg-blue-50/50 border-l text-gray-400">{fmt(calc.houses.reduce((s, h) => s + h.recommended.water, 0))}</td>
                  <td className="px-2 py-3 text-right font-mono bg-blue-50/50">{fmt(calc.houses.reduce((s, h) => s + h.actual.water, 0))}</td>
                  <td className="px-2 py-3 text-right font-mono bg-yellow-50/50 border-l text-gray-400">{fmt(calc.houses.reduce((s, h) => s + h.recommended.electricity, 0))}</td>
                  <td className="px-2 py-3 text-right font-mono bg-yellow-50/50">{fmt(calc.houses.reduce((s, h) => s + h.actual.electricity, 0))}</td>
                  <td className="px-2 py-3 text-right font-mono bg-gray-50 border-l text-gray-400">{fmt(calc.houses.reduce((s, h) => s + h.recommended.common, 0))}</td>
                  <td className="px-2 py-3 text-right font-mono bg-gray-50">{fmt(calc.houses.reduce((s, h) => s + h.actual.common, 0))}</td>
                  <td className="px-3 py-3 text-right font-mono font-bold bg-green-50/50 border-l text-green-800">
                    {fmt(calc.houses.reduce((s, h) => s + h.actual.total, 0))}
                  </td>
                  {isAdmin && <td></td>}
                </tr>
              </tbody>
            </table>
          </div>
        </div>
      )}

      <div className="bg-amber-50 border border-amber-200 rounded-lg p-4 text-sm">
        <p className="font-semibold text-amber-800 mb-1">Jak se zálohy počítají</p>
        <ul className="text-xs text-amber-700 space-y-0.5 list-disc list-inside">
          <li><strong>Voda:</strong> (průměrná spotřeba + poměrná ztráta) × cena za m³. Ztráta se rozděluje poměrně dle spotřeby.</li>
          <li><strong>Elektřina vodárna:</strong> celkový náklad × koeficient domu (suma koeficientů = 100%).</li>
          <li><strong>Společný základ:</strong> fixní částka za údržbu, pojištění, správu — stejná pro každý dům.</li>
          <li>Admin může u každého domu přepsat doporučenou zálohu na vlastní hodnotu (tlačítko „Upravit").</li>
        </ul>
      </div>
    </div>
  );
}
