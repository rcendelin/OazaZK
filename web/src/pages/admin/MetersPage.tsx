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
  if (error) return <div className="bg-red-50 text-red-700 p-4 rounded-lg">{error}</div>;

  return (
    <div>
      <div className="flex items-center justify-between mb-6">
        <div>
          <h1 className="text-2xl font-bold text-gray-900">Správa vodoměrů</h1>
          <p className="text-sm text-gray-500 mt-1">Evidence vodoměrů — identifikátor se používá v importním souboru</p>
        </div>
        <button onClick={() => setShowCreate(!showCreate)}
          className="bg-blue-600 text-white px-4 py-2 rounded-lg hover:bg-blue-700 text-sm font-medium">
          {showCreate ? 'Zrušit' : 'Přidat vodoměr'}
        </button>
      </div>

      {showCreate && (
        <div className="bg-white border rounded-lg p-6 mb-6">
          <h2 className="text-lg font-semibold mb-4">Nový vodoměr</h2>
          <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Identifikátor *</label>
              <input type="text" value={createForm.meterNumber}
                onChange={(e) => setCreateForm({ ...createForm, meterNumber: e.target.value })}
                placeholder="např. HV-001 nebo DV-001"
                className="w-full border rounded-lg px-3 py-2 text-sm focus:ring-2 focus:ring-blue-500 focus:border-blue-500" />
              <p className="text-xs text-gray-400 mt-1">Tento identifikátor se použije jako záhlaví sloupce v importním Excel souboru</p>
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Název *</label>
              <input type="text" value={createForm.name}
                onChange={(e) => setCreateForm({ ...createForm, name: e.target.value })}
                placeholder="např. Hlavní vodoměr nebo Vodoměr Novákovi"
                className="w-full border rounded-lg px-3 py-2 text-sm focus:ring-2 focus:ring-blue-500 focus:border-blue-500" />
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Typ</label>
              <select value={createForm.type}
                onChange={(e) => setCreateForm({ ...createForm, type: e.target.value as MeterType, houseId: e.target.value === 'Main' ? '' : createForm.houseId })}
                className="w-full border rounded-lg px-3 py-2 text-sm focus:ring-2 focus:ring-blue-500 focus:border-blue-500">
                <option value="Individual">Individuální (domácnost)</option>
                <option value="Main">Hlavní (vstupní)</option>
              </select>
            </div>
            {createForm.type === 'Individual' && (
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">Domácnost</label>
                <select value={createForm.houseId}
                  onChange={(e) => setCreateForm({ ...createForm, houseId: e.target.value })}
                  className="w-full border rounded-lg px-3 py-2 text-sm focus:ring-2 focus:ring-blue-500 focus:border-blue-500">
                  <option value="">— vyberte —</option>
                  {houses?.filter((h) => h.isActive).map((h) => (
                    <option key={h.id} value={h.id}>{h.name}</option>
                  ))}
                </select>
              </div>
            )}
          </div>
          {createError && <p className="text-sm text-red-600 mt-3">{createError}</p>}
          <div className="mt-4 flex gap-2">
            <button onClick={handleCreate}
              className="bg-blue-600 text-white px-4 py-2 rounded-lg hover:bg-blue-700 text-sm font-medium">Vytvořit</button>
            <button onClick={() => { setShowCreate(false); setCreateForm(emptyCreate); setCreateError(null); }}
              className="bg-gray-100 text-gray-700 px-4 py-2 rounded-lg hover:bg-gray-200 text-sm">Zrušit</button>
          </div>
        </div>
      )}

      {/* Main meter */}
      {mainMeter && (
        <div className="bg-blue-50 border border-blue-200 rounded-lg p-4 mb-6">
          <div className="flex items-center justify-between">
            <div>
              <span className="inline-block px-2 py-0.5 rounded-full text-xs font-medium bg-blue-200 text-blue-800 mb-1">Hlavní vodoměr (vstupní)</span>
              <h3 className="font-semibold">{mainMeter.name || mainMeter.meterNumber}</h3>
              <p className="text-sm text-gray-600">Identifikátor: <code className="bg-white px-1 rounded">{mainMeter.meterNumber}</code></p>
              <p className="text-xs text-gray-400 mt-1">Instalace: {czDate.format(new Date(mainMeter.installationDate))}</p>
            </div>
            <button onClick={() => startEdit(mainMeter)} className="text-blue-600 hover:text-blue-800 text-xs font-medium">Upravit</button>
          </div>
          {editId === mainMeter.id && (
            <div className="mt-3 pt-3 border-t border-blue-200 grid grid-cols-1 md:grid-cols-2 gap-3">
              <div>
                <label className="block text-xs font-medium text-gray-700 mb-1">Identifikátor</label>
                <input type="text" value={editForm.meterNumber} onChange={(e) => setEditForm({ ...editForm, meterNumber: e.target.value })}
                  className="w-full border rounded px-2 py-1 text-sm" />
              </div>
              <div>
                <label className="block text-xs font-medium text-gray-700 mb-1">Název</label>
                <input type="text" value={editForm.name} onChange={(e) => setEditForm({ ...editForm, name: e.target.value })}
                  className="w-full border rounded px-2 py-1 text-sm" />
              </div>
              <div className="md:col-span-2 flex gap-2">
                <button onClick={handleUpdate} className="bg-blue-600 text-white px-3 py-1 rounded text-xs hover:bg-blue-700">Uložit</button>
                <button onClick={() => setEditId(null)} className="bg-gray-100 text-gray-700 px-3 py-1 rounded text-xs hover:bg-gray-200">Zrušit</button>
                {editError && <span className="text-xs text-red-600">{editError}</span>}
              </div>
            </div>
          )}
        </div>
      )}

      {/* Individual meters */}
      <h2 className="text-lg font-semibold mb-3">Individuální vodoměry ({individualMeters.length})</h2>
      <div className="bg-white border rounded-lg overflow-hidden">
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead>
              <tr className="bg-gray-50 border-b">
                <th className="text-left px-4 py-3 font-medium text-gray-700">Identifikátor</th>
                <th className="text-left px-4 py-3 font-medium text-gray-700">Název</th>
                <th className="text-left px-4 py-3 font-medium text-gray-700">Domácnost</th>
                <th className="text-left px-4 py-3 font-medium text-gray-700">Instalace</th>
                <th className="text-right px-4 py-3 font-medium text-gray-700">Akce</th>
              </tr>
            </thead>
            <tbody>
              {individualMeters.map((meter) =>
                editId === meter.id ? (
                  <tr key={meter.id} className="border-b bg-blue-50">
                    <td className="px-4 py-2"><input type="text" value={editForm.meterNumber} onChange={(e) => setEditForm({ ...editForm, meterNumber: e.target.value })} className="w-full border rounded px-2 py-1 text-sm" /></td>
                    <td className="px-4 py-2"><input type="text" value={editForm.name} onChange={(e) => setEditForm({ ...editForm, name: e.target.value })} className="w-full border rounded px-2 py-1 text-sm" /></td>
                    <td className="px-4 py-2">
                      <select value={editForm.houseId} onChange={(e) => setEditForm({ ...editForm, houseId: e.target.value })} className="border rounded px-2 py-1 text-sm">
                        <option value="">— nepřiřazeno —</option>
                        {houses?.filter((h) => h.isActive).map((h) => (
                          <option key={h.id} value={h.id}>{h.name}</option>
                        ))}
                      </select>
                    </td>
                    <td className="px-4 py-2 text-gray-400 text-xs">{czDate.format(new Date(meter.installationDate))}</td>
                    <td className="px-4 py-2 text-right">
                      <div className="flex gap-1 justify-end">
                        <button onClick={handleUpdate} className="bg-blue-600 text-white px-3 py-1 rounded text-xs hover:bg-blue-700">Uložit</button>
                        <button onClick={() => setEditId(null)} className="bg-gray-100 text-gray-700 px-3 py-1 rounded text-xs hover:bg-gray-200">Zrušit</button>
                      </div>
                      {editError && <p className="text-xs text-red-600 mt-1">{editError}</p>}
                    </td>
                  </tr>
                ) : (
                  <tr key={meter.id} className="border-b hover:bg-gray-50">
                    <td className="px-4 py-3"><code className="bg-gray-100 px-1.5 py-0.5 rounded text-xs">{meter.meterNumber}</code></td>
                    <td className="px-4 py-3 font-medium">{meter.name}</td>
                    <td className="px-4 py-3 text-gray-600">{meter.houseName ?? <span className="text-gray-400">— nepřiřazeno —</span>}</td>
                    <td className="px-4 py-3 text-gray-500 text-xs">{czDate.format(new Date(meter.installationDate))}</td>
                    <td className="px-4 py-3 text-right">
                      <button onClick={() => startEdit(meter)} className="text-blue-600 hover:text-blue-800 text-xs font-medium">Upravit</button>
                    </td>
                  </tr>
                ),
              )}
              {individualMeters.length === 0 && (
                <tr><td colSpan={5} className="px-4 py-8 text-center text-gray-400">Žádné individuální vodoměry</td></tr>
              )}
            </tbody>
          </table>
        </div>
      </div>

      <div className="mt-6 p-4 bg-amber-50 border border-amber-200 rounded-lg">
        <p className="text-sm text-amber-800 font-medium">Jak funguje import odečtů</p>
        <p className="text-xs text-amber-700 mt-1">
          V importním Excel souboru použijte identifikátory vodoměrů jako záhlaví sloupců (řádek 1).
          Sloupec A = datum odečtu, další sloupce = stavy jednotlivých vodoměrů.
          Identifikátory se automaticky napárují na vodoměry v systému.
        </p>
      </div>
    </div>
  );
}
