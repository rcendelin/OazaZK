import { useCallback, useEffect, useRef, useState } from 'react';
import { useAuth } from '../auth/AuthContext';
import { useApi } from '../hooks/useApi';
import {
  getDocuments,
  uploadDocument,
  downloadDocument,
  deleteDocument,
  getDocumentVersions,
  uploadDocumentVersion,
  downloadDocumentVersion,
} from '../api/documents';
import { FileUploadZone } from '../components/FileUploadZone';
import { ConfirmDialog } from '../components/ConfirmDialog';
import { Spinner } from '../components/Spinner';
import type { DocumentResponse, DocumentVersionResponse } from '../types';

const CATEGORIES = [
  { key: '', label: 'Vse' },
  { key: 'stanovy', label: 'Stanovy' },
  { key: 'zapisy', label: 'Zapisy' },
  { key: 'smlouvy', label: 'Smlouvy' },
  { key: 'ostatni', label: 'Ostatni' },
] as const;

const CATEGORY_LABELS: Record<string, string> = {
  stanovy: 'Stanovy',
  zapisy: 'Zapisy',
  smlouvy: 'Smlouvy',
  ostatni: 'Ostatni',
};

const CATEGORY_COLORS: Record<string, string> = {
  stanovy: 'bg-accent-light text-accent',
  zapisy: 'bg-success-light text-success',
  smlouvy: 'bg-purple-50 text-purple-600',
  ostatni: 'bg-surface-sunken text-text-secondary',
};

const formatDate = (dateStr: string): string =>
  new Intl.DateTimeFormat('cs-CZ').format(new Date(dateStr));

const formatFileSize = (bytes: number): string => {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(0)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
};

