"use client";

import { useRouter } from "next/navigation";

import { DailyRouteApp } from "@/components/orders/daily-route-app";
import { PageError } from "@/components/ui/page-error";
import { useBusinessContextStore } from "@/stores/business-context-store";

export default function MyRoutesPage() {
  const router = useRouter();
  const businessId = useBusinessContextStore((state) => state.selectedBusinessId);

  if (!businessId)
    return <PageError message="Selecciona una sede para consultar tus rutas." />;

  return (
    <DailyRouteApp
      businessId={businessId}
      onAdministrative={() => router.push("/dashboard/orders")}
    />
  );
}
