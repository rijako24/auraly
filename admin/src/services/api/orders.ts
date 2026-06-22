import { apiClient, withPagedDefaults } from "./client";
import type { PagedRequest, PagedResponse } from "@/types/api";
import type { Order, OrderSummary } from "@/types/entities";

export type OrderFilters = Partial<PagedRequest> & {
  businessId?: string;
  customer?: string;
  createdFrom?: string;
  createdTo?: string;
  status?: string;
};

export const ordersApi = {
  list: (params?: OrderFilters) =>
    apiClient.get<PagedResponse<Order>>("/orders", withPagedDefaults(params)),
  summary: (params?: Omit<OrderFilters, "page" | "pageSize" | "sortBy" | "sortDirection">) =>
    apiClient.get<OrderSummary>(
      "/orders/summary",
      params as Record<string, string | number | boolean | undefined> | undefined
    ),
  getById: (id: string) => apiClient.get<Order>(`/orders/${id}`),
};
