import { useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import { FileUploadZone } from '../components/FileUploadZone.tsx';
import { Spinner } from '../components/Spinner.tsx';
import { HelpNote } from '../components/help/HelpNote';
import { confirmBankImport, previewBankImport } from '../api/bankImport.ts';
import type {
  BankImportAction,
  BankImportHouse,
  BankImportPaymentType,
  BankImportPreview,
  BankImportRow,
  ConfirmBankImportResult,
} from '../api/bankImport.ts';

const czk = new Intl.NumberFormat('cs-CZ', { style: 'currency', currency: 'CZK', maximumFractionDigits: 2 });
const fmtDate = (iso: string | null) => (iso ? new Intl.DateTimeFormat('cs-CZ').format(new Date(iso)) : '—');
const monthKey = (year: number, month: number) => `${year}-${String(month).padStart(2, '0')}`;

type PageState = 'initial' | 'uploading' | 'preview' | 'confirming' | 'success';

/** A previewed movement plus the admin's decisions. Only `New` rows are editable. */
interface EditableRow extends BankImportRow {
  action: BankImportAction;
  /** True once the admin picked the house by hand (the account gets learned on confirm). */
  manualHouse: boolean;
}

const statusLabel: Record<Exclude<BankImportRow['status'], 'New'>, string> = {
  AlreadyImported: 'Již importováno',
  Ignored: 'Dříve ignorováno',
  Outgoing: 'Odchozí — neimportuje se',
  UnsupportedCurrency: 'Jiná měna — neimportuje se',
};

// ───────────────────── Suggestion logic (mirrors ImportBankStatementUseCase) ─────────────────────

function splitAmount(amount: number, house: BankImportHouse | undefined) {
  if (!house || house.totalAmount <= 0) {
    return { waterAmount: amount, electricityAmount: 0, commonAmount: 0 };
  }
  if (Math.abs(amount - house.totalAmount) < 0.005) {
    return { waterAmount: house.waterAmount, electricityAmount: house.electricityAmount, commonAmount: house.commonAmount };
  }
  const electricityAmount = Math.round((amount * house.electricityAmount) / house.totalAmount);
  const commonAmount = Math.round((amount * house.commonAmount) / house.totalAmount);
  return { waterAmount: round2(amount - electricityAmount - commonAmount), electricityAmount, commonAmount };
}

const round2 = (n: number) => Math.round(n * 100) / 100;

/** Months already taken by a regular advance: stored ones + other rows of this import. */
function takenMonths(rows: EditableRow[], preview: BankImportPreview, houseId: string, exceptId: string): Set<string> {
  const taken = new Set(preview.existingAdvanceMonths[houseId] ?? []);
  for (const r of rows) {
    if (r.transactionId !== exceptId && isImportable(r) && r.action === 'Import' && r.houseId === houseId && r.paymentType === 'Advance') {
      taken.add(monthKey(r.year, r.month));
    }
  }
  return taken;
}

/** Re-suggests type, month and split after the admin changes the house. */
function resuggest(row: EditableRow, rows: EditableRow[], preview: BankImportPreview): EditableRow {
  const date = new Date(row.date);
  const base = { ...row, year: date.getFullYear(), month: date.getMonth() + 1, warnings: [] as string[] };
  const house = preview.houses.find((h) => h.houseId === row.houseId);
  const saysDoplatek = (row.message ?? '').toLowerCase().includes('doplat');

  let paymentType: BankImportPaymentType = 'Doplatek';
  if (house && !saysDoplatek && house.totalAmount > 0) {
    const prescribed = Math.abs(row.amount - house.totalAmount) < 0.005;
    const taken = takenMonths(rows, preview, house.houseId, row.transactionId).has(monthKey(base.year, base.month));
    if (prescribed && !taken) {
      paymentType = 'Advance';
    } else {
      base.warnings.push(prescribed
        ? `Záloha za ${String(base.month).padStart(2, '0')}/${base.year} už existuje — navrženo jako doplatek.`
        : `Neobvyklá částka (předepsaná záloha ${czk.format(house.totalAmount)}) — navrženo jako doplatek.`);
    }
  }

  return { ...base, paymentType, ...splitAmount(row.amount, house) };
}

// ───────────────────── Validation ─────────────────────

const isImportable = (row: BankImportRow) => row.status === 'New';

function rowProblem(row: EditableRow, rows: EditableRow[], preview: BankImportPreview): string | null {
  if (!isImportable(row) || row.action === 'Ignore') return null;
  if (!row.houseId) return 'Vyberte domácnost, nebo platbu ignorujte.';
  const parts = [row.waterAmount, row.electricityAmount, row.commonAmount];
  if (parts.some((p) => Number.isNaN(p) || p < 0)) return 'Složky nesmí být záporné.';
  if (Math.abs(parts.reduce((a, b) => a + b, 0) - row.amount) >= 0.005) {
    return `Součet složek se musí rovnat ${czk.format(row.amount)}.`;
  }
  if (row.paymentType === 'Advance' && takenMonths(rows, preview, row.houseId, row.transactionId).has(monthKey(row.year, row.month))) {
    return `Domácnost už má zálohu za ${String(row.month).padStart(2, '0')}/${row.year}.`;
  }
  return null;
}

// ───────────────────── Page ─────────────────────

export function BankImportPage() {
  const [state, setState] = useState<PageState>('initial');
  const [file, setFile] = useState<File | null>(null);
  const [preview, setPreview] = useState<BankImportPreview | null>(null);
  const [rows, setRows] = useState<EditableRow[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [result, setResult] = useState<ConfirmBankImportResult | null>(null);
  const inFlight = useRef(false);

  const handleUpload = async () => {
    if (!file || inFlight.current) return;
    inFlight.current = true;
    setState('uploading');
    setError(null);
    try {
      const data = await previewBankImport(file);
      setPreview(data);
      setRows(data.rows.map((r) => ({ ...r, action: 'Import', manualHouse: false })));
      setState('preview');
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Načtení výpisu se nezdařilo.');
      setState('initial');
    } finally {
      inFlight.current = false;
    }
  };

  const updateRow = (id: string, change: (row: EditableRow) => EditableRow) => {
    setRows((prev) => prev.map((r) => (r.transactionId === id ? change(r) : r)));
  };

  const changeHouse = (id: string, houseId: string) => {
    if (!preview) return;
    setRows((prev) => prev.map((r) => (r.transactionId === id
      ? resuggest({ ...r, houseId: houseId || null, manualHouse: true, action: 'Import' }, prev, preview)
      : r)));
  };

  const changeType = (id: string, paymentType: BankImportPaymentType) => {
    updateRow(id, (r) => ({ ...r, paymentType }));
  };

  const changeMonth = (id: string, value: string) => {
    const [y, m] = value.split('-').map(Number);
    if (!y || !m) return;
    updateRow(id, (r) => ({ ...r, year: y, month: m }));
  };

  const changePart = (id: string, field: 'waterAmount' | 'electricityAmount' | 'commonAmount', value: string) => {
    const parsed = parseFloat(value.replace(/\s/g, '').replace(',', '.'));
    updateRow(id, (r) => ({ ...r, [field]: Number.isNaN(parsed) ? 0 : parsed }));
  };

  const toggleIgnore = (id: string) => {
    updateRow(id, (r) => ({ ...r, action: r.action === 'Ignore' ? 'Import' : 'Ignore' }));
  };

  const handleConfirm = async () => {
    if (!preview || inFlight.current) return;
    inFlight.current = true;
    setState('confirming');
    setError(null);
    try {
      const payload = rows.filter(isImportable).map((r) => ({
        transactionId: r.transactionId,
        date: r.date,
        amount: r.amount,
        counterAccount: r.counterAccount,
        counterName: r.counterName,
        message: r.message,
        variableSymbol: r.variableSymbol,
        action: r.action,
        houseId: r.action === 'Import' ? r.houseId : null,
        paymentType: r.paymentType,
        year: r.year,
        month: r.month,
        waterAmount: r.waterAmount,
        electricityAmount: r.electricityAmount,
        commonAmount: r.commonAmount,
      }));
      setResult(await confirmBankImport(preview.statement.account, payload));
      setState('success');
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Uložení plateb se nezdařilo.');
      setState('preview');
    } finally {
      inFlight.current = false;
    }
  };

  const handleReset = () => {
    setState('initial');
    setFile(null);
    setPreview(null);
    setRows([]);
    setError(null);
    setResult(null);
  };

  const newRows = rows.filter(isImportable);
  const problems = preview ? newRows.map((r) => rowProblem(r, rows, preview)).filter((p) => p !== null) : [];
  const canConfirm = newRows.length > 0 && problems.length === 0 && state === 'preview';

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-bold text-text-primary">Import z banky</h1>
        <p className="mt-1 text-sm text-text-muted">
          Nahrajte výpis z Fio banky ve formátu CSV (Internetbanking → Výpisy → export CSV).
          Příchozí platby se přiřadí k domácnostem podle čísla účtu, ze kterého přišly.
        </p>
        <HelpNote sectionId="importTwoStep" />
      </div>

      {error && (
        <div className="rounded-xl border border-danger/20 bg-danger-light p-4">
          <p className="text-sm text-danger">{error}</p>
        </div>
      )}

      {state === 'success' && result && (
        <div className="rounded-xl border border-success/20 bg-success-light p-4">
          <p className="text-sm font-medium text-success">
            Naimportováno {result.imported}, ignorováno {result.ignored}, nově přiřazené účty {result.newAccounts}
            {result.skipped.length > 0 && `, přeskočeno (už zpracované) ${result.skipped.length}`}.
          </p>
          <div className="mt-3 flex flex-wrap gap-2">
            <button onClick={handleReset} className="rounded-xl bg-success px-4 py-2 text-sm font-medium text-white hover:bg-emerald-600">
              Importovat další výpis
            </button>
            <Link to="/saldo" className="rounded-xl border border-border bg-surface-raised px-4 py-2 text-sm font-medium text-text-secondary hover:bg-surface-sunken/50">
              Platby
            </Link>
            <Link to="/advances" className="rounded-xl border border-border bg-surface-raised px-4 py-2 text-sm font-medium text-text-secondary hover:bg-surface-sunken/50">
              Zálohy
            </Link>
          </div>
        </div>
      )}

      {(state === 'initial' || state === 'uploading') && (
        <div className="space-y-4">
          <FileUploadZone accept=".csv" onFileSelected={setFile} disabled={state === 'uploading'} />
          {file && (
            <div className="flex items-center justify-between rounded-xl border border-border bg-surface-raised px-4 py-3">
              <span className="text-sm font-medium text-text-secondary">{file.name}</span>
              <button onClick={() => void handleUpload()} disabled={state === 'uploading'}
                className="rounded-xl bg-accent px-4 py-2 text-sm font-medium text-white hover:bg-accent-hover disabled:opacity-50">
                {state === 'uploading' ? <span className="flex items-center gap-2"><Spinner size="sm" /> Načítám…</span> : 'Načíst náhled'}
              </button>
            </div>
          )}
        </div>
      )}

      {(state === 'preview' || state === 'confirming') && preview && (
        <>
          <StatementSummary preview={preview} />

          <div className="overflow-x-auto rounded-2xl border border-border bg-surface-raised shadow-card">
            <table className="w-full text-sm">
              <thead className="bg-surface-sunken text-xs font-semibold uppercase tracking-wider text-text-muted">
                <tr>
                  <th className="px-3 py-3 text-left">Datum</th>
                  <th className="px-3 py-3 text-right">Částka</th>
                  <th className="px-3 py-3 text-left">Protistrana</th>
                  <th className="px-3 py-3 text-left">Zpráva / VS</th>
                  <th className="px-3 py-3 text-left">Domácnost</th>
                  <th className="px-3 py-3 text-left">Typ</th>
                  <th className="px-3 py-3 text-right">Voda</th>
                  <th className="px-3 py-3 text-right">Elektřina</th>
                  <th className="px-3 py-3 text-right">Společné</th>
                  <th className="px-3 py-3"></th>
                </tr>
              </thead>
              <tbody className="divide-y divide-border">
                {rows.map((row) => (
                  <PreviewRow
                    key={row.transactionId}
                    row={row}
                    houses={preview.houses}
                    problem={rowProblem(row, rows, preview)}
                    disabled={state === 'confirming'}
                    onHouse={(v) => changeHouse(row.transactionId, v)}
                    onType={(v) => changeType(row.transactionId, v)}
                    onMonth={(v) => changeMonth(row.transactionId, v)}
                    onPart={(f, v) => changePart(row.transactionId, f, v)}
                    onToggleIgnore={() => toggleIgnore(row.transactionId)}
                  />
                ))}
              </tbody>
            </table>
          </div>

          <div className="flex flex-wrap items-center gap-3">
            <button onClick={handleReset} disabled={state === 'confirming'}
              className="rounded-xl border border-border bg-surface-raised px-4 py-2 text-sm font-medium text-text-secondary hover:bg-surface-sunken/50 disabled:opacity-50">
              Zrušit
            </button>
            <button onClick={() => void handleConfirm()} disabled={!canConfirm}
              className="rounded-xl bg-accent px-4 py-2 text-sm font-medium text-white hover:bg-accent-hover disabled:opacity-50">
              {state === 'confirming' ? <span className="flex items-center gap-2"><Spinner size="sm" /> Ukládám…</span> : 'Potvrdit import'}
            </button>
            {newRows.length === 0 && <p className="text-sm text-text-muted">Ve výpisu nejsou žádné nové příchozí platby.</p>}
            {problems.length > 0 && (
              <p className="text-sm text-danger">
                {problems.length === 1 ? '1 platba potřebuje' : `${problems.length} plateb potřebuje`} doplnit — viz červeně označené řádky.
              </p>
            )}
          </div>
        </>
      )}
    </div>
  );
}

function StatementSummary({ preview }: { preview: BankImportPreview }) {
  const s = preview.statement;
  const newCount = preview.rows.filter(isImportable).length;
  return (
    <div className="space-y-3">
      <div className="grid grid-cols-2 gap-3 rounded-2xl border border-border bg-surface-raised p-4 text-sm md:grid-cols-4">
        <div>
          <p className="text-xs text-text-muted">Účet</p>
          <p className="font-medium">{s.account}</p>
        </div>
        <div>
          <p className="text-xs text-text-muted">Výpis</p>
          <p className="font-medium">{s.number ?? '—'} ({fmtDate(s.dateFrom)} – {fmtDate(s.dateTo)})</p>
        </div>
        <div>
          <p className="text-xs text-text-muted">Příjmy</p>
          <p className="font-medium">{s.totalIncome !== null ? czk.format(s.totalIncome) : '—'}</p>
        </div>
        <div>
          <p className="text-xs text-text-muted">Nové příchozí platby</p>
          <p className="font-medium">{newCount}</p>
        </div>
      </div>
      {preview.warnings.length > 0 && (
        <div className="rounded-xl border border-warning/20 bg-warning-light p-4">
          {preview.warnings.map((w, i) => <p key={i} className="text-sm text-warning">{w}</p>)}
        </div>
      )}
    </div>
  );
}

interface PreviewRowProps {
  row: EditableRow;
  houses: BankImportHouse[];
  problem: string | null;
  disabled: boolean;
  onHouse: (houseId: string) => void;
  onType: (type: BankImportPaymentType) => void;
  onMonth: (value: string) => void;
  onPart: (field: 'waterAmount' | 'electricityAmount' | 'commonAmount', value: string) => void;
  onToggleIgnore: () => void;
}

function PreviewRow({ row, houses, problem, disabled, onHouse, onType, onMonth, onPart, onToggleIgnore }: PreviewRowProps) {
  const editable = isImportable(row);
  const ignored = editable && row.action === 'Ignore';
  const tone = !editable || ignored
    ? 'bg-surface-sunken/60 text-text-muted'
    : problem
      ? 'bg-danger-light/60'
      : row.warnings.length > 0
        ? 'bg-warning-light/60'
        : 'bg-success-light/40';
  const houseName = houses.find((h) => h.houseId === row.houseId)?.houseName;
  const inputCls = 'w-24 rounded-lg border border-border bg-surface-raised px-2 py-1 text-right text-sm disabled:opacity-50';
  const active = editable && !ignored;

  return (
    <tr className={tone}>
      <td className="whitespace-nowrap px-3 py-2 align-top">{fmtDate(row.date)}</td>
      <td className={`whitespace-nowrap px-3 py-2 text-right align-top font-mono ${row.amount < 0 ? 'text-danger' : ''}`}>{czk.format(row.amount)}</td>
      <td className="px-3 py-2 align-top">
        <p className="font-medium">{row.counterName ?? '—'}</p>
        <p className="text-xs text-text-muted font-mono">{row.counterAccount ?? '—'}</p>
      </td>
      <td className="max-w-[14rem] px-3 py-2 align-top">
        <p className="truncate" title={row.message ?? undefined}>{row.message ?? '—'}</p>
        {row.variableSymbol && <p className="text-xs text-text-muted">VS {row.variableSymbol}</p>}
      </td>

      {!editable ? (
        <td colSpan={5} className="px-3 py-2 align-top text-xs">
          {statusLabel[row.status as keyof typeof statusLabel]}
          {houseName && ` · ${houseName}`}
          {row.warnings.map((w, i) => <span key={i}> · {w}</span>)}
        </td>
      ) : (
        <>
          <td className="px-3 py-2 align-top">
            <select value={row.houseId ?? ''} onChange={(e) => onHouse(e.target.value)} disabled={disabled || ignored}
              className="w-40 rounded-lg border border-border bg-surface-raised px-2 py-1 text-sm disabled:opacity-50">
              <option value="">— vyberte —</option>
              {houses.map((h) => <option key={h.houseId} value={h.houseId}>{h.houseName}</option>)}
            </select>
            {row.houseId && !ignored && (
              <p className="mt-1 text-xs text-text-muted">
                {row.matchSource === 'Account' && !row.manualHouse ? 'podle účtu' : 'ručně — účet se uloží k domácnosti'}
              </p>
            )}
          </td>
          <td className="px-3 py-2 align-top">
            <select value={row.paymentType} onChange={(e) => onType(e.target.value as BankImportPaymentType)} disabled={disabled || !active}
              className="rounded-lg border border-border bg-surface-raised px-2 py-1 text-sm disabled:opacity-50">
              <option value="Advance">Záloha</option>
              <option value="Doplatek">Doplatek</option>
            </select>
            {row.paymentType === 'Advance' && (
              <input type="month" value={monthKey(row.year, row.month)} onChange={(e) => onMonth(e.target.value)} disabled={disabled || !active}
                className="mt-1 block rounded-lg border border-border bg-surface-raised px-2 py-1 text-sm disabled:opacity-50" />
            )}
          </td>
          {(['waterAmount', 'electricityAmount', 'commonAmount'] as const).map((field) => (
            <td key={field} className="px-3 py-2 text-right align-top">
              <input type="number" step="1" min="0" value={row[field]} onChange={(e) => onPart(field, e.target.value)}
                disabled={disabled || !active} className={inputCls} />
            </td>
          ))}
        </>
      )}

      <td className="px-3 py-2 align-top text-right">
        {editable && (
          <button onClick={onToggleIgnore} disabled={disabled} className="text-xs font-medium text-text-muted hover:text-text-primary disabled:opacity-50">
            {ignored ? 'Importovat' : 'Ignorovat'}
          </button>
        )}
        {active && (problem || row.warnings.length > 0) && (
          <div className="mt-1 max-w-[14rem] text-left text-xs">
            {problem && <p className="text-danger">{problem}</p>}
            {row.warnings.map((w, i) => <p key={i} className="text-warning">{w}</p>)}
          </div>
        )}
      </td>
    </tr>
  );
}