export function DocumentsPage() {
  const { user, getAccessToken } = useAuth();
  const isAdmin = user?.role === 'Admin';

  const [activeCategory, setActiveCategory] = useState('');
  const [showUploadModal, setShowUploadModal] = useState(false);
  const [downloadError, setDownloadError] = useState<string | null>(null);
  const [deleteTarget, setDeleteTarget] = useState<DocumentResponse | null>(null);
  const [deleteLoading, setDeleteLoading] = useState(false);
  const [expandedDocId, setExpandedDocId] = useState<string | null>(null);
  const [versions, setVersions] = useState<DocumentVersionResponse[]>([]);
  const [versionsLoading, setVersionsLoading] = useState(false);
  const [versionUploadTarget, setVersionUploadTarget] = useState<DocumentResponse | null>(null);

  const fetchDocuments = useCallback(
    () => getDocuments(activeCategory || undefined),
    [activeCategory],
  );
  const {
    data: documents,
    loading,
    error,
    refetch,
  } = useApi(fetchDocuments, [activeCategory]);

  const handleDownload = async (doc: DocumentResponse) => {
    try {
      setDownloadError(null);
      await downloadDocument(doc.id, doc.name, getAccessToken);
    } catch {
      setDownloadError('Stahovani se nezdarilo');
    }
  };

  const handleDeleteConfirm = async () => {
    if (!deleteTarget) return;
    try {
      setDeleteLoading(true);
      await deleteDocument(deleteTarget.id);
      setDeleteTarget(null);
      refetch();
    } catch {
      setDownloadError('Smazani se nezdarilo');
    } finally {
      setDeleteLoading(false);
    }
  };

  const handleToggleVersions = async (doc: DocumentResponse) => {
    if (expandedDocId === doc.id) {
      setExpandedDocId(null);
      setVersions([]);
      return;
    }
    setExpandedDocId(doc.id);
    setVersionsLoading(true);
    try {
      const v = await getDocumentVersions(doc.id);
      setVersions(v);
    } catch {
      setVersions([]);
    } finally {
      setVersionsLoading(false);
    }
  };

  const handleDownloadVersion = async (doc: DocumentResponse, version: number) => {
    try {
      setDownloadError(null);
      await downloadDocumentVersion(doc.id, version, doc.name, getAccessToken);
    } catch {
      setDownloadError('Stahovani verze se nezdarilo');
    }
  };

  return (
    <div>
      <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
        <h1 className="text-2xl font-bold text-text-primary">Dokumenty</h1>
        {isAdmin && (
          <button
            onClick={() => setShowUploadModal(true)}
            className="inline-flex items-center rounded-xl bg-accent px-4 py-2 text-sm font-medium text-white hover:bg-accent-hover"
          >
            Nahrat dokument
          </button>
        )}
      </div>

      {/* Category tabs */}
      <div className="mt-6 border-b border-border">
        <nav className="-mb-px flex space-x-6 overflow-x-auto" aria-label="Kategorie">
          {CATEGORIES.map((cat) => (
            <button
              key={cat.key}
              onClick={() => setActiveCategory(cat.key)}
              className={`whitespace-nowrap border-b-2 px-1 pb-3 text-sm font-medium transition-colors ${
                activeCategory === cat.key
                  ? 'border-accent text-accent'
                  : 'border-transparent text-text-muted hover:border-border-strong hover:text-text-secondary'
              }`}
            >
              {cat.label}
            </button>
          ))}
        </nav>
      </div>

      {/* Error messages */}
      {error && (
        <div className="mt-4 rounded-xl bg-danger-light p-3">
          <p className="text-sm text-danger">{error}</p>
        </div>
      )}
      {downloadError && (
        <div className="mt-4 rounded-xl bg-danger-light p-3">
          <p className="text-sm text-danger">{downloadError}</p>
        </div>
      )}

      {/* Loading */}
      {loading && (
        <div className="mt-8 flex justify-center">
          <Spinner size="lg" />
        </div>
      )}

      {/* Empty state */}
      {!loading && !error && documents && documents.length === 0 && (
        <div className="mt-8 text-center">
          <p className="text-text-muted">Zadne dokumenty v teto kategorii.</p>
        </div>
      )}

      {/* Desktop table */}
      {!loading && documents && documents.length > 0 && (
        <>
          <div className="mt-6 hidden overflow-x-auto md:block">
            <table className="min-w-full divide-y divide-border">
              <thead className="bg-surface-sunken">
                <tr>
                  <th className="px-4 py-3 text-left text-xs font-semibold uppercase tracking-wider text-text-muted">
                    Nazev dokumentu
                  </th>
                  <th className="px-4 py-3 text-left text-xs font-semibold uppercase tracking-wider text-text-muted">
                    Kategorie
                  </th>
                  <th className="px-4 py-3 text-left text-xs font-semibold uppercase tracking-wider text-text-muted">
                    Datum nahrani
                  </th>
                  <th className="px-4 py-3 text-left text-xs font-semibold uppercase tracking-wider text-text-muted">
                    Velikost
                  </th>
                  <th className="px-4 py-3 text-left text-xs font-semibold uppercase tracking-wider text-text-muted">
                    Verze
                  </th>
                  <th className="px-4 py-3 text-right text-xs font-semibold uppercase tracking-wider text-text-muted">
                    Akce
                  </th>
                </tr>
              </thead>
              <tbody className="divide-y divide-border bg-surface-raised">
                {documents.map((doc) => (
                  <DocumentRow
                    key={doc.id}
                    doc={doc}
                    isAdmin={isAdmin}
                    isExpanded={expandedDocId === doc.id}
                    versions={expandedDocId === doc.id ? versions : []}
                    versionsLoading={expandedDocId === doc.id && versionsLoading}
                    onDownload={() => void handleDownload(doc)}
                    onDelete={() => setDeleteTarget(doc)}
                    onToggleVersions={() => void handleToggleVersions(doc)}
                    onUploadVersion={() => setVersionUploadTarget(doc)}
                    onDownloadVersion={(v) => void handleDownloadVersion(doc, v)}
                  />
                ))}
              </tbody>
            </table>
          </div>

          {/* Mobile cards */}
          <div className="mt-6 space-y-3 md:hidden">
            {documents.map((doc) => (
              <div
                key={doc.id}
                className="rounded-2xl bg-surface-raised p-4 shadow-card"
              >
                <div className="flex items-start justify-between">
                  <div className="min-w-0 flex-1">
                    <p className="text-sm font-medium text-text-primary">
                      {doc.name}
                    </p>
                    <div className="mt-1 flex flex-wrap items-center gap-2">
                      <span
                        className={`inline-flex rounded-full px-2 py-0.5 text-xs font-medium ${
                          CATEGORY_COLORS[doc.category] ?? 'bg-surface-sunken text-text-secondary'
                        }`}
                      >
                        {CATEGORY_LABELS[doc.category] ?? doc.category}
                      </span>
                      <span className="text-xs text-text-muted">
                        {formatDate(doc.uploadedAt)}
                      </span>
                      <span className="text-xs text-text-muted">
                        {formatFileSize(doc.fileSizeBytes)}
                      </span>
                    </div>
                  </div>
                </div>
                <div className="mt-3 flex flex-wrap gap-3">
                  <button
                    onClick={() => void handleDownload(doc)}
                    className="text-sm font-medium text-accent hover:text-accent-hover"
                  >
                    Stahnout
                  </button>
                  <button
                    onClick={() => void handleToggleVersions(doc)}
                    className="text-sm font-medium text-text-secondary hover:text-text-primary"
                  >
                    {expandedDocId === doc.id ? 'Skryt verze' : 'Verze'}
                  </button>
                  {isAdmin && (
                    <>
                      <button
                        onClick={() => setVersionUploadTarget(doc)}
                        className="text-sm font-medium text-success hover:text-emerald-600"
                      >
                        Nova verze
                      </button>
                      <button
                        onClick={() => setDeleteTarget(doc)}
                        className="text-sm font-medium text-danger hover:text-danger"
                      >
                        Smazat
                      </button>
                    </>
                  )}
                </div>
                {/* Mobile version list */}
                {expandedDocId === doc.id && (
                  <div className="mt-3 border-t border-border pt-3">
                    {versionsLoading && <Spinner />}
                    {!versionsLoading && versions.length === 0 && (
                      <p className="text-xs text-text-muted">Zadne verze</p>
                    )}
                    {!versionsLoading && versions.map((v) => (
                      <div key={v.versionNumber} className="flex items-center justify-between py-1">
                        <span className="text-xs text-text-muted">
                          v{v.versionNumber} - {formatDate(v.uploadedAt)} - {formatFileSize(v.fileSizeBytes)}
                        </span>
                        <button
                          onClick={() => void handleDownloadVersion(doc, v.versionNumber)}
                          className="text-xs font-medium text-accent hover:text-accent-hover"
                        >
                          Stahnout
                        </button>
                      </div>
                    ))}
                  </div>
                )}
              </div>
            ))}
          </div>
        </>
      )}

      {/* Upload modal */}
      {showUploadModal && (
        <UploadModal
          onClose={() => setShowUploadModal(false)}
          onUploaded={() => {
            setShowUploadModal(false);
            refetch();
          }}
          getAccessToken={getAccessToken}
        />
      )}

      {/* Version upload modal */}
      {versionUploadTarget && (
        <VersionUploadModal
          doc={versionUploadTarget}
          onClose={() => setVersionUploadTarget(null)}
          onUploaded={() => {
            setVersionUploadTarget(null);
            refetch();
            // Refresh versions if currently expanded
            if (expandedDocId === versionUploadTarget.id) {
              void getDocumentVersions(versionUploadTarget.id).then(setVersions);
            }
          }}
          getAccessToken={getAccessToken}
        />
      )}

      {/* Delete confirmation */}
      <ConfirmDialog
        isOpen={deleteTarget !== null}
        title="Smazat dokument"
        message={`Opravdu chcete smazat dokument "${deleteTarget?.name ?? ''}"?`}
        confirmLabel={deleteLoading ? 'Mazu...' : 'Smazat'}
        confirmVariant="danger"
        onConfirm={() => void handleDeleteConfirm()}
        onCancel={() => setDeleteTarget(null)}
      />
    </div>
  );
}

