import { useState } from 'react';
import type { ChangeEvent } from 'react';
import { useAuth } from '../../auth/AuthContext';
import { Spinner } from '../../components/Spinner';
import { ConfirmDialog } from '../../components/ConfirmDialog';
import { HelpDisclosure } from '../../components/help/HelpDisclosure';
import { HelpNote } from '../../components/help/HelpNote';
import { ApiError } from '../../api/client';
import { downloadSeedReport, readCsv, seedApply, seedDryRun } from '../../api/seedImport';
import type { SeedFiles, SeedImportReport } from '../../api/seedImport';
import { formatIsoDay } from '../../utils/date';

const primaryBtn = 'rounded-xl bg-accent px-3 py-1.5 text-sm font-medium text-white hover:bg-accent-hover disabled:opacity-50';
const btn = 'rounded-lg border border-border px-2.5 py-1 text-xs font-medium text-text-secondary hover:bg-surface-sunken disabled:opacity-50';
const th = 'px-3 py-2 text-left';
const kc = (v: number) => `${v.toLocaleString('cs-CZ', { minimumFractionDigits: 2, maximumFractionDigits: 2 })} Kč`;

function reasons(err: unknown): string[] {
  if (err instanceof ApiError && err.details.length > 0) return err.details;
  return [err instanceof Error ? err.message : 'Nastala neočekávaná chyba'];
}

function status(report: SeedImportReport): { text: string; tone: string } {
  if (report.applied) return { text: 'Zapsáno. Opakovaný import už nic nezmění.', tone: 'bg-success-light text-success' };
  if (report.canApply) return { text: 'Zkouška prošla — data lze zapsat.', tone: 'bg-success-light text-success' };
  if (report.issues.length > 0) return { text: 'Nelze zapsat — opravte chyby a konflikty a zkontrolujte znovu.', tone: 'bg-danger-light text-danger' };
  return { text: 'Není co zapsat — všechny řádky už v aplikaci jsou.', tone: 'bg-surface-sunken text-text-secondary' };
}

