"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { businessesApi, employeesApi } from "@/services/api";
import { useBusinessContextStore } from "@/stores/business-context-store";
import type {
  BusinessAvailabilityBlockPayload,
  EmployeeScheduleExceptionPayload,
  WorkingHour,
} from "@/types/entities";

export const workingHourKeys = {
  all: ["working-hours"] as const,
  business: (businessId: string | null) =>
    [...workingHourKeys.all, "business", businessId] as const,
  businessBlocks: (businessId: string | null) =>
    [...workingHourKeys.all, "business-blocks", businessId] as const,
  employee: (employeeId: string) =>
    [...workingHourKeys.all, "employee", employeeId] as const,
  employeeExceptions: (employeeId: string) =>
    [...workingHourKeys.all, "employee-exceptions", employeeId] as const,
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
      queryClient.invalidateQueries({ queryKey: workingHourKeys.business(businessId) });
    },
  });
}

export function useBusinessAvailabilityBlocks() {
  const businessId = useBusinessContextStore((s) => s.selectedBusinessId);
  return useQuery({
    queryKey: workingHourKeys.businessBlocks(businessId),
    queryFn: () => businessesApi.listAvailabilityBlocks(businessId!),
    enabled: !!businessId,
  });
}

export function useCreateBusinessAvailabilityBlock() {
  const businessId = useBusinessContextStore((s) => s.selectedBusinessId);
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (payload: BusinessAvailabilityBlockPayload) =>
      businessesApi.createAvailabilityBlock(businessId!, payload),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: workingHourKeys.businessBlocks(businessId) });
    },
  });
}

export function useUpdateBusinessAvailabilityBlock() {
  const businessId = useBusinessContextStore((s) => s.selectedBusinessId);
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ blockId, payload }: { blockId: string; payload: BusinessAvailabilityBlockPayload }) =>
      businessesApi.updateAvailabilityBlock(businessId!, blockId, payload),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: workingHourKeys.businessBlocks(businessId) });
    },
  });
}

export function useDeleteBusinessAvailabilityBlock() {
  const businessId = useBusinessContextStore((s) => s.selectedBusinessId);
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (blockId: string) => businessesApi.deleteAvailabilityBlock(businessId!, blockId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: workingHourKeys.businessBlocks(businessId) });
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
      queryClient.invalidateQueries({ queryKey: workingHourKeys.employee(employeeId) });
    },
  });
}

export function useEmployeeScheduleExceptions(employeeId: string) {
  return useQuery({
    queryKey: workingHourKeys.employeeExceptions(employeeId),
    queryFn: () => employeesApi.listScheduleExceptions(employeeId),
    enabled: !!employeeId,
  });
}

export function useCreateEmployeeScheduleException(employeeId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (payload: EmployeeScheduleExceptionPayload) =>
      employeesApi.createScheduleException(employeeId, payload),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: workingHourKeys.employeeExceptions(employeeId) });
    },
  });
}

export function useUpdateEmployeeScheduleException(employeeId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ exceptionId, payload }: { exceptionId: string; payload: EmployeeScheduleExceptionPayload }) =>
      employeesApi.updateScheduleException(employeeId, exceptionId, payload),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: workingHourKeys.employeeExceptions(employeeId) });
    },
  });
}

export function useDeleteEmployeeScheduleException(employeeId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (exceptionId: string) => employeesApi.deleteScheduleException(employeeId, exceptionId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: workingHourKeys.employeeExceptions(employeeId) });
    },
  });
}