// ─── Document Row with expandable versions ──────────────────────────────────

interface DocumentRowProps {
  doc: DocumentResponse;
  isAdmin: boolean;
  isExpanded: boolean;
  versions: DocumentVersionResponse[];
  versionsLoading: boolean;
  onDownload: () => void;
  onDelete: () => void;
  onToggleVersions: () => void;
  onUploadVersion: () => void;
  onDownloadVersion: (version: number) => void;
}

function DocumentRow({
  doc,
  isAdmin,
  isExpanded,
  versions,
  versionsLoading,
  onDownload,
  onDelete,
  onToggleVersions,
  onUploadVersion,
  onDownloadVersion,
}: DocumentRowProps) {
  return (
    <>
      <tr className="hover:bg-surface-sunken/50">
        <td className="whitespace-nowrap px-4 py-3 text-sm font-medium text-text-primary">
          <button
            onClick={onToggleVersions}
            className="text-left hover:text-accent"
          >
            {doc.name}
          </button>
        </td>
        <td className="whitespace-nowrap px-4 py-3 text-sm">
          <span
            className={`inline-flex rounded-full px-2 py-0.5 text-xs font-medium ${
              CATEGORY_COLORS[doc.category] ?? 'bg-surface-sunken text-text-secondary'
            }`}
          >
            {CATEGORY_LABELS[doc.category] ?? doc.category}
          </span>
        </td>
        <td className="whitespace-nowrap px-4 py-3 text-sm text-text-muted">
          {formatDate(doc.uploadedAt)}
        </td>
        <td className="whitespace-nowrap px-4 py-3 text-sm text-text-muted">
          {formatFileSize(doc.fileSizeBytes)}
        </td>
        <td className="whitespace-nowrap px-4 py-3 text-sm text-text-muted">
          <button
            onClick={onToggleVersions}
            className="text-accent hover:text-accent-hover"
          >
            {isExpanded ? 'Skryt' : 'Zobrazit'}
          </button>
        </td>
        <td className="whitespace-nowrap px-4 py-3 text-right text-sm">
          <button
            onClick={onDownload}
            className="font-medium text-accent hover:text-accent-hover"
          >
            Stahnout
          </button>
          {isAdmin && (
            <>
              <button
                onClick={onUploadVersion}
                className="ml-4 font-medium text-success hover:text-emerald-600"
              >
                Nova verze
              </button>
              <button
                onClick={onDelete}
                className="ml-4 font-medium text-danger hover:text-danger"
              >
                Smazat
              </button>
            </>
          )}
        </td>
      </tr>
      {isExpanded && (
        <tr>
          <td colSpan={6} className="bg-surface-sunken px-8 py-3">
            {versionsLoading && (
              <div className="flex justify-center py-2">
                <Spinner />
              </div>
            )}
            {!versionsLoading && versions.length === 0 && (
              <p className="text-sm text-text-muted">Zadne verze k zobrazeni</p>
            )}
            {!versionsLoading && versions.length > 0 && (
              <table className="w-full text-sm">
                <thead>
                  <tr className="text-xs text-text-muted">
                    <th className="pb-1 text-left font-medium">Verze</th>
                    <th className="pb-1 text-left font-medium">Datum</th>
                    <th className="pb-1 text-left font-medium">Velikost</th>
                    <th className="pb-1 text-right font-medium">Akce</th>
                  </tr>
                </thead>
                <tbody>
                  {versions.map((v) => (
                    <tr key={v.versionNumber} className="border-t border-border">
                      <td className="py-1.5 text-text-secondary">v{v.versionNumber}</td>
                      <td className="py-1.5 text-text-muted">{formatDate(v.uploadedAt)}</td>
                      <td className="py-1.5 text-text-muted">{formatFileSize(v.fileSizeBytes)}</td>
                      <td className="py-1.5 text-right">
                        <button
                          onClick={() => onDownloadVersion(v.versionNumber)}
                          className="font-medium text-accent hover:text-accent-hover"
                        >
                          Stahnout
                        </button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </td>
        </tr>
      )}
    </>
  );
}

// ─── Upload Modal ───────────────────────────────────────────────────────────

interface UploadModalProps {
  onClose: () => void;
  onUploaded: () => void;
  getAccessToken: () => Promise<string | null>;
}

function UploadModal({ onClose, onUploaded, getAccessToken }: UploadModalProps) {
  const [name, setName] = useState('');
  const [category, setCategory] = useState('stanovy');
  const [file, setFile] = useState<File | null>(null);
  const [uploading, setUploading] = useState(false);
  const [uploadError, setUploadError] = useState<string | null>(null);
  const submittingRef = useRef(false);

  // Close on Escape
  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose();
    };
    document.addEventListener('keydown', handleKeyDown);
    return () => document.removeEventListener('keydown', handleKeyDown);
  }, [onClose]);

  const handleSubmit = async () => {
    if (!file || !name.trim() || submittingRef.current) return;
    submittingRef.current = true;
    setUploading(true);
    setUploadError(null);

    try {
      await uploadDocument(file, name.trim(), category, getAccessToken);
      onUploaded();
    } catch {
      setUploadError('Nahravani se nezdarilo');
    } finally {
      setUploading(false);
      submittingRef.current = false;
    }
  };

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center"
      role="dialog"
      aria-modal="true"
    >
      <div
        className="fixed inset-0 bg-black/40 backdrop-blur-sm transition-opacity"
        onClick={onClose}
      />
      <div className="relative z-10 mx-4 w-full max-w-lg rounded-2xl bg-surface-raised p-6 shadow-dialog">
        <h2 className="text-lg font-semibold text-text-primary">Nahrat dokument</h2>

        <div className="mt-4 space-y-4">
          {/* Document name */}
          <div>
            <label
              htmlFor="doc-name"
              className="block text-sm font-medium text-text-secondary"
            >
              Nazev dokumentu
            </label>
            <input
              id="doc-name"
              type="text"
              value={name}
              onChange={(e) => setName(e.target.value)}
              className="mt-1 block w-full rounded-xl border border-border bg-surface-raised px-3 py-2 text-sm shadow-card focus:border-accent focus:outline-none focus:ring-2 focus:ring-accent/20"
              placeholder="Nazev dokumentu"
            />
          </div>

          {/* Category select */}
          <div>
            <label
              htmlFor="doc-category"
              className="block text-sm font-medium text-text-secondary"
            >
              Kategorie
            </label>
            <select
              id="doc-category"
              value={category}
              onChange={(e) => setCategory(e.target.value)}
              className="mt-1 block w-full rounded-xl border border-border bg-surface-raised px-3 py-2 text-sm shadow-card focus:border-accent focus:outline-none focus:ring-2 focus:ring-accent/20"
            >
              <option value="stanovy">Stanovy</option>
              <option value="zapisy">Zapisy</option>
              <option value="smlouvy">Smlouvy</option>
              <option value="ostatni">Ostatni</option>
            </select>
          </div>

          {/* File upload */}
          <div>
            <label className="block text-sm font-medium text-text-secondary">
              Soubor
            </label>
            <div className="mt-1">
              {file ? (
                <div className="flex items-center justify-between rounded-xl border border-border px-3 py-2">
                  <span className="truncate text-sm text-text-secondary">
                    {file.name} ({formatFileSize(file.size)})
                  </span>
                  <button
                    onClick={() => setFile(null)}
                    className="ml-2 text-sm text-danger hover:text-danger"
                  >
                    Odebrat
                  </button>
                </div>
              ) : (
                <FileUploadZone
                  onFileSelected={setFile}
                  accept=".pdf,.docx,.xlsx,.jpg,.png"
                  disabled={uploading}
                />
              )}
            </div>
          </div>

          {/* Error */}
          {uploadError && (
            <div className="rounded-xl bg-danger-light p-3">
              <p className="text-sm text-danger">{uploadError}</p>
            </div>
          )}
        </div>

        {/* Actions */}
        <div className="mt-6 flex justify-end gap-3">
          <button
            onClick={onClose}
            disabled={uploading}
            className="rounded-xl border border-border bg-surface-raised px-4 py-2 text-sm font-medium text-text-secondary hover:bg-surface-sunken/50"
          >
            Zrusit
          </button>
          <button
            onClick={() => void handleSubmit()}
            disabled={uploading || !file || !name.trim()}
            className="rounded-xl bg-accent px-4 py-2 text-sm font-medium text-white hover:bg-accent-hover disabled:cursor-not-allowed disabled:opacity-50"
          >
            {uploading ? 'Nahravam...' : 'Nahrat'}
          </button>
        </div>
      </div>
    </div>
  );
}

// ─── Version Upload Modal ───────────────────────────────────────────────────

interface VersionUploadModalProps {
  doc: DocumentResponse;
  onClose: () => void;
  onUploaded: () => void;
  getAccessToken: () => Promise<string | null>;
}

function VersionUploadModal({ doc, onClose, onUploaded, getAccessToken }: VersionUploadModalProps) {
  const [file, setFile] = useState<File | null>(null);
  const [uploading, setUploading] = useState(false);
  const [uploadError, setUploadError] = useState<string | null>(null);
  const submittingRef = useRef(false);

  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose();
    };
    document.addEventListener('keydown', handleKeyDown);
    return () => document.removeEventListener('keydown', handleKeyDown);
  }, [onClose]);

  const handleSubmit = async () => {
    if (!file || submittingRef.current) return;
    submittingRef.current = true;
    setUploading(true);
    setUploadError(null);

    try {
      await uploadDocumentVersion(doc.id, file, getAccessToken);
      onUploaded();
    } catch {
      setUploadError('Nahravani verze se nezdarilo');
    } finally {
      setUploading(false);
      submittingRef.current = false;
    }
  };

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center"
      role="dialog"
      aria-modal="true"
    >
      <div
        className="fixed inset-0 bg-black/40 backdrop-blur-sm transition-opacity"
        onClick={onClose}
      />
      <div className="relative z-10 mx-4 w-full max-w-lg rounded-2xl bg-surface-raised p-6 shadow-dialog">
        <h2 className="text-lg font-semibold text-text-primary">
          Nahrat novou verzi: {doc.name}
        </h2>

        <div className="mt-4 space-y-4">
          <div>
            <label className="block text-sm font-medium text-text-secondary">
              Soubor
            </label>
            <div className="mt-1">
              {file ? (
                <div className="flex items-center justify-between rounded-xl border border-border px-3 py-2">
                  <span className="truncate text-sm text-text-secondary">
                    {file.name} ({formatFileSize(file.size)})
                  </span>
                  <button
                    onClick={() => setFile(null)}
                    className="ml-2 text-sm text-danger hover:text-danger"
                  >
                    Odebrat
                  </button>
                </div>
              ) : (
                <FileUploadZone
                  onFileSelected={setFile}
                  accept=".pdf,.docx,.xlsx,.jpg,.png"
                  disabled={uploading}
                />
              )}
            </div>
          </div>

          {uploadError && (
            <div className="rounded-xl bg-danger-light p-3">
              <p className="text-sm text-danger">{uploadError}</p>
            </div>
          )}
        </div>

        <div className="mt-6 flex justify-end gap-3">
          <button
            onClick={onClose}
            disabled={uploading}
            className="rounded-xl border border-border bg-surface-raised px-4 py-2 text-sm font-medium text-text-secondary hover:bg-surface-sunken/50"
          >
            Zrusit
          </button>
          <button
            onClick={() => void handleSubmit()}
            disabled={uploading || !file}
            className="rounded-xl bg-accent px-4 py-2 text-sm font-medium text-white hover:bg-accent-hover disabled:cursor-not-allowed disabled:opacity-50"
          >
            {uploading ? 'Nahravam...' : 'Nahrat verzi'}
          </button>
        </div>
      </div>
    </div>
  );
}
