import { useCallback, useRef, useState } from 'react';
import { FileUploadZone } from '../components/FileUploadZone.tsx';
import { Spinner } from '../components/Spinner.tsx';
import { importReadings, confirmImport } from '../api/readings.ts';
import type { ImportPreviewResponse, ImportValidationMessage } from '../types/index.ts';

const czNumber = new Intl.NumberFormat('cs-CZ', {
  minimumFractionDigits: 1,
  maximumFractionDigits: 1,
});

type ImportState = 'initial' | 'uploading' | 'preview' | 'confirming' | 'success';

function ValidationMessages({ messages }: { messages: ImportValidationMessage[] }) {
  const errors = messages.filter((m) => m.type === 'Error');
  const warnings = messages.filter((m) => m.type === 'Warning');

  if (messages.length === 0) return null;

  return (
    <div className="space-y-3">
      {errors.length > 0 && (
        <div className="rounded-md border border-red-200 bg-red-50 p-4">
          <h4 className="text-sm font-semibold text-red-800">
            Chyby ({errors.length})
          </h4>
          <ul className="mt-2 space-y-1">
            {errors.map((msg, i) => (
              <li key={i} className="text-sm text-red-700">
                {msg.row !== null && `Řádek ${msg.row}: `}
                {msg.column !== null && `[${msg.column}] `}
                {msg.message}
              </li>
            ))}
          </ul>
        </div>
      )}
      {warnings.length > 0 && (
        <div className="rounded-md border border-amber-200 bg-amber-50 p-4">
          <h4 className="text-sm font-semibold text-amber-800">
            Upozornění ({warnings.length})
          </h4>
          <ul className="mt-2 space-y-1">
            {warnings.map((msg, i) => (
              <li key={i} className="text-sm text-amber-700">
                {msg.row !== null && `Řádek ${msg.row}: `}
                {msg.column !== null && `[${msg.column}] `}
                {msg.message}
              </li>
            ))}
          </ul>
        </div>
      )}
    </div>
  );
}

