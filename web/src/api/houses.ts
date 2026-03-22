import { apiClient } from './client.ts';
import type { House } from '../types/index.ts';

export const getHouses = (): Promise<House[]> =>
  apiClient.get<House[]>('/houses');

export const getHouse = (id: string): Promise<House> =>
  apiClient.get<House>(`/houses/${encodeURIComponent(id)}`);

export const createHouse = (data: {
  name: string;
  address: string;
  contactPerson: string;
  email: string;
}): Promise<House> =>
  apiClient.post<House>('/houses', data);

export const updateHouse = (id: string, data: {
  name: string;
  address: string;
  contactPerson: string;
  email: string;
  isActive: boolean;
}): Promise<House> =>
  apiClient.put<House>(`/houses/${encodeURIComponent(id)}`, data);
