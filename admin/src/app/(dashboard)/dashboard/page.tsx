"use client";

import CommercialSummaryPage from "./reports/page";
import { BrandedEmptyState } from "@/components/ui/branded-empty-state";
import { useAuthStore } from "@/stores/auth-store";

export default function DashboardPage() {
  const canReadSummary = useAuthStore((state) => state.user?.permissions.includes("sales.reports.read") ?? false);
  if (canReadSummary) return <CommercialSummaryPage />;
  return <BrandedEmptyState
    title="Tu espacio está listo"
    description="Usa el menú para continuar cuando lo necesites."
  />;
}