function PreviewTable({ preview }: { preview: ImportPreviewResponse }) {
  return (
    <div className="overflow-x-auto rounded-lg border border-gray-200">
      <table className="w-full text-left text-sm">
        <thead className="bg-gray-50 text-xs uppercase text-gray-500">
          <tr>
            <th className="px-4 py-3">Datum</th>
            {preview.meters.map((meter) => (
              <th key={meter.meterId} className="px-4 py-3">
                {meter.meterType === 'Main'
                  ? 'Hlavní vodoměr'
                  : meter.houseName ?? meter.meterNumber}
              </th>
            ))}
          </tr>
        </thead>
        <tbody className="divide-y divide-gray-100">
          {preview.rows.map((row, rowIdx) => {
            // Check for messages associated with this row
            const rowMessages = preview.messages.filter(
              (m) => m.row === rowIdx + 1,
            );

            return (
              <tr key={rowIdx} className="hover:bg-gray-50">
                <td className="px-4 py-3 font-medium text-gray-900">
                  {row.date}
                </td>
                {preview.meters.map((meter) => {
                  const value = row.readings[meter.meterId];
                  const cellMsg = rowMessages.find(
                    (m) => m.column === meter.meterNumber,
                  );
                  const isError = cellMsg?.type === 'Error';
                  const isWarning = cellMsg?.type === 'Warning';

                  return (
                    <td
                      key={meter.meterId}
                      className={`px-4 py-3 ${
                        isError
                          ? 'bg-red-50 text-red-800'
                          : isWarning
                            ? 'bg-amber-50 text-amber-800'
                            : 'text-gray-600'
                      }`}
                      title={cellMsg?.message}
                    >
                      {value !== undefined ? czNumber.format(value) : '\u2014'}
                    </td>
                  );
                })}
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}

export function ReadingsImportPage() {
  const [state, setState] = useState<ImportState>('initial');
  const [selectedFile, setSelectedFile] = useState<File | null>(null);
  const [preview, setPreview] = useState<ImportPreviewResponse | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [successCount, setSuccessCount] = useState<number | null>(null);
  const inFlight = useRef(false);

  const handleFileSelected = useCallback((file: File) => {
    setSelectedFile(file);
    setError(null);
  }, []);

  const handleUpload = useCallback(async () => {
    if (!selectedFile || inFlight.current) return;
    inFlight.current = true;

    setState('uploading');
    setError(null);

    try {
      const result = await importReadings(selectedFile);
      setPreview(result);
      setState('preview');
    } catch (err: unknown) {
      const message =
        err instanceof Error ? err.message : 'Import se nezdařil';
      setError(message);
      setState('initial');
    } finally {
      inFlight.current = false;
    }
  }, [selectedFile]);

  const handleConfirm = useCallback(async () => {
    if (!preview || inFlight.current) return;
    inFlight.current = true;

    setState('confirming');
    setError(null);

    try {
      const result = await confirmImport(preview.importSessionId);
      setSuccessCount(result.count);
      setState('success');
    } catch (err: unknown) {
      const message =
        err instanceof Error ? err.message : 'Potvrzení importu se nezdařilo';
      setError(message);
      setState('preview');
    } finally {
      inFlight.current = false;
    }
  }, [preview]);

  const handleReset = useCallback(() => {
    setState('initial');
    setSelectedFile(null);
    setPreview(null);
    setError(null);
    setSuccessCount(null);
  }, []);

  return (
    <div>
      <h1 className="text-2xl font-bold text-gray-900">Import odečtů</h1>
      <p className="mt-1 text-sm text-gray-500">
        Nahrajte soubor Excel (.xlsx) s odečty vodoměrů
      </p>

      {/* Success message */}
      {state === 'success' && (
        <div className="mt-6 rounded-md border border-green-200 bg-green-50 p-4">
          <div className="flex items-center gap-2">
            <svg
              className="h-5 w-5 text-green-600"
              fill="none"
              viewBox="0 0 24 24"
              strokeWidth="2"
              stroke="currentColor"
            >
              <path
                strokeLinecap="round"
                strokeLinejoin="round"
                d="M9 12.75L11.25 15 15 9.75M21 12a9 9 0 11-18 0 9 9 0 0118 0z"
              />
            </svg>
            <p className="text-sm font-medium text-green-800">
              Import byl úspěšný. Importováno{' '}
              {successCount !== null ? successCount : ''} odečtů.
            </p>
          </div>
          <button
            onClick={handleReset}
            className="mt-3 rounded-md bg-green-600 px-4 py-2 text-sm font-medium text-white transition-colors hover:bg-green-700"
          >
            Importovat další
          </button>
        </div>
      )}

      {/* Error message */}
      {error && (
        <div className="mt-6 rounded-md border border-red-200 bg-red-50 p-4">
          <p className="text-sm text-red-700">{error}</p>
        </div>
      )}

      {/* Upload zone */}
      {(state === 'initial' || state === 'uploading') && (
        <div className="mt-6 space-y-4">
          <FileUploadZone
            onFileSelected={handleFileSelected}
            disabled={state === 'uploading'}
          />

          {selectedFile && (
            <div className="flex items-center justify-between rounded-md border border-gray-200 bg-white px-4 py-3">
              <div className="flex items-center gap-3">
                <svg
                  className="h-5 w-5 text-green-500"
                  fill="none"
                  viewBox="0 0 24 24"
                  strokeWidth="2"
                  stroke="currentColor"
                >
                  <path
                    strokeLinecap="round"
                    strokeLinejoin="round"
                    d="M19.5 14.25v-2.625a3.375 3.375 0 00-3.375-3.375h-1.5A1.125 1.125 0 0113.5 7.125v-1.5a3.375 3.375 0 00-3.375-3.375H8.25m2.25 0H5.625c-.621 0-1.125.504-1.125 1.125v17.25c0 .621.504 1.125 1.125 1.125h12.75c.621 0 1.125-.504 1.125-1.125V11.25a9 9 0 00-9-9z"
                  />
                </svg>
                <span className="text-sm font-medium text-gray-700">
                  {selectedFile.name}
                </span>
              </div>
              <button
                onClick={handleUpload}
                disabled={state === 'uploading'}
                className="rounded-md bg-blue-600 px-4 py-2 text-sm font-medium text-white transition-colors hover:bg-blue-700 disabled:opacity-50"
              >
                {state === 'uploading' ? (
                  <span className="flex items-center gap-2">
                    <Spinner size="sm" />
                    Nahrávám...
                  </span>
                ) : (
                  'Importovat'
                )}
              </button>
            </div>
          )}
        </div>
      )}

      {/* Preview */}
      {(state === 'preview' || state === 'confirming') && preview && (
        <div className="mt-6 space-y-6">
          {/* Validation messages */}
          <ValidationMessages messages={preview.messages} />

          {/* Preview table */}
          <div>
            <h2 className="mb-3 text-lg font-semibold text-gray-900">
              Náhled importu
            </h2>
            <PreviewTable preview={preview} />
          </div>

          {/* Action buttons */}
          <div className="flex items-center gap-3">
            <button
              onClick={handleReset}
              disabled={state === 'confirming'}
              className="rounded-md bg-white px-4 py-2 text-sm font-medium text-gray-700 ring-1 ring-gray-300 transition-colors hover:bg-gray-50 disabled:opacity-50"
            >
              Zrušit
            </button>
            <button
              onClick={() => void handleConfirm()}
              disabled={state === 'confirming' || preview.hasErrors}
              className="rounded-md bg-blue-600 px-4 py-2 text-sm font-medium text-white transition-colors hover:bg-blue-700 disabled:opacity-50"
            >
              {state === 'confirming' ? (
                <span className="flex items-center gap-2">
                  <Spinner size="sm" />
                  Potvrzuji...
                </span>
              ) : (
                'Potvrdit import'
              )}
            </button>
            {preview.hasErrors && (
              <p className="text-sm text-red-600">
                Opravte chyby před potvrzením importu
              </p>
            )}
          </div>
        </div>
      )}
    </div>
  );
}
