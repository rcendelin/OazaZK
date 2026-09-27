import { useState } from 'react';
import type { ReactNode } from 'react';
import { ConfirmDialog } from './ConfirmDialog';

interface ReasonConfirmDialogProps {
  title: string;
  message: ReactNode;
  confirmLabel: string;
  /** Label of the reason field (the reason goes to the audit log). */
  reasonLabel?: string;
  onConfirm: (reason: string | undefined) => void;
  onCancel: () => void;
}

/**
 * A confirmation of a destructive action with an optional reason (sent to the API as `?reason=`).
 * Render it only while open (`{target && <ReasonConfirmDialog … />}`) so the reason starts empty each time.
 */
export function ReasonConfirmDialog({
  title,
  message,
  confirmLabel,
  reasonLabel = 'Důvod (nepovinné, zapíše se do auditu)',
  onConfirm,
  onCancel,
}: ReasonConfirmDialogProps) {
  const [reason, setReason] = useState('');
  return (
    <ConfirmDialog
      isOpen
      title={title}
      message={(
        <div className="space-y-3">
          <div>{message}</div>
          <label className="block text-xs text-text-secondary">
            <span className="mb-1 block">{reasonLabel}</span>
            <input
              value={reason}
              onChange={(e) => setReason(e.target.value)}
              className="w-full rounded-lg border border-border bg-surface-raised px-2 py-1.5 text-sm focus:border-accent focus:ring-2 focus:ring-accent/20"
            />
          </label>
        </div>
      )}
      confirmLabel={confirmLabel}
      confirmVariant="danger"
      onConfirm={() => onConfirm(reason.trim() || undefined)}
      onCancel={onCancel}
    />
  );
}
