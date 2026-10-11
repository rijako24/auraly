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
import { useReferenceOptions } from "@/hooks/use-reference-options";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import type { Business } from "@/types/entities";

export default function BusinessDetailPage() {
  const params = useParams();
  const id = params.id as string;
  const { data: business, isLoading, isError, refetch } = useBusiness(id);
  const updateBusiness = useUpdateBusiness();
  const posModes = useReferenceOptions("point-of-sale-mode");

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
        <CardHeader><CardTitle>Tipo de operación</CardTitle></CardHeader>
        <CardContent className="space-y-2">
          <p className="text-sm text-muted-foreground">Define qué pantalla se abre al entrar al punto de venta de esta sede.</p>
          <Select value={business.pointOfSaleMode} onValueChange={(value) => updateBusiness.mutate({ id, values: { pointOfSaleMode: value as Business["pointOfSaleMode"] } }, {
            onSuccess: () => toast.success("Tipo de operación actualizado."),
            onError: () => toast.error("No fue posible actualizar el tipo de punto de venta."),
          })} disabled={posModes.isLoading || posModes.isError || !posModes.data?.length || updateBusiness.isPending}>
            <SelectTrigger className="max-w-sm"><SelectValue placeholder="Selecciona el tipo" /></SelectTrigger>
            <SelectContent>{(posModes.data ?? []).map((mode) => <SelectItem key={mode.id} value={mode.code}>{mode.label}</SelectItem>)}</SelectContent>
          </Select>
          {posModes.isError && <p role="alert" className="text-sm text-destructive">No fue posible cargar los tipos de punto de venta.</p>}
          {business.pointOfSaleMode === "RestaurantBar" && <div className="mt-4 space-y-4 rounded-2xl border p-4">
            <h3 className="font-semibold">Atención en restaurante y bar</h3>
            <label className="flex items-center justify-between gap-4"><span><span className="block font-medium">Pedir clave al seleccionar mesero</span><span className="text-xs text-muted-foreground">Acepta su clave principal o secundaria antes de abrir la mesa.</span></span>
              <Switch checked={business.restaurantRequireWaiterCredential} disabled={updateBusiness.isPending || business.restaurantRestrictTableToWaiter}
                onCheckedChange={(checked) => updateBusiness.mutate({ id, values: { restaurantRequireWaiterCredential: checked } }, {
                  onSuccess: () => toast.success("Atención de meseros actualizada."),
                  onError: () => toast.error("No fue posible actualizar la atención de meseros."),
                })} /></label>
            <label className="flex items-center justify-between gap-4"><span><span className="block font-medium">Restringir mesa al mesero responsable</span><span className="text-xs text-muted-foreground">Exige clave para identificar al mesero.</span></span>
              <Switch checked={business.restaurantRestrictTableToWaiter} disabled={updateBusiness.isPending}
                onCheckedChange={(checked) => updateBusiness.mutate({ id, values: { restaurantRestrictTableToWaiter: checked, ...(checked ? { restaurantRequireWaiterCredential: true } : {}) } }, {
                  onSuccess: () => toast.success("Atención de meseros actualizada."),
                  onError: () => toast.error("No fue posible actualizar la atención de meseros."),
                })} /></label>
          </div>}
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


