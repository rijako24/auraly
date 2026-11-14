"use client";

import { useMemo } from "react";
import { requireBusinessTimeZone, businessDateInput, businessDateTimeInput, businessDateTimeAt, businessDayBoundary, addCalendarDays } from "@/lib/accounting-document-date";
import { formatDate, formatDateTime } from "@/lib/utils";
import { useBusinessContextStore } from "@/stores/business-context-store";

// Presentation only. The existing context provider owns loading and refresh.
export function useBusinessDates(businessId?: string | null) {
  const timeZone = useBusinessContextStore(state => state.businesses.find(
    item => item.businessId === (businessId ?? state.selectedBusinessId))?.timeZone);
  return useMemo(() => ({
    timeZone,
    formatDate: (value: string | Date) => formatDate(value, requireBusinessTimeZone(timeZone)),
    formatDateTime: (value: string | Date) => formatDateTime(value, requireBusinessTimeZone(timeZone)),
    today: () => businessDateTimeInput(new Date(), timeZone).slice(0, 10),
    nowInput: () => businessDateTimeInput(new Date(), timeZone),
    dateInput: (value: string) => businessDateInput(value, timeZone),
    dateTimeInput: (value: string) => businessDateTimeInput(value, timeZone),
    dateTimeAt: (value: string) => businessDateTimeAt(value, timeZone),
    dayBoundary: (value: string, next = false) => value ? businessDayBoundary(next ? addCalendarDays(value, 1) : value, timeZone) : undefined,
  }), [timeZone]);
}
