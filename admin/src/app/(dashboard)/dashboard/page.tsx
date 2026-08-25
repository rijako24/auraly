"use client";

import TodayReportPage from "./reports/page";
import { BrandedEmptyState } from "@/components/ui/branded-empty-state";
import { useAuthStore } from "@/stores/auth-store";

export default function DashboardPage() {
  const canReadToday = useAuthStore((state) => state.user?.permissions.includes("sales.reports.read") ?? false);
  if (canReadToday) return <TodayReportPage />;
  return <BrandedEmptyState
    title="Tu espacio está listo"
    description="Usa el menú para continuar cuando lo necesites."
  />;
}
