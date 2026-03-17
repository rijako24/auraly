"use client";

import { useQuery } from "@tanstack/react-query";
import { reservationsApi } from "@/services/api";
import { useBusinessContextStore } from "@/stores/business-context-store";
import type { PagedRequest } from "@/types/api";

export const reservationKeys = {
  all: ["reservations"] as const,
  lists: () => [...reservationKeys.all, "list"] as const,
  list: (
    businessId: string | null,
    params?: Partial<PagedRequest> & {
      status?: number;
      fromDate?: string;
      toDate?: string;
    }
  ) => [...reservationKeys.lists(), businessId, params] as const,
  details: () => [...reservationKeys.all, "detail"] as const,
  detail: (id: string) => [...reservationKeys.details(), id] as const,
};

export function useReservations(
  params?: Partial<PagedRequest> & {
    status?: number;
    fromDate?: string;
    toDate?: string;
  }
) {
  const businessId = useBusinessContextStore((s) => s.selectedBusinessId);

  return useQuery({
    queryKey: reservationKeys.list(businessId, params),
    queryFn: () =>
      reservationsApi.list({ ...params, businessId: businessId! }),
    enabled: !!businessId,
  });
}

export function useReservation(id: string) {
  return useQuery({
    queryKey: reservationKeys.detail(id),
    queryFn: () => reservationsApi.getById(id),
    enabled: !!id,
  });
}
