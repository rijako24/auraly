"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { businessesApi, employeesApi } from "@/services/api";
import { useBusinessContextStore } from "@/stores/business-context-store";
import type { WorkingHour } from "@/types/entities";

export const workingHourKeys = {
  all: ["working-hours"] as const,
  business: (businessId: string | null) =>
    [...workingHourKeys.all, "business", businessId] as const,
  employee: (employeeId: string) =>
    [...workingHourKeys.all, "employee", employeeId] as const,
};

export function useBusinessWorkingHours() {
  const businessId = useBusinessContextStore((s) => s.selectedBusinessId);
  return useQuery({
    queryKey: workingHourKeys.business(businessId),
    queryFn: () => businessesApi.getWorkingHours(businessId!),
    enabled: !!businessId,
  });
}

export function useUpdateBusinessWorkingHours() {
  const businessId = useBusinessContextStore((s) => s.selectedBusinessId);
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (workingHours: WorkingHour[]) =>
      businessesApi.updateWorkingHours(businessId!, workingHours),
    onSuccess: () => {
      queryClient.invalidateQueries({
        queryKey: workingHourKeys.business(businessId),
      });
    },
  });
}

export function useEmployeeWorkingHours(employeeId: string) {
  return useQuery({
    queryKey: workingHourKeys.employee(employeeId),
    queryFn: () => employeesApi.getWorkingHours(employeeId),
    enabled: !!employeeId,
  });
}

export function useUpdateEmployeeWorkingHours(employeeId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (workingHours: WorkingHour[]) =>
      employeesApi.updateWorkingHours(employeeId, workingHours),
    onSuccess: () => {
      queryClient.invalidateQueries({
        queryKey: workingHourKeys.employee(employeeId),
      });
    },
  });
}
