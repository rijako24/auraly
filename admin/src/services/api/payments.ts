import { apiClient, withPagedDefaults } from "./client";
import { PagedResponse, PagedRequest } from "@/types/api";
import { PaymentTransaction } from "@/types/entities";

export const paymentsApi = {
  list: (params?: Partial<PagedRequest> & { businessId?: string; status?: string }) =>
    apiClient.get<PagedResponse<PaymentTransaction>>("/payments", withPagedDefaults(params)),
  getById: (id: string) => apiClient.get<PaymentTransaction>(`/payments/${id}`),
  create: (data: Partial<PaymentTransaction>) => apiClient.post<PaymentTransaction>("/payments", data),
  update: (id: string, data: Partial<PaymentTransaction>) => apiClient.put<PaymentTransaction>(`/payments/${id}`, data),
  delete: (id: string) => apiClient.delete(`/payments/${id}`),
};
