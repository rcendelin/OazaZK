import { useCallback, useRef, useState } from 'react';
import { useAuth } from '../auth/AuthContext';
import { useApi } from '../hooks/useApi';
import {
  getInvoices,
  createInvoice,
  updateInvoice,
  deleteInvoice,
  uploadInvoiceAttachment,
  downloadInvoiceAttachment,
  type InvoiceInput,
} from '../api/invoices';
import { ConfirmDialog } from './ConfirmDialog';
import { Spinner } from './Spinner';
import type { SupplierInvoice } from '../types';

const CZK = (v: number) =>
  new Intl.NumberFormat('cs-CZ', { minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(v);
const M3 = (v: number) =>
  new Intl.NumberFormat('cs-CZ', { minimumFractionDigits: 1, maximumFractionDigits: 1 }).format(v);
const fmtDate = (s: string) => {
  try {
    return new Intl.DateTimeFormat('cs-CZ').format(new Date(s));
  } catch {
    return '—';
  }
};
const MONTHS = [
  'Leden', 'Únor', 'Březen', 'Duben', 'Květen', 'Červen',
  'Červenec', 'Srpen', 'Září', 'Říjen', 'Listopad', 'Prosinec',
];

interface FormState {
  month: number;
  invoiceNumber: string;
  amount: string;
  consumptionM3: string;
  issuedDate: string;
  dueDate: string;
}

const today = () => new Date().toISOString().split('T')[0];
const emptyForm = (): FormState => ({
  month: new Date().getMonth() + 1,
  invoiceNumber: '',
  amount: '',
  consumptionM3: '',
  issuedDate: today(),
  dueDate: today(),
});

export function InvoicesSection() {
  const { getAccessToken } = useAuth();
  const currentYear = new Date().getFullYear();
  const [year, setYear] = useState(currentYear);

  const fetchInvoices = useCallback(() => getInvoices(year), [year]);
  const { data: invoices, loading, error, refetch } = useApi<SupplierInvoice[]>(fetchInvoices, [year]);

  const [showForm, setShowForm] = useState(false);
  const [editId, setEditId] = useState<string | null>(null);
  const [form, setForm] = useState<FormState>(emptyForm());
  const [file, setFile] = useState<File | null>(null);
  const [formError, setFormError] = useState<string | null>(null);
  const [deleteTarget, setDeleteTarget] = useState<SupplierInvoice | null>(null);
  const savingRef = useRef(false);

  const startCreate = () => {
    setEditId(null);
    setForm(emptyForm());
    setFile(null);
    setFormError(null);
    setShowForm(true);
  };

  const startEdit = (inv: SupplierInvoice) => {
    setEditId(inv.id);
    setForm({
      month: inv.month,
      invoiceNumber: inv.invoiceNumber,
      amount: String(inv.amount).replace('.', ','),
      consumptionM3: String(inv.consumptionM3).replace('.', ','),
      issuedDate: inv.issuedDate.split('T')[0],
      dueDate: inv.dueDate.split('T')[0],
    });
    setFile(null);
    setFormError(null);
    setShowForm(true);
  };

  const handleSave = async () => {
    if (savingRef.current) return;
    const amount = parseFloat(form.amount.replace(',', '.'));
    const consumption = parseFloat(form.consumptionM3.replace(',', '.'));
    if (!form.invoiceNumber.trim()) {
      setFormError('Zadejte číslo faktury.');
      return;
    }
    if (isNaN(amount) || amount < 0) {
      setFormError('Zadejte platnou částku.');
      return;
    }
    if (isNaN(consumption) || consumption < 0) {
      setFormError('Zadejte platnou spotřebu.');
      return;
    }
    savingRef.current = true;
    setFormError(null);
    const payload: InvoiceInput = {
      year,
      month: form.month,
      invoiceNumber: form.invoiceNumber.trim(),
      issuedDate: new Date(form.issuedDate).toISOString(),
      dueDate: new Date(form.dueDate).toISOString(),
      amount,
      consumptionM3: consumption,
    };
    try {
      const saved = editId ? await updateInvoice(editId, payload) : await createInvoice(payload);
      if (file) {
        await uploadInvoiceAttachment(saved.id, file, getAccessToken);
      }
      setShowForm(false);
      setEditId(null);
      setFile(null);
      refetch();
    } catch (err) {
      setFormError(err instanceof Error ? err.message : 'Uložení selhalo.');
    } finally {
      savingRef.current = false;
    }
  };

  const handleDelete = async () => {
    if (!deleteTarget) return;
    try {
      await deleteInvoice(deleteTarget.id);
      setDeleteTarget(null);
      refetch();
    } catch (err) {
      setFormError(err instanceof Error ? err.message : 'Smazání selhalo.');
      setDeleteTarget(null);
    }
  };

  const handleDownload = async (inv: SupplierInvoice) => {
    try {
      await downloadInvoiceAttachment(inv.id, `faktura-${inv.invoiceNumber || inv.id}.pdf`, getAccessToken);
    } catch (err) {
      setFormError(err instanceof Error ? err.message : 'Stahování přílohy selhalo.');
    }
  };

  const list = (invoices ?? []).slice().sort((a, b) => a.month - b.month);
  const total = list.reduce((s, i) => s + i.amount, 0);
  const years = [currentYear + 1, currentYear, currentYear - 1, currentYear - 2];

  return (
    <div className="bg-surface-raised border border-border rounded-2xl p-6 shadow-card">
      <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between mb-4">
        <div>
          <h2 className="text-lg font-semibold text-text-primary">Faktury za vodu</h2>
          <p className="text-xs text-text-muted mt-0.5">Faktury dodavatele — vstupují do vyúčtování příslušného období.</p>
        </div>
        <div className="flex items-center gap-2">
          <select
            value={year}
            onChange={(e) => setYear(Number(e.target.value))}
            className="border border-border rounded-xl px-2 py-1.5 text-sm bg-surface-raised focus:border-accent focus:ring-2 focus:ring-accent/20"
          >
            {years.map((y) => <option key={y} value={y}>{y}</option>)}
          </select>
          <button
            onClick={() => (showForm ? setShowForm(false) : startCreate())}
            className="bg-accent text-white px-3 py-1.5 rounded-xl hover:bg-accent-hover text-sm font-medium"
          >
            {showForm ? 'Zavřít' : 'Přidat fakturu'}
          </button>
        </div>
      </div>

      {showForm && (
        <div className="border border-border rounded-xl p-4 mb-4 bg-surface-sunken/30">
          <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-3">
            <div>
              <label className="block text-xs font-medium text-text-secondary mb-1">Měsíc ({year})</label>
              <select
                value={form.month}
                onChange={(e) => setForm({ ...form, month: Number(e.target.value) })}
                className="w-full border border-border rounded-xl px-2 py-1.5 text-sm bg-surface-raised"
              >
                {MONTHS.map((m, i) => <option key={i} value={i + 1}>{m}</option>)}
              </select>
            </div>
            <div>
              <label className="block text-xs font-medium text-text-secondary mb-1">Číslo faktury</label>
              <input
                type="text"
                value={form.invoiceNumber}
                onChange={(e) => setForm({ ...form, invoiceNumber: e.target.value })}
                className="w-full border border-border rounded-xl px-2 py-1.5 text-sm bg-surface-raised"
              />
            </div>
            <div>
              <label className="block text-xs font-medium text-text-secondary mb-1">Částka (Kč)</label>
              <input
                type="text"
                inputMode="decimal"
                value={form.amount}
                onChange={(e) => setForm({ ...form, amount: e.target.value })}
                placeholder="0,00"
                className="w-full border border-border rounded-xl px-2 py-1.5 text-sm text-right bg-surface-raised"
              />
            </div>
            <div>
              <label className="block text-xs font-medium text-text-secondary mb-1">Spotřeba (m³)</label>
              <input
                type="text"
                inputMode="decimal"
                value={form.consumptionM3}
                onChange={(e) => setForm({ ...form, consumptionM3: e.target.value })}
                placeholder="0,0"
                className="w-full border border-border rounded-xl px-2 py-1.5 text-sm text-right bg-surface-raised"
              />
            </div>
            <div>
              <label className="block text-xs font-medium text-text-secondary mb-1">Vystaveno</label>
              <input
                type="date"
                value={form.issuedDate}
                onChange={(e) => setForm({ ...form, issuedDate: e.target.value })}
                className="w-full border border-border rounded-xl px-2 py-1.5 text-sm bg-surface-raised"
              />
            </div>
            <div>
              <label className="block text-xs font-medium text-text-secondary mb-1">Splatnost</label>
              <input
                type="date"
                value={form.dueDate}
                onChange={(e) => setForm({ ...form, dueDate: e.target.value })}
                className="w-full border border-border rounded-xl px-2 py-1.5 text-sm bg-surface-raised"
              />
            </div>
            <div className="sm:col-span-2 lg:col-span-3">
              <label className="block text-xs font-medium text-text-secondary mb-1">
                Příloha (PDF){editId ? ' — nahráním nahradíte stávající' : ' — volitelné'}
              </label>
              <input
                type="file"
                accept="application/pdf"
                onChange={(e) => setFile(e.target.files?.[0] ?? null)}
                className="block w-full text-sm text-text-secondary file:mr-3 file:rounded-xl file:border-0 file:bg-accent file:px-3 file:py-1.5 file:text-white"
              />
            </div>
          </div>
          {formError && <p className="text-sm text-danger mt-3">{formError}</p>}
          <div className="mt-4 flex gap-2">
            <button onClick={handleSave} className="bg-accent text-white px-4 py-2 rounded-xl hover:bg-accent-hover text-sm font-medium">
              {editId ? 'Uložit změny' : 'Uložit fakturu'}
            </button>
            <button
              onClick={() => { setShowForm(false); setEditId(null); setFormError(null); }}
              className="bg-surface-sunken text-text-secondary px-4 py-2 rounded-xl text-sm hover:bg-surface-sunken"
            >
              Zrušit
            </button>
          </div>
        </div>
      )}

      {loading && <div className="flex justify-center py-8"><Spinner size="lg" /></div>}
      {error && <div className="rounded-xl bg-danger-light p-3"><p className="text-sm text-danger">{error}</p></div>}

      {!loading && !error && (
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead>
              <tr className="bg-surface-sunken text-xs uppercase tracking-wider text-text-muted">
                <th className="text-left px-3 py-2">Měsíc</th>
                <th className="text-left px-3 py-2">Číslo</th>
                <th className="text-right px-3 py-2">Částka</th>
                <th className="text-right px-3 py-2">Spotřeba</th>
                <th className="text-left px-3 py-2">Splatnost</th>
                <th className="text-left px-3 py-2">Příloha</th>
                <th className="text-right px-3 py-2">Akce</th>
              </tr>
            </thead>
            <tbody>
              {list.map((inv) => (
                <tr key={inv.id} className="border-b border-border hover:bg-surface-sunken/40">
                  <td className="px-3 py-2">{MONTHS[inv.month - 1] ?? inv.month}</td>
                  <td className="px-3 py-2">{inv.invoiceNumber}</td>
                  <td className="px-3 py-2 text-right font-mono">{CZK(inv.amount)} Kč</td>
                  <td className="px-3 py-2 text-right font-mono text-text-secondary">{M3(inv.consumptionM3)} m³</td>
                  <td className="px-3 py-2 text-text-secondary">{fmtDate(inv.dueDate)}</td>
                  <td className="px-3 py-2">
                    {inv.attachmentBlobName
                      ? <button onClick={() => void handleDownload(inv)} className="text-accent hover:text-accent-hover text-xs font-medium">Stáhnout PDF</button>
                      : <span className="text-text-muted text-xs">—</span>}
                  </td>
                  <td className="px-3 py-2 text-right">
                    <div className="flex gap-2 justify-end">
                      <button onClick={() => startEdit(inv)} className="text-accent hover:text-accent-hover text-xs font-medium">Upravit</button>
                      <button onClick={() => setDeleteTarget(inv)} className="text-text-muted hover:text-danger text-xs font-medium">Smazat</button>
                    </div>
                  </td>
                </tr>
              ))}
              {list.length === 0 && (
                <tr><td colSpan={7} className="px-3 py-8 text-center text-text-muted">Za rok {year} zatím nejsou žádné faktury.</td></tr>
              )}
            </tbody>
            {list.length > 0 && (
              <tfoot>
                <tr className="border-t-2 border-border font-semibold bg-surface-sunken/40">
                  <td className="px-3 py-2" colSpan={2}>Celkem {year}</td>
                  <td className="px-3 py-2 text-right font-mono">{CZK(total)} Kč</td>
                  <td colSpan={4}></td>
                </tr>
              </tfoot>
            )}
          </table>
        </div>
      )}

      <ConfirmDialog
        isOpen={deleteTarget !== null}
        title="Smazat fakturu"
        message={deleteTarget ? `Opravdu smazat fakturu ${deleteTarget.invoiceNumber}? Příloha bude rovněž odstraněna z evidence.` : ''}
        confirmLabel="Smazat"
        confirmVariant="danger"
        onConfirm={() => void handleDelete()}
        onCancel={() => setDeleteTarget(null)}
      />
    </div>
  );
}
