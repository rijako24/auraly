"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import { ArrowLeft } from "lucide-react";

import { Avatar, AvatarFallback } from "@/components/ui/avatar";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Switch } from "@/components/ui/switch";
import { PageError } from "@/components/ui/page-error";
import { PageLoading } from "@/components/ui/page-loading";
import { useBusiness, useUpdateBusiness } from "@/hooks/use-businesses";
import { formatDate, getInitials } from "@/lib/utils";
import { toast } from "sonner";

export default function BusinessDetailPage() {
  const params = useParams();
  const id = params.id as string;
  const { data: business, isLoading, isError, refetch } = useBusiness(id);
  const updateBusiness = useUpdateBusiness();

  if (isLoading) return <PageLoading cards={2} />;
  if (isError || !business) return <PageError onRetry={refetch} />;

  return (
    <div className="space-y-6">
      <div className="flex items-center gap-4">
        <Button variant="ghost" size="icon" asChild>
          <Link href="/dashboard/businesses"><ArrowLeft className="h-4 w-4" /></Link>
        </Button>
        <Avatar className="h-14 w-14">
          <AvatarFallback className="text-lg">{getInitials(business.name)}</AvatarFallback>
        </Avatar>
        <div className="flex-1">
          <h1 className="text-2xl font-semibold tracking-tight">{business.name}</h1>
          <p className="text-muted-foreground">{business.description}</p>
        </div>
        <Badge variant={business.isActive ? "default" : "secondary"}>
          {business.isActive ? "Activo" : "Inactivo"}
        </Badge>
      </div>

      <Card>
        <CardHeader><CardTitle>Datos de la sede</CardTitle></CardHeader>
        <CardContent className="grid gap-4 sm:grid-cols-2">
          <div><p className="text-sm font-medium text-muted-foreground">Nombre</p><p>{business.name}</p></div>
          <div><p className="text-sm font-medium text-muted-foreground">Descripcion</p><p>{business.description}</p></div>
          <div><p className="text-sm font-medium text-muted-foreground">Direccion</p><p>{business.address}</p></div>
          <div><p className="text-sm font-medium text-muted-foreground">Telefono</p><p>{business.phone}</p></div>
          <div><p className="text-sm font-medium text-muted-foreground">Email</p><p>{business.email}</p></div>
          <div><p className="text-sm font-medium text-muted-foreground">Sitio web</p><p>{business.website || "Sin sitio web"}</p></div>
          <div><p className="text-sm font-medium text-muted-foreground">Estado</p><p>{business.isActive ? "Activo" : "Inactivo"}</p></div>
          <div><p className="text-sm font-medium text-muted-foreground">Creado</p><p>{formatDate(business.createdAt)}</p></div>
        </CardContent>
      </Card>

      <Card>
        <CardHeader><CardTitle>Precios y costos compartidos</CardTitle></CardHeader>
        <CardContent className="flex items-center justify-between gap-6">
          <div>
            <p className="font-medium">Compartir precios con otras sedes</p>
            <p className="text-sm text-muted-foreground">Al activarlo, esta sede forma parte del grupo que prepara y publica los mismos precios y calcula el costo promedio con las existencias consolidadas del grupo.</p>
          </div>
          <Switch aria-label="Compartir precios con otras sedes" checked={business.sharesProductPrices}
            disabled={updateBusiness.isPending}
            onCheckedChange={(checked) => updateBusiness.mutate({ id, values: { sharesProductPrices: checked } }, {
              onSuccess: () => toast.success(checked ? "La sede ahora comparte precios y costos." : "La sede manejará sus precios y costos de forma independiente."),
              onError: () => toast.error("No fue posible actualizar la configuración de la sede."),
            })} />
        </CardContent>
      </Card>
    </div>
  );
}


