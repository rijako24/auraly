import { apiClient, withPagedDefaults } from "./client";
import { PagedResponse, PagedRequest } from "@/types/api";
import { PaymentTransaction } from "@/types/entities";

export const paymentsApi = {
  list: (params?: Partial<PagedRequest> & { businessId?: string; status?: string }) =>
    apiClient.get<PagedResponse<PaymentTransaction>>("/payments", withPagedDefaults(params)),
  getById: (id: string) => apiClient.get<PaymentTransaction>(`/payments/${id}`),
  confirmManual: (id: string) =>
    apiClient.post<PaymentTransaction>(`/payments/${id}/confirm-manual`),
};