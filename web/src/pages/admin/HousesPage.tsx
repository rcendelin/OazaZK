import { useState, useCallback, useRef } from 'react';
import { useApi } from '../../hooks/useApi';
import { getHouses, createHouse, updateHouse } from '../../api/houses';
import { Spinner } from '../../components/Spinner';
import type { House } from '../../types/index';

interface HouseFormData {
  name: string;
  address: string;
  contactPerson: string;
  email: string;
}

const emptyForm: HouseFormData = { name: '', address: '', contactPerson: '', email: '' };

export function HousesPage() {
  const { data: houses, loading, error, refetch } = useApi<House[]>(
    useCallback(() => getHouses(), []),
  );

  const [showCreate, setShowCreate] = useState(false);
  const [createForm, setCreateForm] = useState<HouseFormData>(emptyForm);
  const [createError, setCreateError] = useState<string | null>(null);
  const submittingRef = useRef(false);

  const [editId, setEditId] = useState<string | null>(null);
  const [editForm, setEditForm] = useState<HouseFormData & { isActive: boolean }>({ ...emptyForm, isActive: true });
  const [editError, setEditError] = useState<string | null>(null);

  const handleCreate = async () => {
    if (submittingRef.current) return;
    if (!createForm.name.trim() || !createForm.email.trim()) {
      setCreateError('Název a email jsou povinné.');
      return;
    }
    submittingRef.current = true;
    setCreateError(null);
    try {
      await createHouse(createForm);
      setCreateForm(emptyForm);
      setShowCreate(false);
      refetch();
    } catch (err) {
      setCreateError(err instanceof Error ? err.message : 'Vytvoření selhalo.');
    } finally {
      submittingRef.current = false;
    }
  };

  const startEdit = (house: House) => {
    setEditId(house.id);
    setEditForm({
      name: house.name,
      address: house.address,
      contactPerson: house.contactPerson,
      email: house.email,
      isActive: house.isActive,
    });
    setEditError(null);
  };

  const handleUpdate = async () => {
    if (!editId || submittingRef.current) return;
    submittingRef.current = true;
    setEditError(null);
    try {
      await updateHouse(editId, editForm);
      setEditId(null);
      refetch();
    } catch (err) {
      setEditError(err instanceof Error ? err.message : 'Uložení selhalo.');
    } finally {
      submittingRef.current = false;
    }
  };

  const toggleActive = async (house: House) => {
    if (submittingRef.current) return;
    submittingRef.current = true;
    try {
      await updateHouse(house.id, {
        name: house.name,
        address: house.address,
        contactPerson: house.contactPerson,
        email: house.email,
        isActive: !house.isActive,
      });
      refetch();
    } catch {
      // silent
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
          <h1 className="text-2xl font-bold text-gray-900">Správa domácností</h1>
          <p className="text-sm text-gray-500 mt-1">Přehled a správa domácností v Oáze</p>
        </div>
        <button
          onClick={() => setShowCreate(!showCreate)}
          className="bg-blue-600 text-white px-4 py-2 rounded-lg hover:bg-blue-700 text-sm font-medium"
        >
          {showCreate ? 'Zrušit' : 'Přidat domácnost'}
        </button>
      </div>

      {showCreate && (
        <div className="bg-white border rounded-lg p-6 mb-6">
          <h2 className="text-lg font-semibold mb-4">Nová domácnost</h2>
          <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Název *</label>
              <input type="text" value={createForm.name}
                onChange={(e) => setCreateForm({ ...createForm, name: e.target.value })}
                placeholder="např. Novákovi (150)"
                className="w-full border rounded-lg px-3 py-2 text-sm focus:ring-2 focus:ring-blue-500 focus:border-blue-500" />
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Email *</label>
              <input type="email" value={createForm.email}
                onChange={(e) => setCreateForm({ ...createForm, email: e.target.value })}
                placeholder="novak@example.cz"
                className="w-full border rounded-lg px-3 py-2 text-sm focus:ring-2 focus:ring-blue-500 focus:border-blue-500" />
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Adresa</label>
              <input type="text" value={createForm.address}
                onChange={(e) => setCreateForm({ ...createForm, address: e.target.value })}
                placeholder="Zadní Kopanina 150, Praha 5"
                className="w-full border rounded-lg px-3 py-2 text-sm focus:ring-2 focus:ring-blue-500 focus:border-blue-500" />
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Kontaktní osoba</label>
              <input type="text" value={createForm.contactPerson}
                onChange={(e) => setCreateForm({ ...createForm, contactPerson: e.target.value })}
                placeholder="Jan Novák"
                className="w-full border rounded-lg px-3 py-2 text-sm focus:ring-2 focus:ring-blue-500 focus:border-blue-500" />
            </div>
          </div>
          {createError && <p className="text-sm text-red-600 mt-3">{createError}</p>}
          <div className="mt-4 flex gap-2">
            <button onClick={handleCreate}
              className="bg-blue-600 text-white px-4 py-2 rounded-lg hover:bg-blue-700 text-sm font-medium">Vytvořit</button>
            <button onClick={() => { setShowCreate(false); setCreateForm(emptyForm); setCreateError(null); }}
              className="bg-gray-100 text-gray-700 px-4 py-2 rounded-lg hover:bg-gray-200 text-sm">Zrušit</button>
          </div>
        </div>
      )}

      <div className="bg-white border rounded-lg overflow-hidden">
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead>
              <tr className="bg-gray-50 border-b">
                <th className="text-left px-4 py-3 font-medium text-gray-700">Název</th>
                <th className="text-left px-4 py-3 font-medium text-gray-700">Adresa</th>
                <th className="text-left px-4 py-3 font-medium text-gray-700">Kontaktní osoba</th>
                <th className="text-left px-4 py-3 font-medium text-gray-700">Email</th>
                <th className="text-left px-4 py-3 font-medium text-gray-700">Stav</th>
                <th className="text-right px-4 py-3 font-medium text-gray-700">Akce</th>
              </tr>
            </thead>
            <tbody>
              {houses?.map((house) =>
                editId === house.id ? (
                  <tr key={house.id} className="border-b bg-blue-50">
                    <td className="px-4 py-2"><input type="text" value={editForm.name} onChange={(e) => setEditForm({ ...editForm, name: e.target.value })} className="w-full border rounded px-2 py-1 text-sm" /></td>
                    <td className="px-4 py-2"><input type="text" value={editForm.address} onChange={(e) => setEditForm({ ...editForm, address: e.target.value })} className="w-full border rounded px-2 py-1 text-sm" /></td>
                    <td className="px-4 py-2"><input type="text" value={editForm.contactPerson} onChange={(e) => setEditForm({ ...editForm, contactPerson: e.target.value })} className="w-full border rounded px-2 py-1 text-sm" /></td>
                    <td className="px-4 py-2"><input type="email" value={editForm.email} onChange={(e) => setEditForm({ ...editForm, email: e.target.value })} className="w-full border rounded px-2 py-1 text-sm" /></td>
                    <td className="px-4 py-2">
                      <select value={editForm.isActive ? 'true' : 'false'} onChange={(e) => setEditForm({ ...editForm, isActive: e.target.value === 'true' })} className="border rounded px-2 py-1 text-sm">
                        <option value="true">Aktivní</option>
                        <option value="false">Neaktivní</option>
                      </select>
                    </td>
                    <td className="px-4 py-2 text-right">
                      <div className="flex gap-1 justify-end">
                        <button onClick={handleUpdate} className="bg-blue-600 text-white px-3 py-1 rounded text-xs hover:bg-blue-700">Uložit</button>
                        <button onClick={() => setEditId(null)} className="bg-gray-100 text-gray-700 px-3 py-1 rounded text-xs hover:bg-gray-200">Zrušit</button>
                      </div>
                      {editError && <p className="text-xs text-red-600 mt-1">{editError}</p>}
                    </td>
                  </tr>
                ) : (
                  <tr key={house.id} className="border-b hover:bg-gray-50">
                    <td className="px-4 py-3 font-medium">{house.name}</td>
                    <td className="px-4 py-3 text-gray-600">{house.address}</td>
                    <td className="px-4 py-3 text-gray-600">{house.contactPerson}</td>
                    <td className="px-4 py-3 text-gray-600">{house.email}</td>
                    <td className="px-4 py-3">
                      <span className={`inline-block px-2 py-0.5 rounded-full text-xs font-medium ${house.isActive ? 'bg-green-100 text-green-700' : 'bg-gray-100 text-gray-500'}`}>
                        {house.isActive ? 'Aktivní' : 'Neaktivní'}
                      </span>
                    </td>
                    <td className="px-4 py-3 text-right">
                      <div className="flex gap-1 justify-end">
                        <button onClick={() => startEdit(house)} className="text-blue-600 hover:text-blue-800 text-xs font-medium">Upravit</button>
                        <button onClick={() => toggleActive(house)} className={`text-xs font-medium ${house.isActive ? 'text-amber-600 hover:text-amber-800' : 'text-green-600 hover:text-green-800'}`}>
                          {house.isActive ? 'Deaktivovat' : 'Aktivovat'}
                        </button>
                      </div>
                    </td>
                  </tr>
                ),
              )}
              {(!houses || houses.length === 0) && (
                <tr><td colSpan={6} className="px-4 py-8 text-center text-gray-400">Žádné domácnosti</td></tr>
              )}
            </tbody>
          </table>
        </div>
      </div>
      <p className="text-xs text-gray-400 mt-4">Celkem domácností: {houses?.length ?? 0}</p>
    </div>
  );
}
