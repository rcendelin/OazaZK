import { apiClient } from './client.ts';

/** Optional modules switched on for this environment (T10 feature flag). */
export interface Features {
  offBookFund: boolean;
}

export const getFeatures = (): Promise<Features> => apiClient.get('/features');
