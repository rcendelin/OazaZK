import { useState, useCallback, useRef } from 'react';
import { useApi } from '../../hooks/useApi';
import { getMeters, createMeter, updateMeter } from '../../api/meters';
import { getHouses } from '../../api/houses';
import { Spinner } from '../../components/Spinner';
import type { WaterMeter, House, MeterType } from '../../types/index';

const czDate = new Intl.DateTimeFormat('cs-CZ');

interface CreateFormData {
  meterNumber: string;
  name: string;
  type: MeterType;
  houseId: string;
}

const emptyCreate: CreateFormData = { meterNumber: '', name: '', type: 'Individual', houseId: '' };

interface EditFormData {
  meterNumber: string;
  name: string;
  houseId: string;
}

export function MetersPage() {
  const { data: meters, loading, error, refetch } = useApi<WaterMeter[]>(
    useCallback(() => getMeters(), []),
  );
  const { data: houses } = useApi<House[]>(
    useCallback(() => getHouses(), []),
  );

  const [showCreate, setShowCreate] = useState(false);
  const [createForm, setCreateForm] = useState<CreateFormData>(emptyCreate);
  const [createError, setCreateError] = useState<string | null>(null);
  const submittingRef = useRef(false);

  const [editId, setEditId] = useState<string | null>(null);
  const [editForm, setEditForm] = useState<EditFormData>({ meterNumber: '', name: '', houseId: '' });
  const [editError, setEditError] = useState<string | null>(null);

  const mainMeter = meters?.find((m) => m.type === 'Main');
  const individualMeters = meters?.filter((m) => m.type === 'Individual') ?? [];

  const handleCreate = async () => {
    if (submittingRef.current) return;
    if (!createForm.meterNumber.trim() || !createForm.name.trim()) {
      setCreateError('Identifikátor a název jsou povinné.');
      return;
    }
    submittingRef.current = true;
    setCreateError(null);
    try {
      await createMeter({
        meterNumber: createForm.meterNumber,
        name: createForm.name,
        type: createForm.type,
        houseId: createForm.houseId || null,
      });
      setCreateForm(emptyCreate);
      setShowCreate(false);
      refetch();
    } catch (err) {
      setCreateError(err instanceof Error ? err.message : 'Vytvoření selhalo.');
    } finally {
      submittingRef.current = false;
    }
  };

  const startEdit = (meter: WaterMeter) => {
    setEditId(meter.id);
    setEditForm({
      meterNumber: meter.meterNumber,
      name: meter.name,
      houseId: meter.houseId ?? '',
    });
    setEditError(null);
  };

  const handleUpdate = async () => {
    if (!editId || submittingRef.current) return;
    submittingRef.current = true;
    setEditError(null);
    try {
      await updateMeter(editId, {
        meterNumber: editForm.meterNumber,
        name: editForm.name,
        houseId: editForm.houseId || null,
      });
      setEditId(null);
      refetch();
    } catch (err) {
      setEditError(err instanceof Error ? err.message : 'Uložení selhalo.');
    } finally {
      submittingRef.current = false;
    }
  };

  if (loading) return <div className="flex justify-center p-12"><Spinner size="lg" /></div>;
  if (error) return <div className="bg-danger-light text-danger p-4 rounded-2xl">{error}</div>;

  return (
    <div>
      <div className="flex items-center justify-between mb-6">
        <div>
          <h1 className="text-2xl font-bold text-text-primary">Správa vodoměrů</h1>
          <p className="text-sm text-text-muted mt-1">Evidence vodoměrů — identifikátor se používá v importním souboru</p>
        </div>
        <button onClick={() => setShowCreate(!showCreate)}
          className="bg-accent text-white px-4 py-2 rounded-xl hover:bg-accent-hover text-sm font-medium">
          {showCreate ? 'Zrušit' : 'Přidat vodoměr'}
        </button>
      </div>

      {showCreate && (
        <div className="bg-surface-raised border border-border rounded-2xl p-6 mb-6 shadow-card">
          <h2 className="text-lg font-semibold mb-4">Nový vodoměr</h2>
          <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
            <div>
              <label className="block text-sm font-medium text-text-secondary mb-1">Identifikátor *</label>
              <input type="text" value={createForm.meterNumber}
                onChange={(e) => setCreateForm({ ...createForm, meterNumber: e.target.value })}
                placeholder="např. HV-001 nebo DV-001"
                className="w-full border border-border rounded-xl px-3 py-2 text-sm bg-surface-raised focus:border-accent focus:ring-2 focus:ring-accent/20" />
              <p className="text-xs text-text-muted mt-1">Tento identifikátor se použije jako záhlaví sloupce v importním Excel souboru</p>
            </div>
            <div>
              <label className="block text-sm font-medium text-text-secondary mb-1">Název *</label>
              <input type="text" value={createForm.name}
                onChange={(e) => setCreateForm({ ...createForm, name: e.target.value })}
                placeholder="např. Hlavní vodoměr nebo Vodoměr Novákovi"
                className="w-full border border-border rounded-xl px-3 py-2 text-sm bg-surface-raised focus:border-accent focus:ring-2 focus:ring-accent/20" />
            </div>
            <div>
              <label className="block text-sm font-medium text-text-secondary mb-1">Typ</label>
              <select value={createForm.type}
                onChange={(e) => setCreateForm({ ...createForm, type: e.target.value as MeterType, houseId: e.target.value === 'Main' ? '' : createForm.houseId })}
                className="w-full border border-border rounded-xl px-3 py-2 text-sm bg-surface-raised focus:border-accent focus:ring-2 focus:ring-accent/20">
                <option value="Individual">Individuální (domácnost)</option>
                <option value="Main">Hlavní (vstupní)</option>
              </select>
            </div>
            {createForm.type === 'Individual' && (
              <div>
                <label className="block text-sm font-medium text-text-secondary mb-1">Domácnost</label>
                <select value={createForm.houseId}
                  onChange={(e) => setCreateForm({ ...createForm, houseId: e.target.value })}
                  className="w-full border border-border rounded-xl px-3 py-2 text-sm bg-surface-raised focus:border-accent focus:ring-2 focus:ring-accent/20">
                  <option value="">— vyberte —</option>
                  {houses?.filter((h) => h.isActive).map((h) => (
                    <option key={h.id} value={h.id}>{h.name}</option>
                  ))}
                </select>
              </div>
            )}
          </div>
          {createError && <p className="text-sm text-danger mt-3">{createError}</p>}
          <div className="mt-4 flex gap-2">
            <button onClick={handleCreate}
              className="bg-accent text-white px-4 py-2 rounded-xl hover:bg-accent-hover text-sm font-medium">Vytvořit</button>
            <button onClick={() => { setShowCreate(false); setCreateForm(emptyCreate); setCreateError(null); }}
              className="bg-surface-sunken text-text-secondary px-4 py-2 rounded-xl hover:bg-surface-sunken text-sm">Zrušit</button>
          </div>
        </div>
      )}

      {/* Main meter */}
      {mainMeter && (
        <div className="bg-accent-light border border-accent/20 rounded-2xl p-4 mb-6">
          <div className="flex items-center justify-between">
            <div>
              <span className="inline-block px-2 py-0.5 rounded-full text-xs font-medium bg-accent-light text-accent mb-1">Hlavní vodoměr (vstupní)</span>
              <h3 className="font-semibold">{mainMeter.name || mainMeter.meterNumber}</h3>
              <p className="text-sm text-text-secondary">Identifikátor: <code className="bg-surface-raised px-1 rounded">{mainMeter.meterNumber}</code></p>
              <p className="text-xs text-text-muted mt-1">Instalace: {czDate.format(new Date(mainMeter.installationDate))}</p>
            </div>
            <button onClick={() => startEdit(mainMeter)} className="text-accent hover:text-accent-hover text-xs font-medium">Upravit</button>
          </div>
          {editId === mainMeter.id && (
            <div className="mt-3 pt-3 border-t border-accent/20 grid grid-cols-1 md:grid-cols-2 gap-3">
              <div>
                <label className="block text-xs font-medium text-text-secondary mb-1">Identifikátor</label>
                <input type="text" value={editForm.meterNumber} onChange={(e) => setEditForm({ ...editForm, meterNumber: e.target.value })}
                  className="w-full border border-border rounded-xl px-2 py-1 text-sm bg-surface-raised" />
              </div>
              <div>
                <label className="block text-xs font-medium text-text-secondary mb-1">Název</label>
                <input type="text" value={editForm.name} onChange={(e) => setEditForm({ ...editForm, name: e.target.value })}
                  className="w-full border border-border rounded-xl px-2 py-1 text-sm bg-surface-raised" />
              </div>
              <div className="md:col-span-2 flex gap-2">
                <button onClick={handleUpdate} className="bg-accent text-white px-3 py-1 rounded-xl text-xs hover:bg-accent-hover">Uložit</button>
                <button onClick={() => setEditId(null)} className="bg-surface-sunken text-text-secondary px-3 py-1 rounded-xl text-xs hover:bg-surface-sunken">Zrušit</button>
                {editError && <span className="text-xs text-danger">{editError}</span>}
              </div>
            </div>
          )}
        </div>
      )}

      {/* Individual meters */}
      <h2 className="text-lg font-semibold mb-3">Individuální vodoměry ({individualMeters.length})</h2>
      <div className="bg-surface-raised border border-border rounded-2xl overflow-hidden shadow-card">
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead>
              <tr className="bg-surface-sunken border-b border-border">
                <th className="text-left px-4 py-3 text-xs font-semibold uppercase tracking-wider text-text-muted">Identifikátor</th>
                <th className="text-left px-4 py-3 text-xs font-semibold uppercase tracking-wider text-text-muted">Název</th>
                <th className="text-left px-4 py-3 text-xs font-semibold uppercase tracking-wider text-text-muted">Domácnost</th>
                <th className="text-left px-4 py-3 text-xs font-semibold uppercase tracking-wider text-text-muted">Instalace</th>
                <th className="text-right px-4 py-3 text-xs font-semibold uppercase tracking-wider text-text-muted">Akce</th>
              </tr>
            </thead>
            <tbody>
              {individualMeters.map((meter) =>
                editId === meter.id ? (
                  <tr key={meter.id} className="border-b border-border bg-accent-light">
                    <td className="px-4 py-2"><input type="text" value={editForm.meterNumber} onChange={(e) => setEditForm({ ...editForm, meterNumber: e.target.value })} className="w-full border border-border rounded-xl px-2 py-1 text-sm bg-surface-raised" /></td>
                    <td className="px-4 py-2"><input type="text" value={editForm.name} onChange={(e) => setEditForm({ ...editForm, name: e.target.value })} className="w-full border border-border rounded-xl px-2 py-1 text-sm bg-surface-raised" /></td>
                    <td className="px-4 py-2">
                      <select value={editForm.houseId} onChange={(e) => setEditForm({ ...editForm, houseId: e.target.value })} className="border border-border rounded-xl px-2 py-1 text-sm bg-surface-raised">
                        <option value="">— nepřiřazeno —</option>
                        {houses?.filter((h) => h.isActive).map((h) => (
                          <option key={h.id} value={h.id}>{h.name}</option>
                        ))}
                      </select>
                    </td>
                    <td className="px-4 py-2 text-text-muted text-xs">{czDate.format(new Date(meter.installationDate))}</td>
                    <td className="px-4 py-2 text-right">
                      <div className="flex gap-1 justify-end">
                        <button onClick={handleUpdate} className="bg-accent text-white px-3 py-1 rounded-xl text-xs hover:bg-accent-hover">Uložit</button>
                        <button onClick={() => setEditId(null)} className="bg-surface-sunken text-text-secondary px-3 py-1 rounded-xl text-xs hover:bg-surface-sunken">Zrušit</button>
                      </div>
                      {editError && <p className="text-xs text-danger mt-1">{editError}</p>}
                    </td>
                  </tr>
                ) : (
                  <tr key={meter.id} className="border-b border-border hover:bg-surface-sunken/50">
                    <td className="px-4 py-3"><code className="bg-surface-sunken px-1.5 py-0.5 rounded text-xs">{meter.meterNumber}</code></td>
                    <td className="px-4 py-3 font-medium">{meter.name}</td>
                    <td className="px-4 py-3 text-text-secondary">{meter.houseName ?? <span className="text-text-muted">— nepřiřazeno —</span>}</td>
                    <td className="px-4 py-3 text-text-muted text-xs">{czDate.format(new Date(meter.installationDate))}</td>
                    <td className="px-4 py-3 text-right">
                      <button onClick={() => startEdit(meter)} className="text-accent hover:text-accent-hover text-xs font-medium">Upravit</button>
                    </td>
                  </tr>
                ),
              )}
              {individualMeters.length === 0 && (
                <tr><td colSpan={5} className="px-4 py-8 text-center text-text-muted">Žádné individuální vodoměry</td></tr>
              )}
            </tbody>
          </table>
        </div>
      </div>

      <div className="mt-6 p-4 bg-warning-light border border-warning/20 rounded-xl">
        <p className="text-sm text-warning font-medium">Jak funguje import odečtů</p>
        <p className="text-xs text-warning mt-1">
          V importním Excel souboru použijte identifikátory vodoměrů jako záhlaví sloupců (řádek 1).
          Sloupec A = datum odečtu, další sloupce = stavy jednotlivých vodoměrů.
          Identifikátory se automaticky napárují na vodoměry v systému.
        </p>
      </div>
    </div>
  );
}
