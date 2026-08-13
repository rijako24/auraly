"use client";

import { useState } from "react";
import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import { ArrowLeft, Check } from "lucide-react";
import { toast } from "sonner";

import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Checkbox } from "@/components/ui/checkbox";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { PageError } from "@/components/ui/page-error";
import { PageLoading } from "@/components/ui/page-loading";
import { Switch } from "@/components/ui/switch";
import { useServices } from "@/hooks/use-services";
import { employeesApi } from "@/services/api";
import { useBusinessContextStore } from "@/stores/business-context-store";

export default function NewEmployeePage() {
  const router = useRouter();
  const searchParams = useSearchParams();
  const partyId = searchParams.get("partyId") ?? undefined;
  const partyName = searchParams.get("name") ?? "";
  const businessId = useBusinessContextStore((state) => state.selectedBusinessId);
  const { data: servicesData, isLoading, isError, refetch } = useServices({
    page: 1,
    pageSize: 500,
  });
  const [name, setName] = useState(partyName);
  const [isActive, setIsActive] = useState(true);
  const [selectedServiceIds, setSelectedServiceIds] = useState<Set<string>>(
    new Set()
  );
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [isSubmitting, setIsSubmitting] = useState(false);

  const services = servicesData?.items ?? [];

  const toggleService = (serviceId: string) => {
    setSelectedServiceIds((prev) => {
      const next = new Set(prev);
      if (next.has(serviceId)) {
        next.delete(serviceId);
      } else {
        next.add(serviceId);
      }
      return next;
    });
  };

  const validate = () => {
    const newErrors: Record<string, string> = {};
    if (!businessId) newErrors.business = "Este campo es requerido";
    if (!name.trim()) newErrors.name = "Este campo es requerido";
    if (selectedServiceIds.size === 0) {
      newErrors.services = "Este campo es requerido";
    }
    setErrors(newErrors);
    return Object.keys(newErrors).length === 0;
  };

  const handleSubmit = async (event: React.FormEvent) => {
    event.preventDefault();
    if (!validate()) return;

    setIsSubmitting(true);
    try {
      await employeesApi.create({
        businessId: businessId!,
        name: name.trim(),
        partyId,
        isActive,
        serviceIds: Array.from(selectedServiceIds),
      });
      toast.success("Empleado creado");
      router.push("/dashboard/parties");
    } catch {
      toast.error("No se pudo crear el empleado");
    } finally {
      setIsSubmitting(false);
    }
  };

  const handleCancel = () => {
    router.push("/dashboard/parties");
  };

  if (isLoading) return <PageLoading cards={1} />;
  if (isError) return <PageError onRetry={refetch} />;

  return (
    <div className="space-y-6">
      <div className="flex items-center gap-4">
        <Button variant="ghost" size="icon" asChild>
          <Link href="/dashboard/parties">
            <ArrowLeft className="h-4 w-4" />
          </Link>
        </Button>
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">
            Nuevo Empleado
          </h1>
          <p className="text-muted-foreground">
            Registrar un nuevo empleado en el sistema
          </p>
        </div>
      </div>

      <form onSubmit={handleSubmit}>
        <Card>
          <CardHeader>
            <CardTitle>Datos del empleado</CardTitle>
            <p className="text-sm text-muted-foreground">
              Complete los datos basicos
            </p>
          </CardHeader>
          <CardContent className="space-y-6">
            {errors.business && (
              <p className="text-sm text-destructive">{errors.business}</p>
            )}
            <div className="space-y-2">
              <Label htmlFor="name">Nombre completo</Label>
              <Input
                id="name"
                value={name}
                onChange={(event) => setName(event.target.value)}
                placeholder="Nombre del empleado"
                className={errors.name ? "border-destructive" : ""}
              />
              {errors.name && (
                <p className="text-sm text-destructive">{errors.name}</p>
              )}
            </div>

            <div className="flex items-center gap-2">
              <Switch
                id="isActive"
                checked={isActive}
                onCheckedChange={setIsActive}
              />
              <Label htmlFor="isActive">Activo</Label>
            </div>

            <div className="space-y-2">
              <Label>Servicios asignados</Label>
              {errors.services && (
                <p className="text-sm text-destructive">{errors.services}</p>
              )}
              <div className={`grid gap-2 rounded-md border p-4 sm:grid-cols-2 ${errors.services ? "border-destructive ring-1 ring-destructive/20" : ""}`}>
                {services.map((service) => {
                  const isSelected = selectedServiceIds.has(service.serviceId);
                  return (
                    <div
                      key={service.serviceId}
                      className="flex items-center space-x-2"
                    >
                      <Checkbox
                        id={service.serviceId}
                        checked={isSelected}
                        onCheckedChange={() => toggleService(service.serviceId)}
                      />
                      <Label
                        htmlFor={service.serviceId}
                        className="cursor-pointer font-normal"
                      >
                        {service.serviceName}
                      </Label>
                    </div>
                  );
                })}
                {services.length === 0 && (
                  <p className="text-sm text-muted-foreground">
                    No hay servicios disponibles para este negocio.
                  </p>
                )}
              </div>
              <p className="text-xs text-muted-foreground">
                Seleccionados: {selectedServiceIds.size} de {services.length}
              </p>
            </div>
          </CardContent>
        </Card>

        <div className="mt-6 flex gap-4">
          <Button type="submit" disabled={isSubmitting || services.length === 0}>
            <Check className="mr-2 h-4 w-4" />
            {isSubmitting ? "Creando..." : "Crear Empleado"}
          </Button>
          <Button type="button" variant="outline" onClick={handleCancel}>
            Cancelar
          </Button>
        </div>
      </form>
    </div>
  );
}
