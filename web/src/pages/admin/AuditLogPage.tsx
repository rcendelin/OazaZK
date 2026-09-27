import { useCallback, useState } from 'react';
import { useApi } from '../../hooks/useApi';
import { Spinner } from '../../components/Spinner';
import { getAuditLog } from '../../api/audit';
import type { AuditLogEntry, AuditLogFilter } from '../../api/audit';
import { shiftIsoDate, todayIso } from '../../utils/date';

const fmtTime = (iso: string) =>
  new Intl.DateTimeFormat('cs-CZ', { dateStyle: 'short', timeStyle: 'medium' }).format(new Date(iso));

const actionLabel: Record<string, { label: string; cls: string }> = {
  Create: { label: 'Vytvoření', cls: 'bg-success-light text-success' },
  Update: { label: 'Změna', cls: 'bg-accent-light text-accent' },
  Delete: { label: 'Smazání', cls: 'bg-danger-light text-danger' },
  Correction: { label: 'Oprava', cls: 'bg-warning-light text-warning' },
};

function prettyJson(value: string | null): string {
  if (value === null) return '—';
  try {
    return JSON.stringify(JSON.parse(value), null, 2);
  } catch {
    return value;
  }
}

/** Read-only audit log (X4): who changed what, when, from what to what and why. Admin only. */
export function AuditLogPage() {
  const [draft, setDraft] = useState<AuditLogFilter>(() => {
    const today = todayIso();
    return { from: shiftIsoDate(today, { days: -90 }), to: today, entityType: '', entityId: '' };
  });
  const [filter, setFilter] = useState<AuditLogFilter>(draft);

  const { data: entries, loading, error } = useApi<AuditLogEntry[]>(
    useCallback(() => getAuditLog(filter), [filter]),
  );

  const inputCls = 'border border-border rounded-xl px-3 py-2 text-sm bg-surface-raised focus:border-accent focus:ring-2 focus:ring-accent/20';

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-bold text-text-primary">Audit změn</h1>
        <p className="mt-1 text-sm text-text-muted">
          Kdo, kdy a proč změnil účetní data. Záznamy nelze upravit ani smazat.
        </p>
      </div>

      <form
        className="flex flex-wrap items-end gap-3"
        onSubmit={(e) => { e.preventDefault(); setFilter({ ...draft }); }}
      >
        <label className="text-sm text-text-secondary">
          <span className="mb-1 block">Od</span>
          <input type="date" value={draft.from} onChange={(e) => setDraft({ ...draft, from: e.target.value })} className={inputCls} />
        </label>
        <label className="text-sm text-text-secondary">
          <span className="mb-1 block">Do</span>
          <input type="date" value={draft.to} onChange={(e) => setDraft({ ...draft, to: e.target.value })} className={inputCls} />
        </label>
        <label className="text-sm text-text-secondary">
          <span className="mb-1 block">Typ záznamu</span>
          <input type="text" value={draft.entityType ?? ''} placeholder="např. CostComponent"
            onChange={(e) => setDraft({ ...draft, entityType: e.target.value })} className={inputCls} />
        </label>
        <label className="text-sm text-text-secondary">
          <span className="mb-1 block">Id záznamu</span>
          <input type="text" value={draft.entityId ?? ''} onChange={(e) => setDraft({ ...draft, entityId: e.target.value })} className={inputCls} />
        </label>
        <button type="submit" className="rounded-xl bg-accent px-4 py-2 text-sm font-medium text-white hover:bg-accent-hover">
          Zobrazit
        </button>
      </form>

      {error && <div className="rounded-xl bg-danger-light p-4 text-sm text-danger">{error}</div>}

      {loading ? (
        <div className="flex justify-center p-8"><Spinner size="lg" /></div>
      ) : (
        <div className="overflow-x-auto rounded-2xl border border-border bg-surface-raised shadow-card">
          <table className="w-full text-sm">
            <thead className="bg-surface-sunken text-xs font-semibold uppercase tracking-wider text-text-muted">
              <tr>
                <th className="px-4 py-3 text-left">Kdy</th>
                <th className="px-4 py-3 text-left">Kdo</th>
                <th className="px-4 py-3 text-left">Akce</th>
                <th className="px-4 py-3 text-left">Záznam</th>
                <th className="px-4 py-3 text-left">Důvod</th>
                <th className="px-4 py-3 text-left">Změna</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-border">
              {(entries ?? []).map((e) => (
                <tr key={e.id} className="align-top hover:bg-surface-sunken/50">
                  <td className="whitespace-nowrap px-4 py-3">{fmtTime(e.timestamp)}</td>
                  <td className="px-4 py-3">{e.userName ?? e.userId}</td>
                  <td className="px-4 py-3">
                    <span className={`rounded-full px-2 py-0.5 text-xs font-medium ${actionLabel[e.action]?.cls ?? 'bg-surface-sunken text-text-secondary'}`}>
                      {actionLabel[e.action]?.label ?? e.action}
                    </span>
                  </td>
                  <td className="px-4 py-3">
                    <p className="font-medium">{e.entityType}</p>
                    <p className="font-mono text-xs text-text-muted">{e.entityId}</p>
                  </td>
                  <td className="max-w-[16rem] px-4 py-3 text-text-secondary">{e.reason ?? '—'}</td>
                  <td className="px-4 py-3">
                    <details>
                      <summary className="cursor-pointer text-xs font-medium text-accent">Před / po</summary>
                      <div className="mt-2 grid gap-2 md:grid-cols-2">
                        <pre className="max-h-64 overflow-auto rounded-lg bg-surface-sunken p-2 text-xs">{prettyJson(e.oldValue)}</pre>
                        <pre className="max-h-64 overflow-auto rounded-lg bg-surface-sunken p-2 text-xs">{prettyJson(e.newValue)}</pre>
                      </div>
                    </details>
                  </td>
                </tr>
              ))}
              {entries && entries.length === 0 && (
                <tr><td colSpan={6} className="px-4 py-8 text-center text-text-muted">V tomto období nejsou žádné záznamy.</td></tr>
              )}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
