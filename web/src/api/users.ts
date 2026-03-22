import { apiClient } from './client.ts';
import type { User, UserRole, AuthMethod } from '../types/index.ts';

export const getUsers = (): Promise<User[]> =>
  apiClient.get<User[]>('/users');

export const createUser = (data: {
  name: string;
  email: string;
  role: UserRole;
  houseId: string | null;
  authMethod: AuthMethod;
}): Promise<User> =>
  apiClient.post<User>('/users', data);

export const updateUser = (id: string, data: {
  name: string;
  role?: UserRole;
  houseId?: string | null;
  notificationsEnabled?: boolean;
}): Promise<User> =>
  apiClient.put<User>(`/users/${encodeURIComponent(id)}`, data);
