import { useState, useCallback, useRef } from 'react';
import { useApi } from '../../hooks/useApi';
import { getUsers, createUser, updateUser } from '../../api/users';
import { getHouses } from '../../api/houses';
import { Spinner } from '../../components/Spinner';
import type { User, House, UserRole, AuthMethod } from '../../types/index';

const czDate = new Intl.DateTimeFormat('cs-CZ', { dateStyle: 'medium', timeStyle: 'short' });

const roleBadge: Record<UserRole, { label: string; cls: string }> = {
  Admin: { label: 'Admin', cls: 'bg-red-100 text-red-700' },
  Member: { label: 'Člen', cls: 'bg-blue-100 text-blue-700' },
  Accountant: { label: 'Účetní', cls: 'bg-green-100 text-green-700' },
};

const authMethodLabel: Record<AuthMethod, string> = {
  EntraId: 'Microsoft',
  MagicLink: 'Magic link',
};

interface CreateFormData {
  name: string;
  email: string;
  role: UserRole;
  houseId: string;
  authMethod: AuthMethod;
}

const emptyCreateForm: CreateFormData = {
  name: '', email: '', role: 'Member', houseId: '', authMethod: 'MagicLink',
};

interface EditFormData {
  name: string;
  role: UserRole;
  houseId: string;
  notificationsEnabled: boolean;
}

