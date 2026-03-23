import { useCallback, useRef, useState } from 'react';
import { useApi } from '../hooks/useApi';
import { useAuth } from '../auth/AuthContext';
import { getAdvanceSettings, updateAdvanceSettings, calculateAdvances } from '../api/advanceSettings';
import { getHouses } from '../api/houses';
import { Spinner } from '../components/Spinner';
import type { AdvanceSettingsData, AdvanceCalculation } from '../api/advanceSettings';
import type { House } from '../types';

const czCurrency = new Intl.NumberFormat('cs-CZ', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
const czNum = (v: number, d = 1) => new Intl.NumberFormat('cs-CZ', { minimumFractionDigits: d, maximumFractionDigits: d }).format(v);
const czDate = (s: string) => s ? new Intl.DateTimeFormat('cs-CZ').format(new Date(s)) : '—';

export function AdvancesPage() {
  const { user } = useAuth();
  const isAdmin = user?.role === 'Admin';

  const { data: settings, loading: settingsLoading, refetch: refetchSettings } = useApi<AdvanceSettingsData>(
    useCallback(() => getAdvanceSettings(), []),
  );
  const { data: calc, loading: calcLoading, refetch: refetchCalc } = useApi<AdvanceCalculation>(
    useCallback(() => calculateAdvances(), []),
  );
  const { data: houses } = useApi<House[]>(useCallback(() => getHouses(), []));

  const [editing, setEditing] = useState(false);
  const [form, setForm] = useState<AdvanceSettingsData | null>(null);
  const [coefficients, setCoefficients] = useState<Record<string, string>>({});
  const [saveError, setSaveError] = useState<string | null>(null);
  const [saveSuccess, setSaveSuccess] = useState<string | null>(null);
  const savingRef = useRef(false);

  const startEdit = () => {
    if (!settings) return;
    setForm({ ...settings });
    const coeffs: Record<string, string> = {};
    for (const [k, v] of Object.entries(settings.electricityCoefficients)) {
      coeffs[k] = String(v).replace('.', ',');
    }
    setCoefficients(coeffs);
    setEditing(true);
    setSaveError(null);
    setSaveSuccess(null);
  };

  const handleSave = async () => {
    if (!form || savingRef.current) return;
    savingRef.current = true;
    setSaveError(null);

    // Parse coefficients
    const parsedCoeffs: Record<string, number> = {};
    for (const [k, v] of Object.entries(coefficients)) {
      const parsed = parseFloat(v.replace(',', '.'));
      if (!isNaN(parsed)) parsedCoeffs[k] = parsed;
    }

    const dataToSave: AdvanceSettingsData = {
      ...form,
      electricityCoefficients: parsedCoeffs,
    };

    try {
      await updateAdvanceSettings(dataToSave);
      setSaveSuccess('Nastavení uloženo.');
      setEditing(false);
      refetchSettings();
      refetchCalc();
    } catch (err) {
      setSaveError(err instanceof Error ? err.message : 'Uložení selhalo.');
    } finally {
      savingRef.current = false;
    }
  };

  const coeffSum = Object.values(coefficients).reduce((sum, v) => {
    const parsed = parseFloat(v.replace(',', '.'));
    return sum + (isNaN(parsed) ? 0 : parsed);
  }, 0);

  if (settingsLoading || calcLoading) return <div className="flex justify-center p-12"><Spinner size="lg" /></div>;

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-bold text-gray-900">Zálohy</h1>
        <p className="mt-1 text-sm text-gray-600">Výpočet měsíčních záloh pro jednotlivé domácnosti</p>
      </div>

      {saveSuccess && <div className="rounded-md border border-green-200 bg-green-50 p-3"><p className="text-sm text-green-700">{saveSuccess}</p></div>}
      {saveError && <div className="rounded-md border border-red-200 bg-red-50 p-3"><p className="text-sm text-red-700">{saveError}</p></div>}

      {/* Settings section */}
      <div className="bg-white border rounded-lg p-6">
        <div className="flex items-center justify-between mb-4">
          <h2 className="text-lg font-semibold">Nastavení záloh</h2>
          {isAdmin && !editing && (
            <button onClick={startEdit} className="bg-blue-600 text-white px-4 py-2 rounded-lg hover:bg-blue-700 text-sm font-medium">
              Upravit nastavení
            </button>
          )}
        </div>

        {!editing ? (
          <div className="grid grid-cols-1 md:grid-cols-3 gap-6">
            <div className="space-y-3">
              <h3 className="text-sm font-semibold text-gray-500 uppercase">Cena vody</h3>
              <div>
                <p className="text-2xl font-bold">{settings ? czCurrency.format(settings.waterPricePerM3) : '—'} <span className="text-sm font-normal text-gray-400">Kč/m³</span></p>
                <p className="text-xs text-gray-500 mt-1">
                  Platnost: {settings?.waterPriceValidFrom ? czDate(settings.waterPriceValidFrom) : '—'}
                  {' — '}
                  {settings?.waterPriceValidTo ? czDate(settings.waterPriceValidTo) : 'bez omezení'}
                </p>
              </div>
            </div>
            <div className="space-y-3">
              <h3 className="text-sm font-semibold text-gray-500 uppercase">Měsíční úhrada spolku</h3>
              <p className="text-2xl font-bold">{settings ? czCurrency.format(settings.monthlyAssociationFee) : '—'} <span className="text-sm font-normal text-gray-400">Kč/dům</span></p>
            </div>
            <div className="space-y-3">
              <h3 className="text-sm font-semibold text-gray-500 uppercase">Elektřina ke studni</h3>
              <p className="text-2xl font-bold">{settings ? czCurrency.format(settings.monthlyElectricityCost) : '—'} <span className="text-sm font-normal text-gray-400">Kč/měsíc celkem</span></p>
            </div>
          </div>
        ) : form && (
          <div className="space-y-6">
            <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">Cena vody (Kč/m³)</label>
                <input type="number" step="0.01" value={form.waterPricePerM3}
                  onChange={(e) => setForm({ ...form, waterPricePerM3: parseFloat(e.target.value) || 0 })}
                  className="w-full border rounded-lg px-3 py-2 text-sm" />
              </div>
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">Měsíční úhrada spolku (Kč/dům)</label>
                <input type="number" step="1" value={form.monthlyAssociationFee}
                  onChange={(e) => setForm({ ...form, monthlyAssociationFee: parseFloat(e.target.value) || 0 })}
                  className="w-full border rounded-lg px-3 py-2 text-sm" />
              </div>
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">Platnost ceny od</label>
                <input type="date" value={form.waterPriceValidFrom?.split('T')[0] ?? ''}
                  onChange={(e) => setForm({ ...form, waterPriceValidFrom: e.target.value })}
                  className="w-full border rounded-lg px-3 py-2 text-sm" />
              </div>
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">Platnost ceny do (prázdné = bez omezení)</label>
                <input type="date" value={form.waterPriceValidTo?.split('T')[0] ?? ''}
                  onChange={(e) => setForm({ ...form, waterPriceValidTo: e.target.value || null })}
                  className="w-full border rounded-lg px-3 py-2 text-sm" />
              </div>
              <div className="md:col-span-2">
                <label className="block text-sm font-medium text-gray-700 mb-1">Elektřina ke studni — celkem Kč/měsíc</label>
                <input type="number" step="1" value={form.monthlyElectricityCost}
                  onChange={(e) => setForm({ ...form, monthlyElectricityCost: parseFloat(e.target.value) || 0 })}
                  className="w-full border rounded-lg px-3 py-2 text-sm" />
              </div>
            </div>

            {/* Electricity coefficients per house */}
            <div>
              <h3 className="text-sm font-semibold text-gray-700 mb-2">Koeficienty elektřiny (% per domácnost, součet = 100%)</h3>
              <div className="grid grid-cols-2 md:grid-cols-4 gap-3">
                {houses?.filter((h) => h.isActive).map((house) => (
                  <div key={house.id}>
                    <label className="block text-xs text-gray-500 mb-0.5">{house.name}</label>
                    <div className="flex items-center gap-1">
                      <input type="text" inputMode="decimal"
                        value={coefficients[house.id] ?? ''}
                        onChange={(e) => setCoefficients({ ...coefficients, [house.id]: e.target.value })}
                        placeholder="0"
                        className="w-full border rounded px-2 py-1.5 text-sm text-right" />
                      <span className="text-xs text-gray-400">%</span>
                    </div>
                  </div>
                ))}
              </div>
              <p className={`text-xs mt-2 ${Math.abs(coeffSum - 100) > 0.1 ? 'text-red-600 font-medium' : 'text-green-600'}`}>
                Součet: {czNum(coeffSum, 1)}% {Math.abs(coeffSum - 100) > 0.1 ? '(musí být 100%)' : '✓'}
              </p>
            </div>

            <div className="flex gap-2">
              <button onClick={handleSave}
                className="bg-blue-600 text-white px-4 py-2 rounded-lg hover:bg-blue-700 text-sm font-medium">Uložit</button>
              <button onClick={() => setEditing(false)}
                className="bg-gray-100 text-gray-700 px-4 py-2 rounded-lg hover:bg-gray-200 text-sm">Zrušit</button>
            </div>
          </div>
        )}
      </div>

      {/* Calculation summary */}
      {calc && (
        <>
          <div className="grid grid-cols-2 gap-4 sm:grid-cols-4">
            <div className="rounded-lg border bg-white p-4">
              <p className="text-xs text-gray-500">Průměrná spotřeba hlavní/měsíc</p>
              <p className="text-xl font-bold">{czNum(calc.mainMeterMonthlyConsumptionM3)} <span className="text-sm font-normal text-gray-400">m³</span></p>
            </div>
            <div className="rounded-lg border bg-white p-4">
              <p className="text-xs text-gray-500">Průměrná spotřeba domů/měsíc</p>
              <p className="text-xl font-bold">{czNum(calc.totalIndividualMonthlyM3)} <span className="text-sm font-normal text-gray-400">m³</span></p>
            </div>
            <div className="rounded-lg border bg-white p-4">
              <p className="text-xs text-gray-500">Průměrná ztráta/měsíc</p>
              <p className="text-xl font-bold text-red-600">{czNum(calc.monthlyLossM3)} <span className="text-sm font-normal text-gray-400">m³</span></p>
            </div>
            <div className="rounded-lg border bg-white p-4">
              <p className="text-xs text-gray-500">Cena vody</p>
              <p className="text-xl font-bold">{czCurrency.format(calc.settings.waterPricePerM3)} <span className="text-sm font-normal text-gray-400">Kč/m³</span></p>
            </div>
          </div>

          {/* Per-house calculation table */}
          <div className="bg-white border rounded-lg overflow-hidden">
            <div className="overflow-x-auto">
              <table className="w-full text-sm">
                <thead>
                  <tr className="bg-gray-50 border-b">
                    <th className="text-left px-4 py-3 font-medium text-gray-700">Domácnost</th>
                    <th className="text-right px-3 py-3 font-medium text-gray-700">Spotřeba m³/měs</th>
                    <th className="text-right px-3 py-3 font-medium text-gray-700">Ztráta m³</th>
                    <th className="text-right px-3 py-3 font-medium text-gray-700">Celkem m³</th>
                    <th className="text-right px-3 py-3 font-medium text-gray-700">Podíl %</th>
                    <th className="text-right px-3 py-3 font-medium text-gray-700">Voda Kč</th>
                    <th className="text-right px-3 py-3 font-medium text-gray-700">Spolek Kč</th>
                    <th className="text-right px-3 py-3 font-medium text-gray-700">Elektřina %</th>
                    <th className="text-right px-3 py-3 font-medium text-gray-700">Elektřina Kč</th>
                    <th className="text-right px-3 py-3 font-medium text-gray-700 bg-blue-50">Záloha Kč</th>
                  </tr>
                </thead>
                <tbody>
                  {calc.houses.map((h) => (
                    <tr key={h.houseId} className="border-b hover:bg-gray-50">
                      <td className="px-4 py-3 font-medium">{h.houseName}</td>
                      <td className="px-3 py-3 text-right font-mono">{czNum(h.avgMonthlyConsumptionM3)}</td>
                      <td className="px-3 py-3 text-right font-mono text-red-600">{czNum(h.lossShareM3)}</td>
                      <td className="px-3 py-3 text-right font-mono">{czNum(h.totalWaterM3)}</td>
                      <td className="px-3 py-3 text-right font-mono">{czNum(h.sharePercent)}%</td>
                      <td className="px-3 py-3 text-right font-mono">{czCurrency.format(h.waterCostCzk)}</td>
                      <td className="px-3 py-3 text-right font-mono">{czCurrency.format(h.associationFeeCzk)}</td>
                      <td className="px-3 py-3 text-right font-mono">{czNum(h.electricityCoefficient)}%</td>
                      <td className="px-3 py-3 text-right font-mono">{czCurrency.format(h.electricityCostCzk)}</td>
                      <td className="px-3 py-3 text-right font-mono font-bold bg-blue-50">{czCurrency.format(h.totalAdvanceCzk)}</td>
                    </tr>
                  ))}
                  {/* Totals */}
                  <tr className="bg-gray-50 font-semibold border-t-2">
                    <td className="px-4 py-3">Celkem</td>
                    <td className="px-3 py-3 text-right font-mono">{czNum(calc.houses.reduce((s, h) => s + h.avgMonthlyConsumptionM3, 0))}</td>
                    <td className="px-3 py-3 text-right font-mono text-red-600">{czNum(calc.houses.reduce((s, h) => s + h.lossShareM3, 0))}</td>
                    <td className="px-3 py-3 text-right font-mono">{czNum(calc.houses.reduce((s, h) => s + h.totalWaterM3, 0))}</td>
                    <td className="px-3 py-3 text-right font-mono">—</td>
                    <td className="px-3 py-3 text-right font-mono">{czCurrency.format(calc.houses.reduce((s, h) => s + h.waterCostCzk, 0))}</td>
                    <td className="px-3 py-3 text-right font-mono">{czCurrency.format(calc.houses.reduce((s, h) => s + h.associationFeeCzk, 0))}</td>
                    <td className="px-3 py-3 text-right font-mono">—</td>
                    <td className="px-3 py-3 text-right font-mono">{czCurrency.format(calc.houses.reduce((s, h) => s + h.electricityCostCzk, 0))}</td>
                    <td className="px-3 py-3 text-right font-mono font-bold bg-blue-50">{czCurrency.format(calc.houses.reduce((s, h) => s + h.totalAdvanceCzk, 0))}</td>
                  </tr>
                </tbody>
              </table>
            </div>
          </div>

          <div className="bg-amber-50 border border-amber-200 rounded-lg p-4">
            <p className="text-sm text-amber-800 font-medium">Jak se záloha počítá</p>
            <p className="text-xs text-amber-700 mt-1">
              <strong>Záloha = úhrada spolku + elektřina + voda</strong><br />
              Voda = (průměrná spotřeba + poměrná ztráta) × cena za m³<br />
              Ztráta se rozpouští poměrově podle spotřeby jednotlivých domů<br />
              Elektřina ke studni se rozpouští podle nastavených koeficientů (musí dát 100%)<br />
              Průměrná spotřeba se počítá z posledních 3 odečtů
            </p>
          </div>
        </>
      )}
    </div>
  );
}
