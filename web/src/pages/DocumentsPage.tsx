import { useCallback, useEffect, useRef, useState } from 'react';
import { useAuth } from '../auth/AuthContext';
import { useApi } from '../hooks/useApi';
import {
  getDocuments,
  uploadDocument,
  downloadDocument,
  deleteDocument,
} from '../api/documents';
import { FileUploadZone } from '../components/FileUploadZone';
import { ConfirmDialog } from '../components/ConfirmDialog';
import { Spinner } from '../components/Spinner';
import type { DocumentResponse } from '../types';

const CATEGORIES = [
  { key: '', label: 'Vše' },
  { key: 'stanovy', label: 'Stanovy' },
  { key: 'zapisy', label: 'Zápisy' },
  { key: 'smlouvy', label: 'Smlouvy' },
  { key: 'ostatni', label: 'Ostatní' },
] as const;

const CATEGORY_LABELS: Record<string, string> = {
  stanovy: 'Stanovy',
  zapisy: 'Zápisy',
  smlouvy: 'Smlouvy',
  ostatni: 'Ostatní',
};

const CATEGORY_COLORS: Record<string, string> = {
  stanovy: 'bg-blue-100 text-blue-700',
  zapisy: 'bg-green-100 text-green-700',
  smlouvy: 'bg-purple-100 text-purple-700',
  ostatni: 'bg-gray-100 text-gray-700',
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
      setDownloadError('Stahování se nezdařilo');
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
      setDownloadError('Smazání se nezdařilo');
    } finally {
      setDeleteLoading(false);
    }
  };

  return (
    <div>
      <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
        <h1 className="text-2xl font-bold text-gray-900">Dokumenty</h1>
        {isAdmin && (
          <button
            onClick={() => setShowUploadModal(true)}
            className="inline-flex items-center rounded-md bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700"
          >
            Nahrát dokument
          </button>
        )}
      </div>

      {/* Category tabs */}
      <div className="mt-6 border-b border-gray-200">
        <nav className="-mb-px flex space-x-6 overflow-x-auto" aria-label="Kategorie">
          {CATEGORIES.map((cat) => (
            <button
              key={cat.key}
              onClick={() => setActiveCategory(cat.key)}
              className={`whitespace-nowrap border-b-2 px-1 pb-3 text-sm font-medium transition-colors ${
                activeCategory === cat.key
                  ? 'border-blue-600 text-blue-600'
                  : 'border-transparent text-gray-500 hover:border-gray-300 hover:text-gray-700'
              }`}
            >
              {cat.label}
            </button>
          ))}
        </nav>
      </div>

      {/* Error messages */}
      {error && (
        <div className="mt-4 rounded-md bg-red-50 p-3">
          <p className="text-sm text-red-700">{error}</p>
        </div>
      )}
      {downloadError && (
        <div className="mt-4 rounded-md bg-red-50 p-3">
          <p className="text-sm text-red-700">{downloadError}</p>
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
          <p className="text-gray-500">Žádné dokumenty v této kategorii.</p>
        </div>
      )}

      {/* Desktop table */}
      {!loading && documents && documents.length > 0 && (
        <>
          <div className="mt-6 hidden overflow-x-auto md:block">
            <table className="min-w-full divide-y divide-gray-200">
              <thead className="bg-gray-50">
                <tr>
                  <th className="px-4 py-3 text-left text-xs font-medium uppercase tracking-wider text-gray-500">
                    Název dokumentu
                  </th>
                  <th className="px-4 py-3 text-left text-xs font-medium uppercase tracking-wider text-gray-500">
                    Kategorie
                  </th>
                  <th className="px-4 py-3 text-left text-xs font-medium uppercase tracking-wider text-gray-500">
                    Datum nahrání
                  </th>
                  <th className="px-4 py-3 text-left text-xs font-medium uppercase tracking-wider text-gray-500">
                    Velikost
                  </th>
                  <th className="px-4 py-3 text-right text-xs font-medium uppercase tracking-wider text-gray-500">
                    Akce
                  </th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-200 bg-white">
                {documents.map((doc) => (
                  <tr key={doc.id} className="hover:bg-gray-50">
                    <td className="whitespace-nowrap px-4 py-3 text-sm font-medium text-gray-900">
                      {doc.name}
                    </td>
                    <td className="whitespace-nowrap px-4 py-3 text-sm">
                      <span
                        className={`inline-flex rounded-full px-2 py-0.5 text-xs font-medium ${
                          CATEGORY_COLORS[doc.category] ?? 'bg-gray-100 text-gray-700'
                        }`}
                      >
                        {CATEGORY_LABELS[doc.category] ?? doc.category}
                      </span>
                    </td>
                    <td className="whitespace-nowrap px-4 py-3 text-sm text-gray-500">
                      {formatDate(doc.uploadedAt)}
                    </td>
                    <td className="whitespace-nowrap px-4 py-3 text-sm text-gray-500">
                      {formatFileSize(doc.fileSizeBytes)}
                    </td>
                    <td className="whitespace-nowrap px-4 py-3 text-right text-sm">
                      <button
                        onClick={() => void handleDownload(doc)}
                        className="font-medium text-blue-600 hover:text-blue-800"
                      >
                        Stáhnout
                      </button>
                      {isAdmin && (
                        <button
                          onClick={() => setDeleteTarget(doc)}
                          className="ml-4 font-medium text-red-600 hover:text-red-800"
                        >
                          Smazat
                        </button>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          {/* Mobile cards */}
          <div className="mt-6 space-y-3 md:hidden">
            {documents.map((doc) => (
              <div
                key={doc.id}
                className="rounded-lg bg-white p-4 shadow-sm"
              >
                <div className="flex items-start justify-between">
                  <div className="min-w-0 flex-1">
                    <p className="text-sm font-medium text-gray-900">
                      {doc.name}
                    </p>
                    <div className="mt-1 flex flex-wrap items-center gap-2">
                      <span
                        className={`inline-flex rounded-full px-2 py-0.5 text-xs font-medium ${
                          CATEGORY_COLORS[doc.category] ?? 'bg-gray-100 text-gray-700'
                        }`}
                      >
                        {CATEGORY_LABELS[doc.category] ?? doc.category}
                      </span>
                      <span className="text-xs text-gray-500">
                        {formatDate(doc.uploadedAt)}
                      </span>
                      <span className="text-xs text-gray-500">
                        {formatFileSize(doc.fileSizeBytes)}
                      </span>
                    </div>
                  </div>
                </div>
                <div className="mt-3 flex gap-3">
                  <button
                    onClick={() => void handleDownload(doc)}
                    className="text-sm font-medium text-blue-600 hover:text-blue-800"
                  >
                    Stáhnout
                  </button>
                  {isAdmin && (
                    <button
                      onClick={() => setDeleteTarget(doc)}
                      className="text-sm font-medium text-red-600 hover:text-red-800"
                    >
                      Smazat
                    </button>
                  )}
                </div>
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

      {/* Delete confirmation */}
      <ConfirmDialog
        isOpen={deleteTarget !== null}
        title="Smazat dokument"
        message={`Opravdu chcete smazat dokument "${deleteTarget?.name ?? ''}"?`}
        confirmLabel={deleteLoading ? 'Mažu...' : 'Smazat'}
        confirmVariant="danger"
        onConfirm={() => void handleDeleteConfirm()}
        onCancel={() => setDeleteTarget(null)}
      />
    </div>
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
      setUploadError('Nahrávání se nezdařilo');
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
        className="fixed inset-0 bg-black/50 transition-opacity"
        onClick={onClose}
      />
      <div className="relative z-10 mx-4 w-full max-w-lg rounded-lg bg-white p-6 shadow-xl">
        <h2 className="text-lg font-semibold text-gray-900">Nahrát dokument</h2>

        <div className="mt-4 space-y-4">
          {/* Document name */}
          <div>
            <label
              htmlFor="doc-name"
              className="block text-sm font-medium text-gray-700"
            >
              Název dokumentu
            </label>
            <input
              id="doc-name"
              type="text"
              value={name}
              onChange={(e) => setName(e.target.value)}
              className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-2 text-sm shadow-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
              placeholder="Název dokumentu"
            />
          </div>

          {/* Category select */}
          <div>
            <label
              htmlFor="doc-category"
              className="block text-sm font-medium text-gray-700"
            >
              Kategorie
            </label>
            <select
              id="doc-category"
              value={category}
              onChange={(e) => setCategory(e.target.value)}
              className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-2 text-sm shadow-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
            >
              <option value="stanovy">Stanovy</option>
              <option value="zapisy">Zápisy</option>
              <option value="smlouvy">Smlouvy</option>
              <option value="ostatni">Ostatní</option>
            </select>
          </div>

          {/* File upload */}
          <div>
            <label className="block text-sm font-medium text-gray-700">
              Soubor
            </label>
            <div className="mt-1">
              {file ? (
                <div className="flex items-center justify-between rounded-md border border-gray-300 px-3 py-2">
                  <span className="truncate text-sm text-gray-700">
                    {file.name} ({formatFileSize(file.size)})
                  </span>
                  <button
                    onClick={() => setFile(null)}
                    className="ml-2 text-sm text-red-600 hover:text-red-800"
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
            <div className="rounded-md bg-red-50 p-3">
              <p className="text-sm text-red-700">{uploadError}</p>
            </div>
          )}
        </div>

        {/* Actions */}
        <div className="mt-6 flex justify-end gap-3">
          <button
            onClick={onClose}
            disabled={uploading}
            className="rounded-md border border-gray-300 bg-white px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50"
          >
            Zrušit
          </button>
          <button
            onClick={() => void handleSubmit()}
            disabled={uploading || !file || !name.trim()}
            className="rounded-md bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700 disabled:cursor-not-allowed disabled:opacity-50"
          >
            {uploading ? 'Nahrávám...' : 'Nahrát'}
          </button>
        </div>
      </div>
    </div>
  );
}

