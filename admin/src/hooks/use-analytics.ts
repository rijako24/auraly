"use client";

import { useQuery } from "@tanstack/react-query";
import { dashboardApi } from "@/services/api";

export const analyticsKeys = {
  all: ["analytics"] as const,
  metrics: (period?: string) => [...analyticsKeys.all, "metrics", period] as const,
  customerGrowth: (period?: string) => [...analyticsKeys.all, "customer-growth", period] as const,
  reservationsByDay: (period?: string) => [...analyticsKeys.all, "reservations-by-day", period] as const,
  revenueByCategory: (period?: string) => [...analyticsKeys.all, "revenue-by-category", period] as const,
  leadFunnel: (period?: string) => [...analyticsKeys.all, "lead-funnel", period] as const,
  topPerforming: (period?: string) => [...analyticsKeys.all, "top-performing", period] as const,
};

export function useAnalyticsMetrics(period?: string) {
  return useQuery({
    queryKey: analyticsKeys.metrics(period),
    queryFn: () => dashboardApi.getAnalyticsMetrics(period),
  });
}

export function useCustomerGrowth(period?: string) {
  return useQuery({
    queryKey: analyticsKeys.customerGrowth(period),
    queryFn: () => dashboardApi.getCustomerGrowth(period),
  });
}

export function useReservationsByDay(period?: string) {
  return useQuery({
    queryKey: analyticsKeys.reservationsByDay(period),
    queryFn: () => dashboardApi.getReservationsByDay(period),
  });
}

export function useRevenueByCategory(period?: string) {
  return useQuery({
    queryKey: analyticsKeys.revenueByCategory(period),
    queryFn: () => dashboardApi.getRevenueByCategory(period),
  });
}

export function useLeadFunnel(period?: string) {
  return useQuery({
    queryKey: analyticsKeys.leadFunnel(period),
    queryFn: () => dashboardApi.getLeadFunnel(period),
  });
}

export function useTopPerformingServices(period?: string) {
  return useQuery({
    queryKey: analyticsKeys.topPerforming(period),
    queryFn: () => dashboardApi.getTopPerformingServices(period),
  });
}
