import { apiClient } from './client.ts';

export interface AuditLogEntry {
  id: string;
  timestamp: string;
  userId: string;
  userName: string | null;
  entityType: string;
  entityId: string;
  action: 'Create' | 'Update' | 'Delete' | 'Correction' | string;
  oldValue: string | null;
  newValue: string | null;
  reason: string | null;
}

export interface AuditLogFilter {
  from: string; // yyyy-MM-dd
  to: string; // yyyy-MM-dd
  entityType?: string;
  entityId?: string;
}

export const getAuditLog = (filter: AuditLogFilter): Promise<AuditLogEntry[]> => {
  const params = new URLSearchParams({ from: filter.from, to: filter.to });
  if (filter.entityType) params.set('entityType', filter.entityType);
  if (filter.entityId) params.set('entityId', filter.entityId);
  return apiClient.get<AuditLogEntry[]>(`/audit-log?${params.toString()}`);
};
