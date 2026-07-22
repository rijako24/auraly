"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { integrationsApi } from "@/services/api";
import { useBusinessContextStore } from "@/stores/business-context-store";
import type {
  UpdateOperationalMode,
  UpdateGoogleCalendarIntegration,
  UpdateWompiIntegration,
} from "@/services/api/integrations";

export const integrationKeys = {
  all: ["integrations"] as const,
  settings: (businessId: string | null | undefined) =>
    [...integrationKeys.all, "settings", businessId] as const,
  mantisWarehouses: (businessId: string | null | undefined) =>
    [...integrationKeys.all, "mantis-warehouses", businessId] as const,

};
export function useIntegrationSettings(businessIdOverride?: string | null) {
  const selectedBusinessId = useBusinessContextStore((s) => s.selectedBusinessId);
  const businessId = businessIdOverride ?? selectedBusinessId;
  return useQuery({
    queryKey: integrationKeys.settings(businessId),
    queryFn: () => integrationsApi.getSettings(businessId!),
    enabled: !!businessId,
  });
}

export function useUpdateGoogleCalendarIntegration() {
  const businessId = useBusinessContextStore((s) => s.selectedBusinessId);
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (data: UpdateGoogleCalendarIntegration) =>
      integrationsApi.updateGoogleCalendar(businessId!, data),
    onSuccess: () => {
      queryClient.invalidateQueries({
        queryKey: integrationKeys.settings(businessId),
      });
    },
  });
}

export function useUpdateWompiIntegration() {
  const businessId = useBusinessContextStore((s) => s.selectedBusinessId);
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (data: UpdateWompiIntegration) =>
      integrationsApi.updateWompi(businessId!, data),
    onSuccess: () => {
      queryClient.invalidateQueries({
        queryKey: integrationKeys.settings(businessId),
      });
    },
  });
}

export function useUpdateOperationalMode(businessIdOverride?: string | null) {
  const selectedBusinessId = useBusinessContextStore((s) => s.selectedBusinessId);
  const businessId = businessIdOverride ?? selectedBusinessId;
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (data: UpdateOperationalMode) =>
      integrationsApi.updateOperationalMode(businessId!, data),
    onSuccess: () => {
      queryClient.invalidateQueries({
        queryKey: integrationKeys.settings(businessId),
      });
    },
  });
}

export function useRefreshMantisProduct() {
  const businessId = useBusinessContextStore((s) => s.selectedBusinessId);
  return useMutation({
    mutationFn: (query: string) => {
      if (!businessId) throw new Error("Selecciona un negocio.");
      return integrationsApi.refreshMantisProduct(businessId, query);
    },
  });
}

export function useMantisWarehouses(enabled = true) {
  const businessId = useBusinessContextStore((s) => s.selectedBusinessId);
  return useQuery({
    queryKey: integrationKeys.mantisWarehouses(businessId),
    queryFn: () => integrationsApi.getMantisWarehouses(businessId!),
    enabled: !!businessId && enabled,
  });
}

export function useUpdateMantisWarehouses() {
  const businessId = useBusinessContextStore((s) => s.selectedBusinessId);
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (channels: Parameters<typeof integrationsApi.updateMantisWarehouses>[1]) =>
      integrationsApi.updateMantisWarehouses(businessId!, channels),
    onSuccess: () => queryClient.invalidateQueries({
      queryKey: integrationKeys.mantisWarehouses(businessId),
    }),
  });
}
