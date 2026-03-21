import { apiClient } from './client.ts';
import type { House } from '../types/index.ts';

export const getHouses = (): Promise<House[]> =>
  apiClient.get<House[]>('/houses');
