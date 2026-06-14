import { Fragment, useCallback, useRef, useState } from 'react';
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
  type InvoiceLineItemInput,
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
const num = (s: string) => {
  const v = parseFloat(s.replace(/\s/g, '').replace(',', '.'));
  return isNaN(v) ? 0 : v;
};
const dec = (n: number) => String(n).replace('.', ',');

interface LineRow {
  dateFrom: string;
  dateTo: string;
  startReading: string;
  endReading: string;
  consumptionM3: string;
  unitPrice: string;
  amountExclVat: string;
}

const emptyLine: LineRow = {
  dateFrom: '', dateTo: '', startReading: '', endReading: '', consumptionM3: '', unitPrice: '', amountExclVat: '',
};

export function InvoicesSection() {
  const { getAccessToken } = useAuth();
  const currentYear = new Date().getFullYear();
  const [year, setYear] = useState(currentYear);

  const fetchInvoices = useCallback(() => getInvoices(year), [year]);
  const { data: invoices, loading, error, refetch } = useApi<SupplierInvoice[]>(fetchInvoices, [year]);

  const [showForm, setShowForm] = useState(false);
  const [editId, setEditId] = useState<string | null>(null);
  const [invoiceNumber, setInvoiceNumber] = useState('');
  const [vatRate, setVatRate] = useState('12');
  const [issuedDate, setIssuedDate] = useState('');
  const [dueDate, setDueDate] = useState('');
  const [lines, setLines] = useState<LineRow[]>([{ ...emptyLine }]);
  const [file, setFile] = useState<File | null>(null);
  const [formError, setFormError] = useState<string | null>(null);
  const [deleteTarget, setDeleteTarget] = useState<SupplierInvoice | null>(null);
  const [expandedId, setExpandedId] = useState<string | null>(null);
  const savingRef = useRef(false);

  const resetForm = () => {
    setEditId(null);
    setInvoiceNumber('');
    setVatRate('12');
    setIssuedDate('');
    setDueDate('');
    setLines([{ ...emptyLine }]);
    setFile(null);
    setFormError(null);
  };

  const startCreate = () => { resetForm(); setShowForm(true); };

  const startEdit = (inv: SupplierInvoice) => {
    setEditId(inv.id);
    setInvoiceNumber(inv.invoiceNumber);
    setVatRate(dec(inv.vatRatePercent));
    setIssuedDate(inv.issuedDate.split('T')[0]);
    setDueDate(inv.dueDate.split('T')[0]);
    setLines(
      inv.lineItems.length
        ? inv.lineItems.map((l) => ({
            dateFrom: l.dateFrom.split('T')[0],
            dateTo: l.dateTo.split('T')[0],
            startReading: dec(l.startReading),
            endReading: dec(l.endReading),
            consumptionM3: dec(l.consumptionM3),
            unitPrice: dec(l.unitPrice),
            amountExclVat: dec(l.amountExclVat),
          }))
        : [{ ...emptyLine }],
    );
    setFile(null);
    setFormError(null);
    setShowForm(true);
  };

  const setLine = (i: number, patch: Partial<LineRow>) =>
    setLines((prev) => prev.map((l, idx) => (idx === i ? { ...l, ...patch } : l)));
  const addLine = () => setLines((prev) => [...prev, { ...emptyLine }]);
  const removeLine = (i: number) => setLines((prev) => (prev.length > 1 ? prev.filter((_, idx) => idx !== i) : prev));

  const vat = num(vatRate);
  const totalExcl = lines.reduce((s, l) => s + num(l.amountExclVat), 0);
  const totalIncl = totalExcl * (1 + vat / 100);

  const handleSave = async () => {
    if (savingRef.current) return;
    if (!invoiceNumber.trim()) { setFormError('Zadejte číslo faktury.'); return; }
    const valid = lines.filter((l) => l.dateFrom && l.dateTo);
    if (valid.length === 0) { setFormError('Přidejte alespoň jeden řádek s obdobím od–do.'); return; }
    savingRef.current = true;
    setFormError(null);
    const lineItems: InvoiceLineItemInput[] = valid.map((l) => ({
      dateFrom: l.dateFrom,
      dateTo: l.dateTo,
      startReading: num(l.startReading),
      endReading: num(l.endReading),
      consumptionM3: num(l.consumptionM3),
      unitPrice: num(l.unitPrice),
      amountExclVat: num(l.amountExclVat),
    }));
    const payload: InvoiceInput = {
      invoiceNumber: invoiceNumber.trim(),
      issuedDate: issuedDate || valid[0].dateFrom,
      dueDate: dueDate || valid[valid.length - 1].dateTo,
      vatRatePercent: vat,
      lineItems,
    };
    try {
      const saved = editId ? await updateInvoice(editId, payload) : await createInvoice(payload);
      if (file) await uploadInvoiceAttachment(saved.id, file, getAccessToken);
      setShowForm(false);
      resetForm();
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
  const years = [currentYear + 1, currentYear, currentYear - 1, currentYear - 2, currentYear - 3];
  const inputCls = 'w-full border border-border rounded-lg px-2 py-1 text-sm bg-surface-raised';

  return (
    <div className="bg-surface-raised border border-border rounded-2xl p-6 shadow-card">
      <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between mb-4">
        <div>
          <h2 className="text-lg font-semibold text-text-primary">Faktury za vodu</h2>
          <p className="text-xs text-text-muted mt-0.5">Jedna faktura = celková částka + více dílčích odečtů (řádků). Do vyúčtování vstupují řádky dle období.</p>
        </div>
        <div className="flex items-center gap-2">
          <select value={year} onChange={(e) => setYear(Number(e.target.value))}
            className="border border-border rounded-xl px-2 py-1.5 text-sm bg-surface-raised">
            {years.map((y) => <option key={y} value={y}>{y}</option>)}
          </select>
          <button onClick={() => (showForm ? setShowForm(false) : startCreate())}
            className="bg-accent text-white px-3 py-1.5 rounded-xl hover:bg-accent-hover text-sm font-medium">
            {showForm ? 'Zavřít' : 'Přidat fakturu'}
          </button>
        </div>
      </div>

      {showForm && (
        <div className="border border-border rounded-xl p-4 mb-4 bg-surface-sunken/30">
          {/* Header */}
          <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-3">
            <div>
              <label className="block text-xs font-medium text-text-secondary mb-1">Číslo faktury</label>
              <input type="text" value={invoiceNumber} onChange={(e) => setInvoiceNumber(e.target.value)} className={inputCls} />
            </div>
            <div>
              <label className="block text-xs font-medium text-text-secondary mb-1">DPH (%)</label>
              <input type="text" inputMode="decimal" value={vatRate} onChange={(e) => setVatRate(e.target.value)} className={`${inputCls} text-right`} />
            </div>
            <div>
              <label className="block text-xs font-medium text-text-secondary mb-1">Vystaveno</label>
              <input type="date" value={issuedDate} onChange={(e) => setIssuedDate(e.target.value)} className={inputCls} />
            </div>
            <div>
              <label className="block text-xs font-medium text-text-secondary mb-1">Splatnost</label>
              <input type="date" value={dueDate} onChange={(e) => setDueDate(e.target.value)} className={inputCls} />
            </div>
          </div>

          {/* Line items */}
          <div className="mt-4">
            <div className="flex items-center justify-between mb-1">
              <label className="block text-xs font-medium text-text-secondary">Dílčí odečty (řádky)</label>
              <button onClick={addLine} className="text-xs font-medium text-accent hover:text-accent-hover">+ Přidat řádek</button>
            </div>
            <div className="overflow-x-auto">
              <table className="text-xs">
                <thead>
                  <tr className="text-text-muted">
                    <th className="px-1 py-1 text-left font-medium">Od</th>
                    <th className="px-1 py-1 text-left font-medium">Do</th>
                    <th className="px-1 py-1 text-right font-medium">Poč. stav</th>
                    <th className="px-1 py-1 text-right font-medium">Kon. stav</th>
                    <th className="px-1 py-1 text-right font-medium">Spotřeba m³</th>
                    <th className="px-1 py-1 text-right font-medium">Kč/m³</th>
                    <th className="px-1 py-1 text-right font-medium">Cena bez DPH</th>
                    <th></th>
                  </tr>
                </thead>
                <tbody>
                  {lines.map((l, i) => (
                    <tr key={i}>
                      <td className="px-1 py-0.5"><input type="date" value={l.dateFrom} onChange={(e) => setLine(i, { dateFrom: e.target.value })} className="border border-border rounded-lg px-1 py-1 bg-surface-raised" /></td>
                      <td className="px-1 py-0.5"><input type="date" value={l.dateTo} onChange={(e) => setLine(i, { dateTo: e.target.value })} className="border border-border rounded-lg px-1 py-1 bg-surface-raised" /></td>
                      <td className="px-1 py-0.5"><input type="text" inputMode="decimal" value={l.startReading} onChange={(e) => setLine(i, { startReading: e.target.value })} className="w-20 border border-border rounded-lg px-1 py-1 text-right bg-surface-raised" /></td>
                      <td className="px-1 py-0.5"><input type="text" inputMode="decimal" value={l.endReading} onChange={(e) => setLine(i, { endReading: e.target.value })} className="w-20 border border-border rounded-lg px-1 py-1 text-right bg-surface-raised" /></td>
                      <td className="px-1 py-0.5"><input type="text" inputMode="decimal" value={l.consumptionM3} onChange={(e) => setLine(i, { consumptionM3: e.target.value })} className="w-20 border border-border rounded-lg px-1 py-1 text-right bg-surface-raised" /></td>
                      <td className="px-1 py-0.5"><input type="text" inputMode="decimal" value={l.unitPrice} onChange={(e) => setLine(i, { unitPrice: e.target.value })} className="w-16 border border-border rounded-lg px-1 py-1 text-right bg-surface-raised" /></td>
                      <td className="px-1 py-0.5"><input type="text" inputMode="decimal" value={l.amountExclVat} onChange={(e) => setLine(i, { amountExclVat: e.target.value })} className="w-24 border border-border rounded-lg px-1 py-1 text-right bg-surface-raised" /></td>
                      <td className="px-1 py-0.5">
                        <button onClick={() => removeLine(i)} disabled={lines.length === 1} className="text-text-muted hover:text-danger disabled:opacity-30 text-xs">×</button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            <p className="mt-2 text-sm text-text-secondary">
              Celkem bez DPH: <strong>{CZK(totalExcl)} Kč</strong> · DPH {dec(vat)} % · <span className="text-text-primary">Celkem vč. DPH: <strong>{CZK(totalIncl)} Kč</strong></span>
            </p>
          </div>

          <div className="mt-3">
            <label className="block text-xs font-medium text-text-secondary mb-1">
              Příloha (PDF){editId ? ' — nahráním nahradíte stávající' : ' — volitelné'}
            </label>
            <input type="file" accept="application/pdf" onChange={(e) => setFile(e.target.files?.[0] ?? null)}
              className="block w-full text-sm text-text-secondary file:mr-3 file:rounded-xl file:border-0 file:bg-accent file:px-3 file:py-1.5 file:text-white" />
          </div>

          {formError && <p className="text-sm text-danger mt-3">{formError}</p>}
          <div className="mt-4 flex gap-2">
            <button onClick={handleSave} className="bg-accent text-white px-4 py-2 rounded-xl hover:bg-accent-hover text-sm font-medium">
              {editId ? 'Uložit změny' : 'Uložit fakturu'}
            </button>
            <button onClick={() => { setShowForm(false); resetForm(); }} className="bg-surface-sunken text-text-secondary px-4 py-2 rounded-xl text-sm hover:bg-surface-sunken">Zrušit</button>
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
                <th className="text-left px-3 py-2">Číslo</th>
                <th className="text-right px-3 py-2">Řádků</th>
                <th className="text-right px-3 py-2">Spotřeba</th>
                <th className="text-right px-3 py-2">Celkem vč. DPH</th>
                <th className="text-left px-3 py-2">Příloha</th>
                <th className="text-right px-3 py-2">Akce</th>
              </tr>
            </thead>
            <tbody>
              {list.map((inv) => (
                <Fragment key={inv.id}>
                  <tr className="border-b border-border hover:bg-surface-sunken/40">
                    <td className="px-3 py-2">
                      <button onClick={() => setExpandedId(expandedId === inv.id ? null : inv.id)} className="font-medium text-text-primary hover:text-accent">
                        {inv.invoiceNumber} {inv.lineItems.length > 0 && <span className="text-text-muted">{expandedId === inv.id ? '▾' : '▸'}</span>}
                      </button>
                    </td>
                    <td className="px-3 py-2 text-right text-text-secondary">{inv.lineItems.length}</td>
                    <td className="px-3 py-2 text-right font-mono text-text-secondary">{M3(inv.consumptionM3)} m³</td>
                    <td className="px-3 py-2 text-right font-mono font-semibold">{CZK(inv.amount)} Kč</td>
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
                  {expandedId === inv.id && inv.lineItems.length > 0 && (
                    <tr className="bg-surface-sunken/30">
                      <td colSpan={6} className="px-3 py-2">
                        <table className="w-full text-xs">
                          <thead>
                            <tr className="text-text-muted">
                              <th className="text-left px-2 py-1">Období</th>
                              <th className="text-right px-2 py-1">Poč.</th>
                              <th className="text-right px-2 py-1">Kon.</th>
                              <th className="text-right px-2 py-1">Spotřeba</th>
                              <th className="text-right px-2 py-1">Kč/m³</th>
                              <th className="text-right px-2 py-1">Cena bez DPH</th>
                            </tr>
                          </thead>
                          <tbody>
                            {inv.lineItems.map((l, idx) => (
                              <tr key={idx} className="border-t border-border/60">
                                <td className="px-2 py-1">{fmtDate(l.dateFrom)} – {fmtDate(l.dateTo)}</td>
                                <td className="px-2 py-1 text-right font-mono">{M3(l.startReading)}</td>
                                <td className="px-2 py-1 text-right font-mono">{M3(l.endReading)}</td>
                                <td className="px-2 py-1 text-right font-mono">{M3(l.consumptionM3)}</td>
                                <td className="px-2 py-1 text-right font-mono">{CZK(l.unitPrice)}</td>
                                <td className="px-2 py-1 text-right font-mono">{CZK(l.amountExclVat)} Kč</td>
                              </tr>
                            ))}
                          </tbody>
                        </table>
                      </td>
                    </tr>
                  )}
                </Fragment>
              ))}
              {list.length === 0 && (
                <tr><td colSpan={6} className="px-3 py-8 text-center text-text-muted">Za rok {year} zatím nejsou žádné faktury.</td></tr>
              )}
            </tbody>
            {list.length > 0 && (
              <tfoot>
                <tr className="border-t-2 border-border font-semibold bg-surface-sunken/40">
                  <td className="px-3 py-2" colSpan={3}>Celkem {year}</td>
                  <td className="px-3 py-2 text-right font-mono">{CZK(total)} Kč</td>
                  <td colSpan={2}></td>
                </tr>
              </tfoot>
            )}
          </table>
        </div>
      )}

      <ConfirmDialog
        isOpen={deleteTarget !== null}
        title="Smazat fakturu"
        message={deleteTarget ? `Opravdu smazat fakturu ${deleteTarget.invoiceNumber} (vč. všech řádků)?` : ''}
        confirmLabel="Smazat"
        confirmVariant="danger"
        onConfirm={() => void handleDelete()}
        onCancel={() => setDeleteTarget(null)}
      />
    </div>
  );
}
