import React, { useState, useCallback, useRef } from 'react';
import { useApi } from '../../hooks/useApi';
import { getHouses, createHouse, updateHouse } from '../../api/houses';
import { getUsers } from '../../api/users';
import { Spinner } from '../../components/Spinner';
import type { House, User } from '../../types/index';

interface HouseFormData {
  name: string;
  address: string;
  contactPerson: string;
  email: string;
}

const emptyForm: HouseFormData = { name: '', address: '', contactPerson: '', email: '' };

const roleBadge: Record<string, { label: string; cls: string }> = {
  Admin: { label: 'Admin', cls: 'bg-danger-light text-danger' },
  Member: { label: 'Člen', cls: 'bg-accent-light text-accent' },
  Accountant: { label: 'Účetní', cls: 'bg-success-light text-success' },
};

export function HousesPage() {
  const { data: houses, loading, error, refetch } = useApi<House[]>(
    useCallback(() => getHouses(), []),
  );
  const { data: users } = useApi<User[]>(
    useCallback(() => getUsers(), []),
  );

  const [expandedId, setExpandedId] = useState<string | null>(null);

  const usersForHouse = (houseId: string): User[] =>
    users?.filter((u) => u.houseId === houseId) ?? [];

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
  if (error) return <div className="bg-danger-light text-danger p-4 rounded-2xl">{error}</div>;

  return (
    <div>
      <div className="flex items-center justify-between mb-6">
        <div>
          <h1 className="text-2xl font-bold text-text-primary">Správa domácností</h1>
          <p className="text-sm text-text-muted mt-1">Přehled a správa domácností v Oáze</p>
        </div>
        <button
          onClick={() => setShowCreate(!showCreate)}
          className="bg-accent text-white px-4 py-2 rounded-xl hover:bg-accent-hover text-sm font-medium"
        >
          {showCreate ? 'Zrušit' : 'Přidat domácnost'}
        </button>
      </div>

      {showCreate && (
        <div className="bg-surface-raised border border-border rounded-2xl p-6 mb-6 shadow-card">
          <h2 className="text-lg font-semibold mb-4">Nová domácnost</h2>
          <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
            <div>
              <label className="block text-sm font-medium text-text-secondary mb-1">Název *</label>
              <input type="text" value={createForm.name}
                onChange={(e) => setCreateForm({ ...createForm, name: e.target.value })}
                placeholder="např. Novákovi (150)"
                className="w-full border border-border rounded-xl px-3 py-2 text-sm bg-surface-raised focus:border-accent focus:ring-2 focus:ring-accent/20" />
            </div>
            <div>
              <label className="block text-sm font-medium text-text-secondary mb-1">Email *</label>
              <input type="email" value={createForm.email}
                onChange={(e) => setCreateForm({ ...createForm, email: e.target.value })}
                placeholder="novak@example.cz"
                className="w-full border border-border rounded-xl px-3 py-2 text-sm bg-surface-raised focus:border-accent focus:ring-2 focus:ring-accent/20" />
            </div>
            <div>
              <label className="block text-sm font-medium text-text-secondary mb-1">Adresa</label>
              <input type="text" value={createForm.address}
                onChange={(e) => setCreateForm({ ...createForm, address: e.target.value })}
                placeholder="Zadní Kopanina 150, Praha 5"
                className="w-full border border-border rounded-xl px-3 py-2 text-sm bg-surface-raised focus:border-accent focus:ring-2 focus:ring-accent/20" />
            </div>
            <div>
              <label className="block text-sm font-medium text-text-secondary mb-1">Kontaktní osoba</label>
              <input type="text" value={createForm.contactPerson}
                onChange={(e) => setCreateForm({ ...createForm, contactPerson: e.target.value })}
                placeholder="Jan Novák"
                className="w-full border border-border rounded-xl px-3 py-2 text-sm bg-surface-raised focus:border-accent focus:ring-2 focus:ring-accent/20" />
            </div>
          </div>
          {createError && <p className="text-sm text-danger mt-3">{createError}</p>}
          <div className="mt-4 flex gap-2">
            <button onClick={handleCreate}
              className="bg-accent text-white px-4 py-2 rounded-xl hover:bg-accent-hover text-sm font-medium">Vytvořit</button>
            <button onClick={() => { setShowCreate(false); setCreateForm(emptyForm); setCreateError(null); }}
              className="bg-surface-sunken text-text-secondary px-4 py-2 rounded-xl hover:bg-surface-sunken text-sm">Zrušit</button>
          </div>
        </div>
      )}

      <div className="bg-surface-raised border border-border rounded-2xl overflow-hidden shadow-card">
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead>
              <tr className="bg-surface-sunken border-b border-border">
                <th className="text-left px-4 py-3 text-xs font-semibold uppercase tracking-wider text-text-muted">Název</th>
                <th className="text-left px-4 py-3 text-xs font-semibold uppercase tracking-wider text-text-muted">Adresa</th>
                <th className="text-left px-4 py-3 text-xs font-semibold uppercase tracking-wider text-text-muted">Kontaktní osoba</th>
                <th className="text-left px-4 py-3 text-xs font-semibold uppercase tracking-wider text-text-muted">Email</th>
                <th className="text-left px-4 py-3 text-xs font-semibold uppercase tracking-wider text-text-muted">Členové</th>
                <th className="text-left px-4 py-3 text-xs font-semibold uppercase tracking-wider text-text-muted">Stav</th>
                <th className="text-right px-4 py-3 text-xs font-semibold uppercase tracking-wider text-text-muted">Akce</th>
              </tr>
            </thead>
            <tbody>
              {houses?.map((house) =>
                editId === house.id ? (
                  <tr key={`edit-${house.id}`} className="border-b border-border bg-accent-light">
                    <td className="px-4 py-2"><input type="text" value={editForm.name} onChange={(e) => setEditForm({ ...editForm, name: e.target.value })} className="w-full border border-border rounded-xl px-2 py-1 text-sm bg-surface-raised" /></td>
                    <td className="px-4 py-2"><input type="text" value={editForm.address} onChange={(e) => setEditForm({ ...editForm, address: e.target.value })} className="w-full border border-border rounded-xl px-2 py-1 text-sm bg-surface-raised" /></td>
                    <td className="px-4 py-2"><input type="text" value={editForm.contactPerson} onChange={(e) => setEditForm({ ...editForm, contactPerson: e.target.value })} className="w-full border border-border rounded-xl px-2 py-1 text-sm bg-surface-raised" /></td>
                    <td className="px-4 py-2"><input type="email" value={editForm.email} onChange={(e) => setEditForm({ ...editForm, email: e.target.value })} className="w-full border border-border rounded-xl px-2 py-1 text-sm bg-surface-raised" /></td>
                    <td className="px-4 py-2 text-text-muted text-xs">{usersForHouse(house.id).length}</td>
                    <td className="px-4 py-2">
                      <select value={editForm.isActive ? 'true' : 'false'} onChange={(e) => setEditForm({ ...editForm, isActive: e.target.value === 'true' })} className="border border-border rounded-xl px-2 py-1 text-sm bg-surface-raised">
                        <option value="true">Aktivní</option>
                        <option value="false">Neaktivní</option>
                      </select>
                    </td>
                    <td className="px-4 py-2 text-right">
                      <div className="flex gap-1 justify-end">
                        <button onClick={handleUpdate} className="bg-accent text-white px-3 py-1 rounded-xl text-xs hover:bg-accent-hover">Uložit</button>
                        <button onClick={() => setEditId(null)} className="bg-surface-sunken text-text-secondary px-3 py-1 rounded-xl text-xs hover:bg-surface-sunken">Zrušit</button>
                      </div>
                      {editError && <p className="text-xs text-danger mt-1">{editError}</p>}
                    </td>
                  </tr>
                ) : (
                  <React.Fragment key={house.id}>
                  <tr className="border-b border-border hover:bg-surface-sunken/50">
                    <td className="px-4 py-3 font-medium">{house.name}</td>
                    <td className="px-4 py-3 text-text-secondary">{house.address}</td>
                    <td className="px-4 py-3 text-text-secondary">{house.contactPerson}</td>
                    <td className="px-4 py-3 text-text-secondary">{house.email}</td>
                    <td className="px-4 py-3">
                      <button
                        onClick={() => setExpandedId(expandedId === house.id ? null : house.id)}
                        className="text-accent hover:text-accent-hover text-xs font-medium"
                      >
                        {usersForHouse(house.id).length} {usersForHouse(house.id).length === 1 ? 'uživatel' : usersForHouse(house.id).length >= 2 && usersForHouse(house.id).length <= 4 ? 'uživatelé' : 'uživatelů'}
                        {expandedId === house.id ? ' ▲' : ' ▼'}
                      </button>
                    </td>
                    <td className="px-4 py-3">
                      <span className={`inline-block px-2 py-0.5 rounded-full text-xs font-medium ${house.isActive ? 'bg-success-light text-success' : 'bg-surface-sunken text-text-muted'}`}>
                        {house.isActive ? 'Aktivní' : 'Neaktivní'}
                      </span>
                    </td>
                    <td className="px-4 py-3 text-right">
                      <div className="flex gap-1 justify-end">
                        <button onClick={() => startEdit(house)} className="text-accent hover:text-accent-hover text-xs font-medium">Upravit</button>
                        <button onClick={() => toggleActive(house)} className={`text-xs font-medium ${house.isActive ? 'text-warning hover:text-warning' : 'text-success hover:text-emerald-600'}`}>
                          {house.isActive ? 'Deaktivovat' : 'Aktivovat'}
                        </button>
                      </div>
                    </td>
                  </tr>
                  {expandedId === house.id && (
                    <tr key={`${house.id}-members`} className="bg-surface-sunken">
                      <td colSpan={7} className="px-6 py-3">
                        <p className="text-xs font-semibold text-text-muted mb-2">Členové domácnosti {house.name}</p>
                        {usersForHouse(house.id).length === 0 ? (
                          <p className="text-xs text-text-muted">Žádní přiřazení uživatelé</p>
                        ) : (
                          <div className="flex flex-wrap gap-3">
                            {usersForHouse(house.id).map((u) => (
                              <div key={u.id} className="flex items-center gap-2 bg-surface-raised rounded-xl px-3 py-2 border border-border text-xs">
                                <div className="w-6 h-6 rounded-full bg-accent-light text-accent flex items-center justify-center font-medium text-xs">
                                  {u.name.charAt(0).toUpperCase()}
                                </div>
                                <div>
                                  <span className="font-medium">{u.name}</span>
                                  <span className="text-text-muted ml-1">({u.email})</span>
                                </div>
                                <span className={`px-1.5 py-0.5 rounded-full text-xs ${roleBadge[u.role]?.cls ?? 'bg-surface-sunken text-text-secondary'}`}>
                                  {roleBadge[u.role]?.label ?? u.role}
                                </span>
                              </div>
                            ))}
                          </div>
                        )}
                      </td>
                    </tr>
                  )}
                  </React.Fragment>
                ),
              )}
              {(!houses || houses.length === 0) && (
                <tr><td colSpan={7} className="px-4 py-8 text-center text-text-muted">Žádné domácnosti</td></tr>
              )}
            </tbody>
          </table>
        </div>
      </div>
      <p className="text-xs text-text-muted mt-4">Celkem domácností: {houses?.length ?? 0}</p>
    </div>
  );
}
