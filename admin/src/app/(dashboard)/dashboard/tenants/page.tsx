"use client";
import { useMemo } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import type { ColumnDef } from "@tanstack/react-table";
import { Plus } from "lucide-react";
import { DataTable } from "@/components/tables/data-table";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { PageLoading } from "@/components/ui/page-loading";
import { PageError } from "@/components/ui/page-error";
import type { Tenant } from "@/types/entities";
import { formatDate } from "@/lib/utils";
import { useTenants } from "@/hooks/use-tenants";
import { useAuthStore } from "@/stores/auth-store";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { PlatformBillingPolicyCard } from "@/components/tenants/platform-billing-policy-card";

export default function TenantsPage() {
  const { data, isLoading, isError, refetch } = useTenants();
  const router = useRouter();
  const canCreateTenant = useAuthStore((state) => state.user?.permissions.includes("tenants.create") ?? false);
  const canManageBillingPolicy = useAuthStore((state) => state.user?.permissions.includes("tenants.billing.policy.manage") ?? false);
  const tenants = data?.items ?? [];
  const columns: ColumnDef<Tenant>[] = useMemo(() => [
    { accessorKey: "name", header: "Nombre", cell: ({ row }) => <div className="font-medium">{row.original.name}</div> },
    { accessorKey: "email", header: "Email" },
    { accessorKey: "isActive", header: "Estado", cell: ({ row }) => <Badge variant={row.original.isActive ? "default" : "secondary"}>{row.original.isActive ? "Activo" : "Inactivo"}</Badge> },
    { accessorKey: "businessCount", header: "Negocios" },
    { id: "users", header: "Usuarios", cell: ({ row }) => `${row.original.activeUserCount} / ${row.original.maximumUsers}` },
    { id: "devices", header: "Cajas", cell: ({ row }) => `${row.original.activeEnrolledDeviceCount} / ${row.original.maximumEnrolledDevices}` },
    { accessorKey: "fiscalCertificateValidTo", header: "Vencimiento certificado DIAN", cell: ({ row }) => {
      const validTo = row.original.fiscalCertificateValidTo;
      if (!validTo) return <span className="text-muted-foreground">Sin certificado</span>;
      const expiring = new Date(validTo).getTime() <= Date.now() + 30 * 24 * 60 * 60 * 1000;
      return <span className={expiring ? "font-semibold text-red-700" : undefined}>{formatDate(validTo)}</span>;
    } },
    { accessorKey: "createdAt", header: "Creado", cell: ({ row }) => formatDate(row.original.createdAt) },
  ], []);

  if (isLoading) return <PageLoading cards={0} />;
  if (isError) return <PageError onRetry={refetch} />;

  return (
    <div className="space-y-6">
      <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
        <div><h1 className="text-2xl font-semibold tracking-tight">Tenants</h1><p className="text-muted-foreground">Gestiona las organizaciones que usan la plataforma</p></div>
        {canCreateTenant && <Button asChild><Link href="/dashboard/tenants/new"><Plus className="mr-2 h-4 w-4" />Nuevo Tenant</Link></Button>}
      </div>
      {canManageBillingPolicy ? <Tabs defaultValue="tenants" className="space-y-5"><TabsList><TabsTrigger value="tenants">Organizaciones</TabsTrigger><TabsTrigger value="billing">Política de cobranza</TabsTrigger></TabsList><TabsContent value="tenants"><DataTable columns={columns} data={tenants} searchKey="name" searchPlaceholder="Buscar por nombre..." enableRowSelection={false} onRowClick={(tenant)=>router.push(`/dashboard/tenants/${tenant.tenantId}`)} /></TabsContent><TabsContent value="billing"><PlatformBillingPolicyCard/></TabsContent></Tabs> : <DataTable columns={columns} data={tenants} searchKey="name" searchPlaceholder="Buscar por nombre..." enableRowSelection={false} onRowClick={(tenant)=>router.push(`/dashboard/tenants/${tenant.tenantId}`)} />}
    </div>
  );
}
