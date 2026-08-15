"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import { ArrowLeft } from "lucide-react";

import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { TenantProvisioningSummary } from "@/components/tenants/tenant-provisioning-summary";
import { TenantGovernancePanel } from "@/components/tenants/tenant-governance-panel";
import { PageError } from "@/components/ui/page-error";
import { PageLoading } from "@/components/ui/page-loading";
import { useTenant } from "@/hooks/use-tenants";

export default function TenantDetailPage() {
  const params = useParams();
  const id = params.id as string;
  const { data: tenant, isLoading, isError, refetch } = useTenant(id);

  if (isLoading) return <PageLoading cards={1} />;
  if (isError || !tenant) return <PageError onRetry={refetch} />;

  return (
    <div className="space-y-6">
      <div className="flex items-center gap-4">
        <Button variant="ghost" size="icon" asChild>
          <Link href="/dashboard/tenants"><ArrowLeft className="h-4 w-4" /></Link>
        </Button>
        <div className="flex-1">
          <h1 className="text-2xl font-semibold tracking-tight">{tenant.name}</h1>
          <p className="text-muted-foreground">Detalle del tenant</p>
        </div>
        <Badge variant={tenant.isActive ? "default" : "secondary"}>
          {tenant.isActive ? "Activo" : "Inactivo"}
        </Badge>
      </div>
      <TenantGovernancePanel tenant={tenant} />

      <TenantProvisioningSummary tenant={tenant} />
    </div>
  );
}
