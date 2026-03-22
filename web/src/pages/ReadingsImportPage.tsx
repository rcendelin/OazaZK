import { useCallback, useRef, useState } from 'react';
import { FileUploadZone } from '../components/FileUploadZone.tsx';
import { Spinner } from '../components/Spinner.tsx';
import { importReadings, confirmImport, createReading } from '../api/readings.ts';
import { getMeters } from '../api/meters.ts';
import { useApi } from '../hooks/useApi.ts';
import type { ImportPreviewResponse, ImportValidationMessage, WaterMeter } from '../types/index.ts';

const czNumber = new Intl.NumberFormat('cs-CZ', {
  minimumFractionDigits: 1,
  maximumFractionDigits: 1,
});

type ImportState = 'initial' | 'uploading' | 'preview' | 'confirming' | 'success';
type TabMode = 'file' | 'manual';

function ValidationMessages({
  errors,
  warnings,
}: {
  errors: ImportValidationMessage[];
  warnings: ImportValidationMessage[];
}) {
  if (errors.length === 0 && warnings.length === 0) return null;

  return (
    <div className="space-y-3">
      {errors.length > 0 && (
        <div className="rounded-md border border-red-200 bg-red-50 p-4">
          <h4 className="text-sm font-semibold text-red-800">Chyby ({errors.length})</h4>
          <ul className="mt-2 space-y-1">
            {errors.map((msg, i) => (
              <li key={i} className="text-sm text-red-700">
                {msg.row !== null && `Řádek ${msg.row}: `}{msg.message}
              </li>
            ))}
          </ul>
        </div>
      )}
      {warnings.length > 0 && (
        <div className="rounded-md border border-amber-200 bg-amber-50 p-4">
          <h4 className="text-sm font-semibold text-amber-800">Upozornění ({warnings.length})</h4>
          <ul className="mt-2 space-y-1">
            {warnings.map((msg, i) => (
              <li key={i} className="text-sm text-amber-700">
                {msg.row !== null && `Řádek ${msg.row}: `}{msg.message}
              </li>
            ))}
          </ul>
        </div>
      )}
    </div>
  );
}

