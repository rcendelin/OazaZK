import { useCallback, useRef, useState } from 'react';

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
      // Reset input so same file can be re-selected
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
      className={`flex cursor-pointer flex-col items-center justify-center rounded-lg border-2 border-dashed p-10 transition-colors ${
        disabled
          ? 'cursor-not-allowed border-gray-200 bg-gray-50'
          : isDragOver
            ? 'border-blue-400 bg-blue-50'
            : 'border-gray-300 bg-white hover:border-gray-400 hover:bg-gray-50'
      }`}
    >
      <svg
        className="mb-3 h-10 w-10 text-gray-400"
        fill="none"
        viewBox="0 0 24 24"
        strokeWidth="1.5"
        stroke="currentColor"
      >
        <path
          strokeLinecap="round"
          strokeLinejoin="round"
          d="M3 16.5v2.25A2.25 2.25 0 005.25 21h13.5A2.25 2.25 0 0021 18.75V16.5m-13.5-9L12 3m0 0l4.5 4.5M12 3v13.5"
        />
      </svg>
      <p className="text-sm font-medium text-gray-700">
        Přetáhněte soubor sem nebo klikněte
      </p>
      <p className="mt-1 text-xs text-gray-500">Pouze soubory .xlsx</p>
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
