"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import { ArrowLeft } from "lucide-react";

import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { PageError } from "@/components/ui/page-error";
import { PageLoading } from "@/components/ui/page-loading";
import { useTenant } from "@/hooks/use-tenants";
import { formatDate } from "@/lib/utils";

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

      <Card>
        <CardHeader><CardTitle>Informacion del tenant</CardTitle></CardHeader>
        <CardContent className="grid gap-4 sm:grid-cols-2">
          <div><p className="text-sm font-medium text-muted-foreground">Nombre</p><p>{tenant.name}</p></div>
          <div><p className="text-sm font-medium text-muted-foreground">Email</p><p>{tenant.email}</p></div>
          <div><p className="text-sm font-medium text-muted-foreground">Estado</p><p>{tenant.isActive ? "Activo" : "Inactivo"}</p></div>
          <div><p className="text-sm font-medium text-muted-foreground">Creado</p><p>{formatDate(tenant.createdAt)}</p></div>
        </CardContent>
      </Card>
    </div>
  );
}