/** Import of the starting data from the CSV templates (T13): dry run → report → apply. Admin only. */
export function SeedImportPage() {
  const { getAccessToken } = useAuth();
  const [files, setFiles] = useState<SeedFiles>({});
  const [report, setReport] = useState<SeedImportReport | null>(null);
  const [errors, setErrors] = useState<string[]>([]);
  const [busy, setBusy] = useState(false);
  const [confirming, setConfirming] = useState(false);
  const names = Object.keys(files).sort();

  const addFiles = async (e: ChangeEvent<HTMLInputElement>) => {
    const picked = Array.from(e.target.files ?? []);
    e.target.value = '';
    const read = await Promise.all(picked.map(async (f) => [f.name.toLowerCase(), await readCsv(f)] as const));
    setFiles((current) => ({ ...current, ...Object.fromEntries(read) }));
    setReport(null);
  };

  const removeFile = (name: string) => {
    setFiles((current) => Object.fromEntries(Object.entries(current).filter(([n]) => n !== name)));
    setReport(null);
  };

  const run = async (action: (f: SeedFiles) => Promise<SeedImportReport>) => {
    setBusy(true);
    setErrors([]);
    try {
      setReport(await action(files));
    } catch (err) {
      setErrors(reasons(err));
    } finally {
      setBusy(false);
    }
  };

  const download = async (format: 'md' | 'xlsx') => {
    setErrors([]);
    try {
      await downloadSeedReport(files, format, getAccessToken);
    } catch (err) {
      setErrors(reasons(err));
    }
  };

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-bold text-text-primary">Import počátečních dat</h1>
        <HelpNote sectionId="seedImport" />
        <HelpDisclosure sectionId="seedImport" />
      </div>

      <section aria-label="Soubory k importu" className="space-y-3 rounded-2xl border border-border bg-surface-raised p-4 shadow-card">
        <label className="block text-sm text-text-secondary">
          <span className="mb-1 block font-medium text-text-primary">Soubory CSV podle šablon</span>
          <input type="file" accept=".csv,text/csv" multiple onChange={(e) => void addFiles(e)} className="text-sm" />
        </label>
        {names.length > 0 && (
          <ul className="flex flex-wrap gap-2" aria-label="Nahrané soubory">
            {names.map((name) => (
              <li key={name} className="flex items-center gap-1 rounded-lg bg-surface-sunken px-2 py-1 text-xs">
                {name}
                <button type="button" aria-label={`Odebrat ${name}`} onClick={() => removeFile(name)} className="text-text-muted hover:text-danger">×</button>
              </li>
            ))}
          </ul>
        )}
        <div className="flex flex-wrap items-center gap-2">
          <button type="button" className={primaryBtn} disabled={busy || names.length === 0} onClick={() => void run(seedDryRun)}>
            Zkontrolovat nanečisto
          </button>
          {busy && <Spinner />}
        </div>
        {errors.length > 0 && (
          <ul role="alert" className="space-y-1 rounded-lg bg-danger-light p-2 text-sm text-danger">
            {errors.map((m) => <li key={m}>{m}</li>)}
          </ul>
        )}
      </section>

      {report && (
        <section aria-label="Report importu" className="space-y-4 rounded-2xl border border-border bg-surface-raised p-4 shadow-card">
          <div className="flex flex-wrap items-center justify-between gap-2">
            <p className={`rounded-lg px-3 py-2 text-sm font-medium ${status(report).tone}`}>{status(report).text}</p>
            <div className="flex flex-wrap gap-2">
              <button type="button" className={btn} onClick={() => void download('xlsx')}>Stáhnout report (XLSX)</button>
              <button type="button" className={btn} onClick={() => void download('md')}>Markdown</button>
              <button type="button" className={primaryBtn} disabled={busy || !report.canApply || report.applied} onClick={() => setConfirming(true)}>
                Zapsat
              </button>
            </div>
          </div>

          <table className="w-full text-sm" aria-label="Soubory">
            <thead className="bg-surface-sunken text-xs font-semibold uppercase tracking-wider text-text-muted">
              <tr>
                <th className={th}>Soubor</th>
                <th className={`${th} text-right`}>Řádků</th>
                <th className={`${th} text-right`}>Nové</th>
                <th className={`${th} text-right`}>Beze změny</th>
                <th className={`${th} text-right`}>Konflikty</th>
                <th className={`${th} text-right`}>Chyby</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-border">
              {report.files.map((f) => (
                <tr key={f.file} className={f.uploaded ? '' : 'text-text-muted'}>
                  <td className="px-3 py-1.5">{f.file}</td>
                  {f.uploaded ? (
                    <>
                      <td className="px-3 py-1.5 text-right">{f.rows}</td>
                      <td className="px-3 py-1.5 text-right">{f.created}</td>
                      <td className="px-3 py-1.5 text-right">{f.unchanged}</td>
                      <td className={`px-3 py-1.5 text-right ${f.conflicts > 0 ? 'font-semibold text-danger' : ''}`}>{f.conflicts}</td>
                      <td className={`px-3 py-1.5 text-right ${f.errors > 0 ? 'font-semibold text-danger' : ''}`}>{f.errors}</td>
                    </>
                  ) : (
                    <td colSpan={5} className="px-3 py-1.5 text-right">nenahráno</td>
                  )}
                </tr>
              ))}
            </tbody>
          </table>

          {report.issues.length > 0 && (
            <table className="w-full text-sm" aria-label="Chyby a konflikty">
              <thead className="bg-surface-sunken text-xs font-semibold uppercase tracking-wider text-text-muted">
                <tr>
                  <th className={th}>Soubor</th>
                  <th className={`${th} text-right`}>Řádek</th>
                  <th className={th}>Druh</th>
                  <th className={th}>Popis</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-border">
                {report.issues.map((i, n) => (
                  <tr key={`${i.file}-${i.line ?? 0}-${n}`} className="align-top">
                    <td className="whitespace-nowrap px-3 py-1.5">{i.file}</td>
                    <td className="px-3 py-1.5 text-right">{i.line ?? '—'}</td>
                    <td className={`px-3 py-1.5 font-medium ${i.severity === 'konflikt' ? 'text-warning' : 'text-danger'}`}>{i.severity}</td>
                    <td className="px-3 py-1.5">{i.message}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}

          <div>
            <h2 className="font-semibold text-text-primary">Salda domů po importu</h2>
            <p className="text-xs text-text-muted">
              Za {formatIsoDay(report.from)} – {formatIsoDay(report.today)}; kladné saldo = přeplatek, záporné = nedoplatek.
            </p>
            <table className="mt-2 w-full text-sm" aria-label="Salda domů po importu">
              <thead className="bg-surface-sunken text-xs font-semibold uppercase tracking-wider text-text-muted">
                <tr>
                  <th className={th}>Dům</th>
                  <th className={`${th} text-right`}>Počáteční podíl</th>
                  <th className={`${th} text-right`}>Platby</th>
                  <th className={`${th} text-right`}>Náklady</th>
                  <th className={`${th} text-right`}>Saldo</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-border">
                {report.houses.map((h) => (
                  <tr key={h.houseName}>
                    <td className="px-3 py-1.5">{h.houseName}</td>
                    <td className="px-3 py-1.5 text-right">{kc(h.opening)}</td>
                    <td className="px-3 py-1.5 text-right">{kc(h.payments)}</td>
                    <td className="px-3 py-1.5 text-right">{kc(h.costs)}</td>
                    <td className={`px-3 py-1.5 text-right font-medium ${h.saldo < 0 ? 'text-danger' : ''}`}>{kc(h.saldo)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          {report.components.length > 0 && (
            <div>
              <h2 className="font-semibold text-text-primary">Kontrola složek</h2>
              <ul className="mt-1 space-y-1 text-sm" aria-label="Kontrola složek">
                {report.components.map((c) => (
                  <li key={c.componentName} className={c.matches ? 'text-text-secondary' : 'font-medium text-danger'}>
                    {c.componentName}: rozpočteno {kc(c.allocated)}, Σ domů {kc(c.houses)}
                    {c.warnings.length > 0 && <span className="text-warning"> — {c.warnings.join('; ')}</span>}
                  </li>
                ))}
              </ul>
            </div>
          )}
        </section>
      )}

      <ConfirmDialog
        isOpen={confirming}
        title="Zapsat počáteční data?"
        message="Import se zapíše do aplikace přesně podle zkoušky nanečisto. Existující záznamy se nepřepíšou; zrušit zápis jde jen ruční opravou."
        confirmLabel="Zapsat"
        onConfirm={() => { setConfirming(false); void run(seedApply); }}
        onCancel={() => setConfirming(false)}
      />
    </div>
  );
}
