import { useCallback, useRef, useState } from 'react';
import { UploadCloud } from 'lucide-react';

/** Documents and invoice attachments: at most 20 MB (the API refuses bigger files too). */
const MAX_FILE_SIZE = 20 * 1024 * 1024;

interface FileUploadZoneProps {
  onFileSelected: (file: File) => void;
  accept?: string;
  disabled?: boolean;
}

export function FileUploadZone({
  onFileSelected,
  accept = '.xlsx',
  disabled = false,
}: FileUploadZoneProps) {
  const [isDragOver, setIsDragOver] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const inputRef = useRef<HTMLInputElement>(null);

  /** Hands the file over, or explains why it was refused (never ignore a file silently). */
  const pick = useCallback(
    (file: File) => {
      if (file.size > MAX_FILE_SIZE) {
        setError('Soubor je větší než 20 MB.');
        return;
      }
      const extensions = accept.split(',').map((ext) => ext.trim().toLowerCase());
      const fileExt = '.' + (file.name.split('.').pop()?.toLowerCase() ?? '');
      if (!extensions.some((ext) => fileExt === ext || file.type === ext)) {
        setError(`Tento typ souboru nelze nahrát (povoleno: ${extensions.join(', ')}).`);
        return;
      }
      setError(null);
      onFileSelected(file);
    },
    [accept, onFileSelected],
  );

  const handleDragOver = useCallback(
    (e: React.DragEvent) => {
      e.preventDefault();
      e.stopPropagation();
      if (!disabled) {
        setIsDragOver(true);
      }
    },
    [disabled],
  );

  const handleDragLeave = useCallback((e: React.DragEvent) => {
    e.preventDefault();
    e.stopPropagation();
    setIsDragOver(false);
  }, []);

  const handleDrop = useCallback(
    (e: React.DragEvent) => {
      e.preventDefault();
      e.stopPropagation();
      setIsDragOver(false);

      if (disabled) return;

      const file = e.dataTransfer.files[0];
      if (file) pick(file);
    },
    [disabled, pick],
  );

  const handleInputChange = useCallback(
    (e: React.ChangeEvent<HTMLInputElement>) => {
      const file = e.target.files?.[0];
      if (file) pick(file);
      if (inputRef.current) {
        inputRef.current.value = '';
      }
    },
    [pick],
  );

  const handleClick = useCallback(() => {
    if (!disabled) {
      inputRef.current?.click();
    }
  }, [disabled]);

  return (
    <div>
    <div
      onDragOver={handleDragOver}
      onDragLeave={handleDragLeave}
      onDrop={handleDrop}
      onClick={handleClick}
      className={`flex cursor-pointer flex-col items-center justify-center rounded-2xl border-2 border-dashed p-10 transition-all duration-200 ${
        disabled
          ? 'cursor-not-allowed border-border bg-surface-sunken opacity-60'
          : isDragOver
            ? 'border-accent bg-accent-light/50 scale-[1.01]'
            : 'border-border-strong bg-surface-raised hover:border-accent hover:bg-accent-light/20'
      }`}
    >
      <div className={`mb-4 flex h-14 w-14 items-center justify-center rounded-2xl transition-colors ${
        isDragOver ? 'bg-accent text-white' : 'bg-surface-sunken text-text-muted'
      }`}>
        <UploadCloud size={28} />
      </div>
      <p className="text-sm font-medium text-text-primary">
        Přetáhněte soubor sem nebo klikněte
      </p>
      <p className="mt-1.5 text-xs text-text-muted">
        Maximální velikost 20 MB
      </p>
      <input
        ref={inputRef}
        type="file"
        accept={accept}
        onChange={handleInputChange}
        className="hidden"
        data-testid="file-upload-input"
      />
    </div>
    {error && <p role="alert" className="mt-2 text-sm text-danger">{error}</p>}
    </div>
  );
}
