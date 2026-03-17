import { apiClient } from "./client";
import type { PagedResponse, PagedRequest } from "@/types/api";
import type { Reservation, ReservationAddOn } from "@/types/entities";

export const reservationsApi = {
  list: (
    params?: Partial<PagedRequest> & {
      businessId?: string;
      status?: number;
      fromDate?: string;
      toDate?: string;
    }
  ) =>
    apiClient.get<PagedResponse<Reservation>>(
      "/reservations",
      params as Record<string, string | number | undefined>
    ),
  getById: (id: string) =>
    apiClient.get<Reservation & { addOns?: ReservationAddOn[] }>(
      `/reservations/${id}`
    ),
  create: (data: Partial<Reservation> & { addOnIds?: string[] }) =>
    apiClient.post<Reservation>("/reservations", data),
  update: (id: string, data: Partial<Reservation>) =>
    apiClient.put<Reservation>(`/reservations/${id}`, data),
  delete: (id: string) => apiClient.delete(`/reservations/${id}`),
  listAddOns: (reservationId: string) =>
    apiClient.get<ReservationAddOn[]>(`/reservations/${reservationId}/addons`),
  addAddOn: (
    reservationId: string,
    data: { addOnServiceId: string; priceSnapshot: number }
  ) =>
    apiClient.post<ReservationAddOn>(
      `/reservations/${reservationId}/addons`,
      data
    ),
  removeAddOn: (reservationId: string, addOnId: string) =>
    apiClient.delete(`/reservations/${reservationId}/addons/${addOnId}`),
};
