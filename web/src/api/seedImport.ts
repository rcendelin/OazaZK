import { apiClient, ApiError } from './client.ts';

export interface SeedFileSummary {
  file: string;
  uploaded: boolean;
  rows: number;
  created: number;
  unchanged: number;
  conflicts: number;
  errors: number;
}

export interface SeedIssue {
  file: string;
  line: number | null;
  severity: 'chyba' | 'konflikt';
  message: string;
}

export interface SeedHouseSaldo {
  houseName: string;
  opening: number;
  payments: number;
  costs: number;
  saldo: number;
}

export interface SeedComponentControl {
  componentName: string;
  allocated: number;
  houses: number;
  matches: boolean;
  warnings: string[];
}

export interface SeedImportReport {
  applied: boolean;
  canApply: boolean;
  from: string;
  today: string;
  files: SeedFileSummary[];
  issues: SeedIssue[];
  houses: SeedHouseSaldo[];
  components: SeedComponentControl[];
}

/** Uploaded CSVs by file name → text. */
export type SeedFiles = Record<string, string>;

/** Import nanečisto (T13): nothing is written. */
export const seedDryRun = (files: SeedFiles): Promise<SeedImportReport> =>
  apiClient.post<SeedImportReport>('/seed-import/dry-run', { files });

/** Writes only when the dry run has no error and no conflict. */
export const seedApply = (files: SeedFiles): Promise<SeedImportReport> =>
  apiClient.post<SeedImportReport>('/seed-import/apply', { files });

/** Downloads the dry-run report as Markdown or XLSX. */
export async function downloadSeedReport(
  files: SeedFiles,
  format: 'md' | 'xlsx',
  getToken: () => Promise<string | null>,
): Promise<void> {
  const token = await getToken();
  const baseUrl = import.meta.env.VITE_API_BASE_URL || '/api';
  const response = await fetch(`${baseUrl}/seed-import/report?format=${format}`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', ...(token ? { Authorization: `Bearer ${token}` } : {}) },
    body: JSON.stringify({ files }),
  });
  if (!response.ok) throw new ApiError(response.status, 'Stažení reportu se nezdařilo');
  const disposition = response.headers.get('Content-Disposition') ?? '';
  const fileName = /filename="([^"]+)"/.exec(disposition)?.[1] ?? `import-report.${format}`;
  const url = URL.createObjectURL(await response.blob());
  const a = document.createElement('a');
  a.href = url;
  a.download = fileName;
  document.body.appendChild(a);
  a.click();
  document.body.removeChild(a);
  URL.revokeObjectURL(url);
}

/** Reads a CSV as UTF-8, falling back to Windows-1250 (Czech Excel) when it is not valid UTF-8. */
export async function readCsv(file: File): Promise<string> {
  const bytes = await file.arrayBuffer();
  try {
    return new TextDecoder('utf-8', { fatal: true }).decode(bytes);
  } catch {
    return new TextDecoder('windows-1250').decode(bytes);
  }
}