export function UsersPage() {
  const { data: users, loading, error, refetch } = useApi<User[]>(
    useCallback(() => getUsers(), []),
  );
  const { data: houses } = useApi<House[]>(
    useCallback(() => getHouses(), []),
  );

  const [showCreate, setShowCreate] = useState(false);
  const [createForm, setCreateForm] = useState<CreateFormData>(emptyCreateForm);
  const [createError, setCreateError] = useState<string | null>(null);
  const submittingRef = useRef(false);

  const [editId, setEditId] = useState<string | null>(null);
  const [editForm, setEditForm] = useState<EditFormData>({ name: '', role: 'Member', houseId: '', notificationsEnabled: true });
  const [editError, setEditError] = useState<string | null>(null);

  const houseName = (houseId: string | null): string => {
    if (!houseId || !houses) return '—';
    return houses.find((h) => h.id === houseId)?.name ?? '—';
  };

  const handleCreate = async () => {
    if (submittingRef.current) return;
    if (!createForm.name.trim() || !createForm.email.trim()) {
      setCreateError('Jméno a email jsou povinné.');
      return;
    }
    submittingRef.current = true;
    setCreateError(null);
    try {
      await createUser({
        name: createForm.name,
        email: createForm.email,
        role: createForm.role,
        houseId: createForm.houseId || null,
        authMethod: createForm.authMethod,
      });
      setCreateForm(emptyCreateForm);
      setShowCreate(false);
      refetch();
    } catch (err) {
      setCreateError(err instanceof Error ? err.message : 'Vytvoření selhalo.');
    } finally {
      submittingRef.current = false;
    }
  };

  const startEdit = (user: User) => {
    setEditId(user.id);
    setEditForm({
      name: user.name,
      role: user.role,
      houseId: user.houseId ?? '',
      notificationsEnabled: user.notificationsEnabled,
    });
    setEditError(null);
  };

  const handleUpdate = async () => {
    if (!editId || submittingRef.current) return;
    submittingRef.current = true;
    setEditError(null);
    try {
      await updateUser(editId, {
        name: editForm.name,
        role: editForm.role,
        houseId: editForm.houseId || null,
        notificationsEnabled: editForm.notificationsEnabled,
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
          <h1 className="text-2xl font-bold text-gray-900">Správa uživatelů</h1>
          <p className="text-sm text-gray-500 mt-1">Přehled a správa uživatelů portálu</p>
        </div>
        <button
          onClick={() => setShowCreate(!showCreate)}
          className="bg-blue-600 text-white px-4 py-2 rounded-lg hover:bg-blue-700 text-sm font-medium"
        >
          {showCreate ? 'Zrušit' : 'Pozvat uživatele'}
        </button>
      </div>

      {showCreate && (
        <div className="bg-white border rounded-lg p-6 mb-6">
          <h2 className="text-lg font-semibold mb-4">Nový uživatel</h2>
          <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Jméno *</label>
              <input type="text" value={createForm.name}
                onChange={(e) => setCreateForm({ ...createForm, name: e.target.value })}
                placeholder="Jan Novák"
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
              <label className="block text-sm font-medium text-gray-700 mb-1">Role</label>
              <select value={createForm.role}
                onChange={(e) => setCreateForm({ ...createForm, role: e.target.value as UserRole })}
                className="w-full border rounded-lg px-3 py-2 text-sm focus:ring-2 focus:ring-blue-500 focus:border-blue-500">
                <option value="Member">Člen</option>
                <option value="Accountant">Účetní</option>
                <option value="Admin">Admin</option>
              </select>
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Domácnost</label>
              <select value={createForm.houseId}
                onChange={(e) => setCreateForm({ ...createForm, houseId: e.target.value })}
                className="w-full border rounded-lg px-3 py-2 text-sm focus:ring-2 focus:ring-blue-500 focus:border-blue-500">
                <option value="">— nepřiřazeno —</option>
                {houses?.filter((h) => h.isActive).map((h) => (
                  <option key={h.id} value={h.id}>{h.name}</option>
                ))}
              </select>
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Způsob přihlášení</label>
              <select value={createForm.authMethod}
                onChange={(e) => setCreateForm({ ...createForm, authMethod: e.target.value as AuthMethod })}
                className="w-full border rounded-lg px-3 py-2 text-sm focus:ring-2 focus:ring-blue-500 focus:border-blue-500">
                <option value="MagicLink">Magic link (email)</option>
                <option value="EntraId">Microsoft (Entra ID)</option>
              </select>
            </div>
          </div>
          {createError && <p className="text-sm text-red-600 mt-3">{createError}</p>}
          <div className="mt-4 flex gap-2">
            <button onClick={handleCreate}
              className="bg-blue-600 text-white px-4 py-2 rounded-lg hover:bg-blue-700 text-sm font-medium">Pozvat</button>
            <button onClick={() => { setShowCreate(false); setCreateForm(emptyCreateForm); setCreateError(null); }}
              className="bg-gray-100 text-gray-700 px-4 py-2 rounded-lg hover:bg-gray-200 text-sm">Zrušit</button>
          </div>
        </div>
      )}

      <div className="bg-white border rounded-lg overflow-hidden">
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead>
              <tr className="bg-gray-50 border-b">
                <th className="text-left px-4 py-3 font-medium text-gray-700">Jméno</th>
                <th className="text-left px-4 py-3 font-medium text-gray-700">Email</th>
                <th className="text-left px-4 py-3 font-medium text-gray-700">Role</th>
                <th className="text-left px-4 py-3 font-medium text-gray-700">Domácnost</th>
                <th className="text-left px-4 py-3 font-medium text-gray-700">Auth</th>
                <th className="text-left px-4 py-3 font-medium text-gray-700">Poslední přihlášení</th>
                <th className="text-right px-4 py-3 font-medium text-gray-700">Akce</th>
              </tr>
            </thead>
            <tbody>
              {users?.map((user) =>
                editId === user.id ? (
                  <tr key={user.id} className="border-b bg-blue-50">
                    <td className="px-4 py-2">
                      <input type="text" value={editForm.name} onChange={(e) => setEditForm({ ...editForm, name: e.target.value })}
                        className="w-full border rounded px-2 py-1 text-sm" />
                    </td>
                    <td className="px-4 py-2 text-gray-500">{user.email}</td>
                    <td className="px-4 py-2">
                      <select value={editForm.role} onChange={(e) => setEditForm({ ...editForm, role: e.target.value as UserRole })}
                        className="border rounded px-2 py-1 text-sm">
                        <option value="Member">Člen</option>
                        <option value="Accountant">Účetní</option>
                        <option value="Admin">Admin</option>
                      </select>
                    </td>
                    <td className="px-4 py-2">
                      <select value={editForm.houseId} onChange={(e) => setEditForm({ ...editForm, houseId: e.target.value })}
                        className="border rounded px-2 py-1 text-sm">
                        <option value="">— nepřiřazeno —</option>
                        {houses?.filter((h) => h.isActive).map((h) => (
                          <option key={h.id} value={h.id}>{h.name}</option>
                        ))}
                      </select>
                    </td>
                    <td className="px-4 py-2 text-gray-500">{authMethodLabel[user.authMethod]}</td>
                    <td className="px-4 py-2">
                      <label className="flex items-center gap-2 text-xs">
                        <input type="checkbox" checked={editForm.notificationsEnabled}
                          onChange={(e) => setEditForm({ ...editForm, notificationsEnabled: e.target.checked })}
                          className="rounded" />
                        Notifikace
                      </label>
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
                  <tr key={user.id} className="border-b hover:bg-gray-50">
                    <td className="px-4 py-3 font-medium">{user.name}</td>
                    <td className="px-4 py-3 text-gray-600">{user.email}</td>
                    <td className="px-4 py-3">
                      <span className={`inline-block px-2 py-0.5 rounded-full text-xs font-medium ${roleBadge[user.role].cls}`}>
                        {roleBadge[user.role].label}
                      </span>
                    </td>
                    <td className="px-4 py-3 text-gray-600">{houseName(user.houseId)}</td>
                    <td className="px-4 py-3 text-gray-500 text-xs">{authMethodLabel[user.authMethod]}</td>
                    <td className="px-4 py-3 text-gray-500 text-xs">
                      {user.lastLogin ? czDate.format(new Date(user.lastLogin)) : '—'}
                    </td>
                    <td className="px-4 py-3 text-right">
                      <button onClick={() => startEdit(user)} className="text-blue-600 hover:text-blue-800 text-xs font-medium">Upravit</button>
                    </td>
                  </tr>
                ),
              )}
              {(!users || users.length === 0) && (
                <tr><td colSpan={7} className="px-4 py-8 text-center text-gray-400">Žádní uživatelé</td></tr>
              )}
            </tbody>
          </table>
        </div>
      </div>
      <p className="text-xs text-gray-400 mt-4">Celkem uživatelů: {users?.length ?? 0}</p>
    </div>
  );
}
