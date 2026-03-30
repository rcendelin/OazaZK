import { useCallback, useRef, useState } from 'react';
import { UploadCloud } from 'lucide-react';

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
  const inputRef = useRef<HTMLInputElement>(null);

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

      const MAX_FILE_SIZE = 20 * 1024 * 1024;
      const file = e.dataTransfer.files[0];
      if (file) {
        if (file.size > MAX_FILE_SIZE) {
          return;
        }
        const extensions = accept.split(',').map(ext => ext.trim().toLowerCase());
        const fileExt = '.' + file.name.split('.').pop()?.toLowerCase();
        if (extensions.some(ext => fileExt === ext || file.type === ext)) {
          onFileSelected(file);
        }
      }
    },
    [accept, disabled, onFileSelected],
  );

  const handleInputChange = useCallback(
    (e: React.ChangeEvent<HTMLInputElement>) => {
      const MAX_FILE_SIZE = 20 * 1024 * 1024;
      const file = e.target.files?.[0];
      if (file && file.size <= MAX_FILE_SIZE) {
        onFileSelected(file);
      }
      if (inputRef.current) {
        inputRef.current.value = '';
      }
    },
    [onFileSelected],
  );

  const handleClick = useCallback(() => {
    if (!disabled) {
      inputRef.current?.click();
    }
  }, [disabled]);

  return (
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
      />
    </div>
  );
}
