import { apiClient } from "./client";
import type { PagedResponse, PagedRequest } from "@/types/api";
import type { Employee, EmployeeService } from "@/types/entities";

export const employeesApi = {
  list: (params?: Partial<PagedRequest> & { businessId?: string }) =>
    apiClient.get<PagedResponse<Employee>>(
      "/employees",
      params as Record<string, string | number | undefined>
    ),
  getById: (id: string) => apiClient.get<Employee>(`/employees/${id}`),
  create: (data: Partial<Employee>) =>
    apiClient.post<Employee>("/employees", data),
  update: (id: string, data: Partial<Employee>) =>
    apiClient.put<Employee>(`/employees/${id}`, data),
  delete: (id: string) => apiClient.delete(`/employees/${id}`),
  listServices: (employeeId: string) =>
    apiClient.get<EmployeeService[]>(`/employees/${employeeId}/services`),
  assignService: (employeeId: string, serviceId: string) =>
    apiClient.post<EmployeeService>(`/employees/${employeeId}/services`, {
      serviceId,
    }),
  removeService: (employeeId: string, serviceId: string) =>
    apiClient.delete(`/employees/${employeeId}/services/${serviceId}`),
};
