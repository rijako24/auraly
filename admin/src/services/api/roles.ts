import { apiClient } from "./client";
import type { PagedResponse, PagedRequest } from "@/types/api";
import type { AppRole, Permission, RolePermission } from "@/types/entities";

export const rolesApi = {
  list: (params?: Partial<PagedRequest>) =>
    apiClient.get<PagedResponse<AppRole>>(
      "/roles",
      params as Record<string, string | number | undefined>
    ),
  getById: (id: string) => apiClient.get<AppRole>(`/roles/${id}`),
  create: (data: Partial<AppRole>) => apiClient.post<AppRole>("/roles", data),
  update: (id: string, data: Partial<AppRole>) =>
    apiClient.put<AppRole>(`/roles/${id}`, data),
  delete: (id: string) => apiClient.delete(`/roles/${id}`),

  // Permissions
  listPermissions: (params?: Partial<PagedRequest>) =>
    apiClient.get<PagedResponse<Permission>>(
      "/roles/permissions",
      params as Record<string, string | number | undefined>
    ),
  getPermissionById: (id: string) =>
    apiClient.get<Permission>(`/roles/permissions/${id}`),
  createPermission: (data: Partial<Permission>) =>
    apiClient.post<Permission>("/roles/permissions", data),
  updatePermission: (id: string, data: Partial<Permission>) =>
    apiClient.put<Permission>(`/roles/permissions/${id}`, data),
  deletePermission: (id: string) =>
    apiClient.delete(`/roles/permissions/${id}`),

  // RolePermissions (assign permissions to roles)
  listRolePermissions: (roleId: string, params?: Partial<PagedRequest>) =>
    apiClient.get<PagedResponse<RolePermission>>(
      `/roles/${roleId}/permissions`,
      params as Record<string, string | number | undefined>
    ),
  assignPermission: (roleId: string, permissionId: string) =>
    apiClient.post<RolePermission>(`/roles/${roleId}/permissions`, {
      permissionId,
    }),
  revokePermission: (roleId: string, rolePermissionId: string) =>
    apiClient.delete(`/roles/${roleId}/permissions/${rolePermissionId}`),
};
