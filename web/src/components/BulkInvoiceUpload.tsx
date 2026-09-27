import { useCallback, useState } from 'react';
import { useApi } from '../hooks/useApi';
import { getCostComponents } from '../api/costComponents';
import type { CostComponent } from '../api/costComponents';
import { uploadDocument } from '../api/documents';

interface FileResult {
  name: string;
  ok: boolean;
  message: string;
}

const inputCls = 'border border-border rounded-lg px-2 py-1.5 text-sm bg-surface-raised focus:border-accent focus:ring-2 focus:ring-accent/20';

/**
 * Bulk upload of invoices and settlements the administrator receives (T11): several PDFs or photos at once into
 * Documents → „Faktury a vyúčtování“, optionally assigned to a cost component. Each file keeps its name.
 */
export function BulkInvoiceUpload({ getAccessToken, onUploaded }: { getAccessToken: () => Promise<string | null>; onUploaded: () => void }) {
  const { data: components } = useApi<CostComponent[]>(useCallback(() => getCostComponents(), []));
  const [files, setFiles] = useState<File[]>([]);
  const [componentId, setComponentId] = useState('');
  const [results, setResults] = useState<FileResult[]>([]);
  const [busy, setBusy] = useState(false);

  const upload = async () => {
    setBusy(true);
    const done: FileResult[] = [];
    for (const file of files) {
      const name = file.name.replace(/\.[^.]+$/, '');
      try {
        await uploadDocument(file, name, 'faktury', getAccessToken, componentId || undefined);
        done.push({ name: file.name, ok: true, message: 'nahráno' });
      } catch (err) {
        done.push({ name: file.name, ok: false, message: err instanceof Error ? err.message : 'nahrání se nezdařilo' });
      }
      setResults([...done]);
    }
    setBusy(false);
    setFiles([]);
    onUploaded();
  };

  return (
    <details className="mt-4 rounded-2xl border border-border bg-surface-raised p-4 shadow-card">
      <summary className="cursor-pointer text-sm font-medium text-accent">Nahrát faktury a vyúčtování (více souborů najednou)</summary>
      <div className="mt-3 space-y-2" aria-label="Hromadné nahrání faktur">
        <div className="flex flex-wrap items-end gap-2">
          <label className="text-xs text-text-secondary">
            <span className="mb-1 block">Soubory (PDF, JPG, PNG; max. 20 MB)</span>
            <input
              type="file"
              multiple
              accept="application/pdf,image/jpeg,image/png"
              onChange={(e) => { setFiles(Array.from(e.target.files ?? [])); setResults([]); }}
              className="text-sm"
            />
          </label>
          <label className="text-xs text-text-secondary">
            <span className="mb-1 block">Nákladová složka (nepovinné)</span>
            <select value={componentId} onChange={(e) => setComponentId(e.target.value)} className={inputCls}>
              <option value="">—</option>
              {(components ?? []).map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
            </select>
          </label>
          <button
            type="button"
            onClick={() => void upload()}
            disabled={busy || files.length === 0}
            className="rounded-xl bg-accent px-3 py-1.5 text-sm font-medium text-white hover:bg-accent-hover disabled:opacity-50"
          >
            {busy ? 'Nahrávám…' : `Nahrát ${files.length || ''}`.trim()}
          </button>
        </div>
        {results.length > 0 && (
          <ul className="space-y-0.5 text-sm" aria-label="Výsledek nahrání">
            {results.map((r) => (
              <li key={r.name} className={r.ok ? 'text-success' : 'text-danger'}>{r.name}: {r.message}</li>
            ))}
          </ul>
        )}
        <p className="text-xs text-text-muted">Nahrané faktury najdete v Nákladech v přehledu „Dokumenty bez zaúčtování“, dokud k nim nevznikne náklad.</p>
      </div>
    </details>
  );
}
