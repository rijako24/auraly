import { apiClient, withPagedDefaults } from "./client";
import type { PagedRequest, PagedResponse } from "@/types/api";
import type { Promotion } from "@/types/entities";

export type PromotionPayload = Omit<Promotion, "promotionId" | "createdAt" | "updatedAt">;

export const promotionsApi = {
  list: (businessId: string, params?: Partial<PagedRequest>) =>
    apiClient.get<PagedResponse<Promotion>>(
      `/businesses/${businessId}/promotions`,
      withPagedDefaults(params)
    ),
  getById: (businessId: string, promotionId: string) =>
    apiClient.get<Promotion>(`/businesses/${businessId}/promotions/${promotionId}`),
  create: (businessId: string, data: PromotionPayload) =>
    apiClient.post<Promotion>(`/businesses/${businessId}/promotions`, data),
  update: (businessId: string, promotionId: string, data: Partial<PromotionPayload>) =>
    apiClient.put<Promotion>(`/businesses/${businessId}/promotions/${promotionId}`, data),
  delete: (businessId: string, promotionId: string) =>
    apiClient.delete(`/businesses/${businessId}/promotions/${promotionId}`),
};
