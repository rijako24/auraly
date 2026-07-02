"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import {
  ArrowLeft,
  Calendar,
  DollarSign,
  Package,
} from "lucide-react";

import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { StatCard } from "@/components/cards/stat-card";
import { PageLoading } from "@/components/ui/page-loading";
import { PageError } from "@/components/ui/page-error";
import {
  ServiceTierLabels,
  ServiceTierColors,
  ServiceTypeLabels,
  ServiceTypeColors,
} from "@/types/enums";
import { formatCurrency, cn } from "@/lib/utils";
import { useService } from "@/hooks/use-services";

export default function ServiceDetailPage() {
  const params = useParams();
  const id = params.id as string;

  const { data: service, isLoading, isError, refetch } = useService(id);

  if (isLoading) return <PageLoading cards={3} />;
  if (isError || !service) return <PageError onRetry={refetch} />;

  const tierKey = service.tier as keyof typeof ServiceTierLabels;
  const typeKey = service.serviceType as keyof typeof ServiceTypeLabels;

  return (
    <div className="space-y-6">
      <div className="flex items-center gap-4">
        <Button variant="ghost" size="icon" asChild>
          <Link href="/dashboard/services">
            <ArrowLeft className="h-4 w-4" />
          </Link>
        </Button>
        <div className="flex-1">
          <h1 className="text-2xl font-semibold tracking-tight">
            {service.serviceName}
          </h1>
          <p className="text-muted-foreground">Detalle del servicio</p>
        </div>
      </div>

      <div className="flex flex-wrap gap-2">
        <Badge className={cn(ServiceTierColors[tierKey])}>
          {ServiceTierLabels[tierKey] ?? "N/A"}
        </Badge>
        <Badge className={cn(ServiceTypeColors[typeKey])}>
          {ServiceTypeLabels[typeKey] ?? "N/A"}
        </Badge>
        <Badge variant={service.isActive ? "default" : "secondary"}>
          {service.isActive ? "Activo" : "Inactivo"}
        </Badge>
        {!service.includeInCheckoutTotal && (
          <Badge variant="secondary">No suma al total</Badge>
        )}
      </div>

      {service.description && (
        <p className="text-muted-foreground max-w-2xl">{service.description}</p>
      )}

      {service.keywords && (
        <p className="text-sm text-muted-foreground max-w-2xl">Keywords: {service.keywords}</p>
      )}

      <div className="grid gap-4 sm:grid-cols-3">
        <StatCard
          title="Precio"
          value={formatCurrency(service.price)}
          icon={DollarSign}
        />
        <StatCard
          title="Duración"
          value={`${service.durationMinutes} min`}
          icon={Calendar}
        />
        <StatCard
          title="Categoría"
          value={service.categoryName ?? service.category?.name ?? "—"}
          icon={Package}
        />
      </div>

      <Tabs defaultValue="info" className="space-y-4">
        <TabsList>
          <TabsTrigger value="info">Información</TabsTrigger>
        </TabsList>
        <TabsContent value="info" className="space-y-4">
          <Card>
            <CardHeader>
              <CardTitle>Datos del servicio</CardTitle>
            </CardHeader>
            <CardContent className="space-y-4">
              <div className="grid gap-4 sm:grid-cols-2">
                <div>
                  <p className="text-sm font-medium text-muted-foreground">
                    Nombre
                  </p>
                  <p>{service.serviceName}</p>
                </div>
                <div>
                  <p className="text-sm font-medium text-muted-foreground">
                    Categoría
                  </p>
                  <p>{service.categoryName ?? service.category?.name ?? "—"}</p>
                </div>
                <div>
                  <p className="text-sm font-medium text-muted-foreground">
                    Keywords
                  </p>
                  <p>{service.keywords || "—"}</p>
                </div>
                <div>
                  <p className="text-sm font-medium text-muted-foreground">
                    Duración
                  </p>
                  <p>{service.durationMinutes} minutos</p>
                </div>
                <div>
                  <p className="text-sm font-medium text-muted-foreground">
                    Precio
                  </p>
                  <p>{formatCurrency(service.price)}</p>
                </div>
                <div>
                  <p className="text-sm font-medium text-muted-foreground">
                    Checkout
                  </p>
                  <p>{service.includeInCheckoutTotal ? "Suma al total" : "No suma al total"}</p>
                </div>
                <div>
                  <p className="text-sm font-medium text-muted-foreground">
                    Estado
                  </p>
                  <p>{service.isActive ? "Activo" : "Inactivo"}</p>
                </div>
                <div>
                  <p className="text-sm font-medium text-muted-foreground">
                    Creado
                  </p>
                  <p>{new Date(service.createdAt).toLocaleDateString()}</p>
                </div>
              </div>
            </CardContent>
          </Card>
        </TabsContent>
      </Tabs>
    </div>
  );
}
