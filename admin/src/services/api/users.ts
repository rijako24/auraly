import { apiClient, withPagedDefaults } from "./client";
import { PagedResponse, PagedRequest } from "@/types/api";
import { AppUser, UserRole } from "@/types/entities";

export const usersApi = {
  list: (params?: Partial<PagedRequest> & { tenantId?: string }) =>
    apiClient.get<PagedResponse<AppUser>>("/users", withPagedDefaults(params)),
  getById: (id: string) => apiClient.get<AppUser>(`/users/${id}`),
  create: (data: Partial<AppUser>) => apiClient.post<AppUser>("/users", data),
  update: (id: string, data: Partial<AppUser>) => apiClient.put<AppUser>(`/users/${id}`, data),
  delete: (id: string) => apiClient.delete(`/users/${id}`),
  getRoles: (userId: string) => apiClient.get<UserRole[]>(`/users/${userId}/roles`),
  assignRole: (userId: string, data: { roleId: string; businessId?: string }) =>
    apiClient.post<UserRole>(`/users/${userId}/roles`, data),
  removeRole: (userId: string, userRoleId: string) =>
    apiClient.delete(`/users/${userId}/roles/${userRoleId}`),
};
