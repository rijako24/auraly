"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { integrationsApi } from "@/services/api";
import { useBusinessContextStore } from "@/stores/business-context-store";
import type {
  UpdateGoogleCalendarIntegration,
  UpdateWompiIntegration,
} from "@/services/api/integrations";

export const integrationKeys = {
  all: ["integrations"] as const,
  settings: (businessId: string | null) =>
    [...integrationKeys.all, "settings", businessId] as const,
};

export function useIntegrationSettings() {
  const businessId = useBusinessContextStore((s) => s.selectedBusinessId);
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
