"use client";

import { useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { useParams } from "next/navigation";
import { ArrowLeft } from "lucide-react";

import { Avatar, AvatarFallback } from "@/components/ui/avatar";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { PageError } from "@/components/ui/page-error";
import { PageLoading } from "@/components/ui/page-loading";
import { useBusiness } from "@/hooks/use-businesses";
import { formatDate, getInitials } from "@/lib/utils";
import { configurationsApi } from "@/services/api";

function JsonViewer({ value }: { value: string }) {
  try {
    return (
      <pre className="max-h-80 overflow-auto rounded-md bg-muted p-4 text-xs">
        {JSON.stringify(JSON.parse(value), null, 2)}
      </pre>
    );
  } catch {
    return <pre className="overflow-auto rounded-md bg-muted p-4 text-xs">{value}</pre>;
  }
}

export default function BusinessDetailPage() {
  const params = useParams();
  const id = params.id as string;
  const { data: business, isLoading, isError, refetch } = useBusiness(id);
  const { data: configurations } = useQuery({
    queryKey: ["businesses", id, "configurations"],
    queryFn: () => configurationsApi.getBusinessConfigurations(id),
    enabled: !!id,
  });

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

      <Tabs defaultValue="info" className="space-y-4">
        <TabsList>
          <TabsTrigger value="info">Informacion</TabsTrigger>
          <TabsTrigger value="config">Configuracion</TabsTrigger>
        </TabsList>

        <TabsContent value="info">
          <Card>
            <CardHeader><CardTitle>Datos del negocio</CardTitle></CardHeader>
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
        </TabsContent>

        <TabsContent value="config">
          <Card>
            <CardHeader>
              <CardTitle>Configuracion del negocio</CardTitle>
              <p className="text-sm text-muted-foreground">Valores retornados por la API para este negocio</p>
            </CardHeader>
            <CardContent className="space-y-4">
              {Object.entries(configurations?.configurations ?? {}).map(([key, value]) => (
                <div key={key} className="space-y-2 rounded-md border p-4">
                  <p className="text-sm font-medium text-muted-foreground">{key}</p>
                  <JsonViewer value={value} />
                </div>
              ))}
              {Object.keys(configurations?.configurations ?? {}).length === 0 && (
                <p className="text-sm text-muted-foreground">Sin configuraciones disponibles.</p>
              )}
            </CardContent>
          </Card>
        </TabsContent>
      </Tabs>
    </div>
  );
}
