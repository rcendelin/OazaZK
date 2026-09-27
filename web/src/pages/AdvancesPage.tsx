import { useCallback, useRef, useState } from 'react';
import { useApi } from '../hooks/useApi';
import { useAuth } from '../auth/AuthContext';
import { getAdvanceSettings, updateAdvanceSettings, calculateAdvances } from '../api/advanceSettings';
import { Spinner } from '../components/Spinner';
import type { AdvanceSettingsData, AdvanceCalculation, AdvanceAmounts, HouseAdvanceOverride } from '../api/advanceSettings';
import { parseCzechNumber } from '../utils/number';
import { formatIsoDay } from '../utils/date';

const fmt = (v: number | null | undefined) => {
  const n = typeof v === 'number' && !isNaN(v) ? v : 0;
  return new Intl.NumberFormat('cs-CZ', { minimumFractionDigits: 0, maximumFractionDigits: 0 }).format(n);
};

const sum = (rows: AdvanceAmounts[], key: keyof AdvanceAmounts) => rows.reduce((s, r) => s + r[key], 0);

/**
 * Monthly advances per house. The recommendation is the house's share of costs from the ledger over the
 * last 12 months ÷ 12; Admin can override it per house (the override map is saved as a whole).
 */
export function AdvancesPage() {
  const { user } = useAuth();
  const isAdmin = user?.role === 'Admin';

  const { data: settings, loading: sLoading, refetch: refetchSettings } = useApi<AdvanceSettingsData>(
    useCallback(() => getAdvanceSettings(), []),
  );
  const { data: calc, loading: cLoading, error: cError, refetch: refetchCalc } = useApi<AdvanceCalculation>(
    useCallback(() => calculateAdvances(), []),
  );

  const [msg, setMsg] = useState<{ type: 'ok' | 'err'; text: string } | null>(null);
  const savingRef = useRef(false);

  const [editingHouse, setEditingHouse] = useState<string | null>(null);
  const [houseForm, setHouseForm] = useState<{ water: string; elec: string; common: string }>({ water: '', elec: '', common: '' });

  const saveOverrides = async (houseOverrides: Record<string, HouseAdvanceOverride>, okText: string) => {
    if (savingRef.current) return;
    savingRef.current = true;
    setMsg(null);
    try {
      await updateAdvanceSettings({ houseOverrides });
      setMsg({ type: 'ok', text: okText });
      setEditingHouse(null);
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
    setMsg(null);
  };

  const saveHouseOverride = () => {
    if (!settings || !editingHouse) return;
    const override: HouseAdvanceOverride = {
      waterAdvance: parseCzechNumber(houseForm.water),
      electricityAdvance: parseCzechNumber(houseForm.elec),
      commonAdvance: parseCzechNumber(houseForm.common),
    };
    if (override.waterAdvance < 0 || override.electricityAdvance < 0 || override.commonAdvance < 0) {
      setMsg({ type: 'err', text: 'Záloha nesmí být záporná.' });
      return;
    }
    void saveOverrides({ ...settings.houseOverrides, [editingHouse]: override }, 'Záloha domu uložena.');
  };

  const resetHouseOverride = (houseId: string) => {
    if (!settings) return;
    const next = { ...settings.houseOverrides };
    delete next[houseId];
    void saveOverrides(next, 'Záloha domu vrácena na doporučenou.');
  };

  if (sLoading || cLoading) return <div className="flex justify-center p-12"><Spinner size="lg" /></div>;

  const rows = calc?.houses ?? [];
  const editInput = (value: string, onChange: (v: string) => void, label: string) => (
    <input type="text" inputMode="decimal" aria-label={label} value={value} onChange={(e) => onChange(e.target.value)}
      className="w-20 border border-border rounded-lg px-1 py-0.5 text-right text-sm bg-surface-raised" />
  );

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-bold text-text-primary">Zálohy</h1>
        <p className="mt-1 text-sm text-text-secondary">Měsíční zálohy pro jednotlivé domácnosti — voda, elektřina a společné náklady</p>
      </div>

      {msg && (
        <div className={`rounded-xl border p-3 ${msg.type === 'ok' ? 'border-success/20 bg-success-light' : 'border-danger/20 bg-danger-light'}`}>
          <p className={`text-sm ${msg.type === 'ok' ? 'text-success' : 'text-danger'}`}>{msg.text}</p>
        </div>
      )}

      {cError && <div className="rounded-xl bg-danger-light p-4 text-sm text-danger">{cError}</div>}

      {calc && (
        <div className="bg-surface-raised border border-border rounded-2xl overflow-hidden shadow-card">
          <div className="px-6 py-4 border-b border-border">
            <h2 className="text-lg font-semibold">Přehled záloh za jednotlivé domy</h2>
            <p className="text-sm text-text-secondary mt-1">
              Období: {formatIsoDay(calc.from)} – {formatIsoDay(calc.to)} ({calc.months} měsíců)
            </p>
            <p className="text-xs text-text-muted mt-0.5">
              Doporučená měsíční záloha = náklady, které saldo domu přiřadilo domu za posledních {calc.months} měsíců, děleno {calc.months},
              zaokrouhleno na celé koruny. Voda zahrnuje i podíl na ztrátách, elektřina jsou složky elektřiny, společné je vše ostatní.
              {isAdmin && ' U každého domu můžete doporučenou zálohu přepsat vlastní hodnotou.'}
            </p>
          </div>
          <div className="overflow-x-auto">
            <table className="w-full text-sm" aria-label="Zálohy domů">
              <thead>
                <tr className="bg-surface-sunken border-b border-border text-xs text-text-muted uppercase">
                  <th className="text-left px-4 py-3">Domácnost</th>
                  <th className="text-center px-2 py-3 border-l border-border" colSpan={4}>Náklady za období Kč</th>
                  <th className="text-right px-3 py-3 border-l border-border">Doporučeno Kč/měs.</th>
                  <th className="text-center px-2 py-3 bg-success-light border-l border-border" colSpan={4}>Skutečná záloha Kč/měs.</th>
                  {isAdmin && <th className="px-2 py-3"></th>}
                </tr>
                <tr className="bg-surface-sunken border-b border-border text-[10px] text-text-muted">
                  <th></th>
                  <th className="px-2 py-1 border-l border-border text-right">Voda</th>
                  <th className="px-2 py-1 text-right">Elektřina</th>
                  <th className="px-2 py-1 text-right">Společné</th>
                  <th className="px-2 py-1 text-right">Celkem</th>
                  <th className="px-3 py-1 border-l border-border text-right">Celkem</th>
                  <th className="px-2 py-1 bg-success-light border-l border-border text-right">Voda</th>
                  <th className="px-2 py-1 bg-success-light text-right">Elektřina</th>
                  <th className="px-2 py-1 bg-success-light text-right">Společné</th>
                  <th className="px-2 py-1 bg-success-light text-right">Celkem</th>
                  {isAdmin && <th></th>}
                </tr>
              </thead>
              <tbody>
                {rows.map((h) => {
                  const editing = editingHouse === h.houseId;
                  return (
                    <tr key={h.houseId} className="border-b border-border hover:bg-surface-sunken/50">
                      <td className="px-4 py-3">
                        <span className="font-medium">{h.houseName}</span>
                        {h.hasOverride && (
                          <span className="ml-2 rounded bg-warning-light px-1.5 py-0.5 text-[10px] font-medium text-warning">upraveno</span>
                        )}
                      </td>
                      <td className="px-2 py-3 text-right font-mono border-l border-border">{fmt(h.costsInPeriod.water)}</td>
                      <td className="px-2 py-3 text-right font-mono">{fmt(h.costsInPeriod.electricity)}</td>
                      <td className="px-2 py-3 text-right font-mono">{fmt(h.costsInPeriod.common)}</td>
                      <td className="px-2 py-3 text-right font-mono font-semibold">{fmt(h.costsInPeriod.total)}</td>
                      <td
                        className="px-3 py-3 text-right font-mono border-l border-border text-text-secondary"
                        title={`Voda ${fmt(h.recommended.water)} + elektřina ${fmt(h.recommended.electricity)} + společné ${fmt(h.recommended.common)} Kč`}
                      >
                        {fmt(h.recommended.total)}
                      </td>
                      <td className="px-2 py-3 text-right font-mono bg-success-light/40 border-l border-border">
                        {editing ? editInput(houseForm.water, (v) => setHouseForm({ ...houseForm, water: v }), `Voda ${h.houseName}`) : fmt(h.actual.water)}
                      </td>
                      <td className="px-2 py-3 text-right font-mono bg-success-light/40">
                        {editing ? editInput(houseForm.elec, (v) => setHouseForm({ ...houseForm, elec: v }), `Elektřina ${h.houseName}`) : fmt(h.actual.electricity)}
                      </td>
                      <td className="px-2 py-3 text-right font-mono bg-success-light/40">
                        {editing ? editInput(houseForm.common, (v) => setHouseForm({ ...houseForm, common: v }), `Společné ${h.houseName}`) : fmt(h.actual.common)}
                      </td>
                      <td className="px-2 py-3 text-right font-mono font-bold bg-success-light/40 text-success">{fmt(h.actual.total)}</td>
                      {isAdmin && (
                        <td className="px-2 py-3 text-right">
                          {editing ? (
                            <div className="flex gap-1">
                              <button onClick={saveHouseOverride} className="text-xs bg-accent text-white px-2 py-1 rounded-lg hover:bg-accent-hover">Uložit</button>
                              <button onClick={() => setEditingHouse(null)} className="text-xs text-text-muted hover:text-text-secondary">Zrušit</button>
                            </div>
                          ) : (
                            <div className="flex gap-2">
                              <button onClick={() => startHouseEdit(h.houseId)} className="text-xs text-accent hover:text-accent-hover">Upravit</button>
                              {h.hasOverride && (
                                <button onClick={() => resetHouseOverride(h.houseId)} className="text-xs text-text-muted hover:text-danger">Zrušit úpravu</button>
                              )}
                            </div>
                          )}
                        </td>
                      )}
                    </tr>
                  );
                })}

                {rows.length === 0 && (
                  <tr>
                    <td colSpan={isAdmin ? 11 : 10} className="px-4 py-8 text-center text-text-muted">Žádné domy k zobrazení.</td>
                  </tr>
                )}

                {rows.length > 1 && (
                  <tr className="bg-surface-sunken font-semibold border-t-2">
                    <td className="px-4 py-3">Celkem</td>
                    <td className="px-2 py-3 text-right font-mono border-l border-border">{fmt(sum(rows.map((h) => h.costsInPeriod), 'water'))}</td>
                    <td className="px-2 py-3 text-right font-mono">{fmt(sum(rows.map((h) => h.costsInPeriod), 'electricity'))}</td>
                    <td className="px-2 py-3 text-right font-mono">{fmt(sum(rows.map((h) => h.costsInPeriod), 'common'))}</td>
                    <td className="px-2 py-3 text-right font-mono">{fmt(sum(rows.map((h) => h.costsInPeriod), 'total'))}</td>
                    <td className="px-3 py-3 text-right font-mono border-l border-border">{fmt(sum(rows.map((h) => h.recommended), 'total'))}</td>
                    <td className="px-2 py-3 text-right font-mono bg-success-light/40 border-l border-border">{fmt(sum(rows.map((h) => h.actual), 'water'))}</td>
                    <td className="px-2 py-3 text-right font-mono bg-success-light/40">{fmt(sum(rows.map((h) => h.actual), 'electricity'))}</td>
                    <td className="px-2 py-3 text-right font-mono bg-success-light/40">{fmt(sum(rows.map((h) => h.actual), 'common'))}</td>
                    <td className="px-2 py-3 text-right font-mono font-bold bg-success-light/40 text-success">{fmt(sum(rows.map((h) => h.actual), 'total'))}</td>
                    {isAdmin && <td></td>}
                  </tr>
                )}
              </tbody>
            </table>
          </div>
        </div>
      )}
    </div>
  );
}
