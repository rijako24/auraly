"use client";

import { FiscalOnboardingCard } from "@/components/fiscal/fiscal-onboarding-card";
import { ElectronicPayrollConfigurationCard } from "@/components/fiscal/electronic-payroll-configuration-card";
import { Card, CardContent } from "@/components/ui/card";
import { useAuthStore } from "@/stores/auth-store";
import { useBusinessContextStore } from "@/stores/business-context-store";

export default function FiscalSettingsPage() {
  const businessId = useBusinessContextStore((state) => state.selectedBusinessId);
  const businessName = useBusinessContextStore((state) => state.businesses.find((item) => item.businessId === state.selectedBusinessId)?.name ?? "Sede actual");
  const canManage = useAuthStore((state) => state.user?.permissions.includes("fiscal.configuration.manage") ?? false);
  const canManagePayroll = useAuthStore((state) => state.user?.permissions.includes("payroll.configure") ?? false);
  if (!businessId) return <Card><CardContent className="p-8 text-center text-muted-foreground">Selecciona una sede en la barra superior.</CardContent></Card>;
  return <div className="mx-auto max-w-7xl space-y-6">
    <header className="rounded-3xl bg-gradient-to-r from-slate-950 via-teal-950 to-slate-950 p-7 text-white"><p className="text-xs font-bold uppercase tracking-[.18em] text-teal-300">Control fiscal central</p><h1 className="mt-2 text-3xl font-black">DIAN · {businessName}</h1><p className="mt-2 max-w-3xl text-sm text-slate-300">Administra una sola identidad fiscal para facturación, documento soporte y nómina electrónica.</p></header>
    <FiscalOnboardingCard businessId={businessId} canManage={canManage} />
    <ElectronicPayrollConfigurationCard businessId={businessId} canManage={canManagePayroll} />
  </div>;
}