function PreviewTable({ preview }: { preview: ImportPreviewResponse }) {
  const meterIds = Array.from(
    new Set(preview.rows.flatMap((row) => Object.keys(row.meterValues))),
  );

  return (
    <div className="overflow-x-auto rounded-lg border border-gray-200">
      <table className="w-full text-left text-sm">
        <thead className="bg-gray-50 text-xs uppercase text-gray-500">
          <tr>
            <th className="px-4 py-3">Datum</th>
            {meterIds.map((meterId) => (
              <th key={meterId} className="px-4 py-3">{meterId}</th>
            ))}
          </tr>
        </thead>
        <tbody className="divide-y divide-gray-100">
          {preview.rows.map((row, rowIdx) => (
            <tr key={rowIdx} className="hover:bg-gray-50">
              <td className="px-4 py-3 font-medium text-gray-900">
                {new Intl.DateTimeFormat('cs-CZ').format(new Date(row.readingDate))}
              </td>
              {meterIds.map((meterId) => {
                const value = row.meterValues[meterId];
                return (
                  <td key={meterId} className="px-4 py-3 text-gray-600">
                    {value !== undefined ? czNumber.format(value) : '—'}
                  </td>
                );
              })}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function ManualEntry() {
  const { data: meters, loading } = useApi<WaterMeter[]>(
    useCallback(() => getMeters(), []),
  );

  const [readingDate, setReadingDate] = useState(() => new Date().toISOString().split('T')[0]);
  const [values, setValues] = useState<Record<string, string>>({});
  const [saving, setSaving] = useState(false);
  const [saveError, setSaveError] = useState<string | null>(null);
  const [saveSuccess, setSaveSuccess] = useState<string | null>(null);
  const inFlight = useRef(false);

  const sortedMeters = (meters ?? []).slice().sort((a, b) => {
    if (a.type === 'Main' && b.type !== 'Main') return -1;
    if (a.type !== 'Main' && b.type === 'Main') return 1;
    return (a.name || a.meterNumber).localeCompare(b.name || b.meterNumber);
  });

  const handleValueChange = (meterId: string, val: string) => {
    setValues((prev) => ({ ...prev, [meterId]: val }));
  };

  const handleSave = async () => {
    if (inFlight.current) return;

    const entries = Object.entries(values).filter(([, v]) => v.trim() !== '');
    if (entries.length === 0) {
      setSaveError('Zadejte alespoň jednu hodnotu.');
      return;
    }

    inFlight.current = true;
    setSaving(true);
    setSaveError(null);
    setSaveSuccess(null);

    let savedCount = 0;
    const errors: string[] = [];

    for (const [meterId, rawValue] of entries) {
      const value = parseFloat(rawValue.replace(',', '.'));
      if (isNaN(value) || value < 0) {
        const meter = meters?.find((m) => m.id === meterId);
        errors.push(`Neplatná hodnota pro ${meter?.name || meterId}: ${rawValue}`);
        continue;
      }

      try {
        await createReading({
          meterId,
          readingDate: new Date(readingDate).toISOString(),
          value,
        });
        savedCount++;
      } catch (err) {
        const meter = meters?.find((m) => m.id === meterId);
        errors.push(`${meter?.name || meterId}: ${err instanceof Error ? err.message : 'chyba'}`);
      }
    }

    if (errors.length > 0) {
      setSaveError(`Uloženo ${savedCount} odečtů. Chyby: ${errors.join('; ')}`);
    } else {
      setSaveSuccess(`Úspěšně uloženo ${savedCount} odečtů k datu ${new Intl.DateTimeFormat('cs-CZ').format(new Date(readingDate))}.`);
      setValues({});
    }
    setSaving(false);
    inFlight.current = false;
  };

  if (loading) return <div className="flex justify-center p-8"><Spinner size="lg" /></div>;

  return (
    <div className="space-y-4">
      <div>
        <label className="block text-sm font-medium text-gray-700 mb-1">Datum odečtu</label>
        <input
          type="date"
          value={readingDate}
          onChange={(e) => setReadingDate(e.target.value)}
          className="border rounded-lg px-3 py-2 text-sm focus:ring-2 focus:ring-blue-500 focus:border-blue-500"
        />
      </div>

      <div className="bg-white border rounded-lg overflow-hidden">
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead>
              <tr className="bg-gray-50 border-b">
                <th className="text-left px-4 py-3 font-medium text-gray-700">Typ</th>
                <th className="text-left px-4 py-3 font-medium text-gray-700">Identifikátor</th>
                <th className="text-left px-4 py-3 font-medium text-gray-700">Název</th>
                <th className="text-left px-4 py-3 font-medium text-gray-700">Domácnost</th>
                <th className="text-left px-4 py-3 font-medium text-gray-700 w-40">Stav vodoměru (m³)</th>
              </tr>
            </thead>
            <tbody>
              {sortedMeters.map((meter) => (
                <tr key={meter.id} className={`border-b ${meter.type === 'Main' ? 'bg-blue-50' : 'hover:bg-gray-50'}`}>
                  <td className="px-4 py-2">
                    <span className={`inline-block px-2 py-0.5 rounded-full text-xs font-medium ${meter.type === 'Main' ? 'bg-blue-200 text-blue-800' : 'bg-gray-100 text-gray-600'}`}>
                      {meter.type === 'Main' ? 'Hlavní' : 'Individuální'}
                    </span>
                  </td>
                  <td className="px-4 py-2">
                    <code className="bg-gray-100 px-1.5 py-0.5 rounded text-xs">{meter.meterNumber}</code>
                  </td>
                  <td className="px-4 py-2 font-medium">{meter.name}</td>
                  <td className="px-4 py-2 text-gray-600">{meter.houseName ?? '—'}</td>
                  <td className="px-4 py-2">
                    <input
                      type="text"
                      inputMode="decimal"
                      value={values[meter.id] ?? ''}
                      onChange={(e) => handleValueChange(meter.id, e.target.value)}
                      placeholder="0,0"
                      className="w-full border rounded px-2 py-1.5 text-sm text-right focus:ring-2 focus:ring-blue-500 focus:border-blue-500"
                    />
                  </td>
                </tr>
              ))}
              {sortedMeters.length === 0 && (
                <tr><td colSpan={5} className="px-4 py-8 text-center text-gray-400">Žádné vodoměry. Přidejte je ve Správě vodoměrů.</td></tr>
              )}
            </tbody>
          </table>
        </div>
      </div>

      {saveError && (
        <div className="rounded-md border border-red-200 bg-red-50 p-3">
          <p className="text-sm text-red-700">{saveError}</p>
        </div>
      )}
      {saveSuccess && (
        <div className="rounded-md border border-green-200 bg-green-50 p-3">
          <p className="text-sm text-green-700">{saveSuccess}</p>
        </div>
      )}

      <div className="flex gap-2">
        <button
          onClick={handleSave}
          disabled={saving}
          className="bg-blue-600 text-white px-4 py-2 rounded-lg hover:bg-blue-700 text-sm font-medium disabled:opacity-50"
        >
          {saving ? <span className="flex items-center gap-2"><Spinner size="sm" /> Ukládám...</span> : 'Uložit odečty'}
        </button>
        <button
          onClick={() => { setValues({}); setSaveError(null); setSaveSuccess(null); }}
          className="bg-gray-100 text-gray-700 px-4 py-2 rounded-lg hover:bg-gray-200 text-sm"
        >
          Vymazat
        </button>
      </div>

      <p className="text-xs text-gray-400">
        Zadejte kumulativní stav vodoměru (nikoliv spotřebu). Čárka nebo tečka jako desetinný oddělovač.
        Nevyplněné vodoměry budou přeskočeny.
      </p>
    </div>
  );
}

export function ReadingsImportPage() {
  const [tab, setTab] = useState<TabMode>('file');
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
      setError(err instanceof Error ? err.message : 'Import se nezdařil');
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
      setError(err instanceof Error ? err.message : 'Potvrzení importu se nezdařilo');
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

  const hasErrors = preview !== null && preview.errors.length > 0;

  return (
    <div>
      <h1 className="text-2xl font-bold text-gray-900">Import odečtů</h1>
      <p className="mt-1 text-sm text-gray-500">Zadejte odečty vodoměrů — ze souboru nebo ručně</p>

      {/* Tabs */}
      <div className="mt-4 flex border-b border-gray-200">
        <button
          onClick={() => setTab('file')}
          className={`px-4 py-2 text-sm font-medium border-b-2 transition-colors ${tab === 'file' ? 'border-blue-600 text-blue-600' : 'border-transparent text-gray-500 hover:text-gray-700'}`}
        >
          Import ze souboru
        </button>
        <button
          onClick={() => setTab('manual')}
          className={`px-4 py-2 text-sm font-medium border-b-2 transition-colors ${tab === 'manual' ? 'border-blue-600 text-blue-600' : 'border-transparent text-gray-500 hover:text-gray-700'}`}
        >
          Ruční zadání
        </button>
      </div>

      {/* Manual entry tab */}
      {tab === 'manual' && (
        <div className="mt-6">
          <ManualEntry />
        </div>
      )}

      {/* File import tab */}
      {tab === 'file' && (
        <div>
          {state === 'success' && (
            <div className="mt-6 rounded-md border border-green-200 bg-green-50 p-4">
              <div className="flex items-center gap-2">
                <svg className="h-5 w-5 text-green-600" fill="none" viewBox="0 0 24 24" strokeWidth="2" stroke="currentColor">
                  <path strokeLinecap="round" strokeLinejoin="round" d="M9 12.75L11.25 15 15 9.75M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
                </svg>
                <p className="text-sm font-medium text-green-800">
                  Import byl úspěšný. Importováno {successCount ?? ''} odečtů.
                </p>
              </div>
              <button onClick={handleReset}
                className="mt-3 rounded-md bg-green-600 px-4 py-2 text-sm font-medium text-white hover:bg-green-700">
                Importovat další
              </button>
            </div>
          )}

          {error && (
            <div className="mt-6 rounded-md border border-red-200 bg-red-50 p-4">
              <p className="text-sm text-red-700">{error}</p>
            </div>
          )}

          {(state === 'initial' || state === 'uploading') && (
            <div className="mt-6 space-y-4">
              <FileUploadZone onFileSelected={handleFileSelected} disabled={state === 'uploading'} />
              {selectedFile && (
                <div className="flex items-center justify-between rounded-md border border-gray-200 bg-white px-4 py-3">
                  <span className="text-sm font-medium text-gray-700">{selectedFile.name}</span>
                  <button onClick={handleUpload} disabled={state === 'uploading'}
                    className="rounded-md bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-50">
                    {state === 'uploading' ? <span className="flex items-center gap-2"><Spinner size="sm" /> Nahrávám...</span> : 'Importovat'}
                  </button>
                </div>
              )}
            </div>
          )}

          {(state === 'preview' || state === 'confirming') && preview && (
            <div className="mt-6 space-y-6">
              <ValidationMessages errors={preview.errors} warnings={preview.warnings} />
              <div>
                <h2 className="mb-3 text-lg font-semibold text-gray-900">Náhled importu</h2>
                <PreviewTable preview={preview} />
              </div>
              <div className="flex items-center gap-3">
                <button onClick={handleReset} disabled={state === 'confirming'}
                  className="rounded-md bg-white px-4 py-2 text-sm font-medium text-gray-700 ring-1 ring-gray-300 hover:bg-gray-50 disabled:opacity-50">
                  Zrušit
                </button>
                <button onClick={() => void handleConfirm()} disabled={state === 'confirming' || hasErrors}
                  className="rounded-md bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-50">
                  {state === 'confirming' ? <span className="flex items-center gap-2"><Spinner size="sm" /> Potvrzuji...</span> : 'Potvrdit import'}
                </button>
                {hasErrors && <p className="text-sm text-red-600">Opravte chyby před potvrzením importu</p>}
              </div>
            </div>
          )}
        </div>
      )}
    </div>
  );
}
