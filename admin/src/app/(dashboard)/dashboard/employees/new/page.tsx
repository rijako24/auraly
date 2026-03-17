"use client";

import { useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { ArrowLeft, Check } from "lucide-react";

import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Switch } from "@/components/ui/switch";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Checkbox } from "@/components/ui/checkbox";

// Mock services for multi-select
const MOCK_SERVICES = [
  { serviceId: "svc-1", serviceName: "Spa Bebé Premium" },
  { serviceId: "svc-2", serviceName: "Masaje Relajante" },
  { serviceId: "svc-3", serviceName: "Hidroterapia" },
  { serviceId: "svc-4", serviceName: "Flotación Neonatal" },
  { serviceId: "svc-5", serviceName: "Aceite de Almendras Premium" },
  { serviceId: "svc-6", serviceName: "Fotografía Profesional" },
  { serviceId: "svc-7", serviceName: "Aromaterapia Esencial" },
  { serviceId: "svc-8", serviceName: "Spa Bebé Express" },
  { serviceId: "svc-9", serviceName: "Champú y Peinado" },
];

export default function NewEmployeePage() {
  const router = useRouter();
  const [name, setName] = useState("");
  const [isActive, setIsActive] = useState(true);
  const [selectedServiceIds, setSelectedServiceIds] = useState<Set<string>>(new Set());
  const [errors, setErrors] = useState<Record<string, string>>({});

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
    if (!name.trim()) newErrors.name = "El nombre es requerido";
    if (selectedServiceIds.size === 0) {
      newErrors.services = "Seleccione al menos un servicio";
    }
    setErrors(newErrors);
    return Object.keys(newErrors).length === 0;
  };

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    if (!validate()) return;
    // TODO: API call
    router.push("/dashboard/employees");
  };

  const handleCancel = () => {
    router.push("/dashboard/employees");
  };

  return (
    <div className="space-y-6">
      <div className="flex items-center gap-4">
        <Button variant="ghost" size="icon" asChild>
          <Link href="/dashboard/employees">
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
              Complete los datos básicos
            </p>
          </CardHeader>
          <CardContent className="space-y-6">
            <div className="space-y-2">
              <Label htmlFor="name">Nombre completo</Label>
              <Input
                id="name"
                value={name}
                onChange={(e) => setName(e.target.value)}
                placeholder="Ej: María Elena Rodríguez"
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
              <div className="grid gap-2 rounded-md border p-4 sm:grid-cols-2">
                {MOCK_SERVICES.map((svc) => {
                  const isSelected = selectedServiceIds.has(svc.serviceId);
                  return (
                    <div
                      key={svc.serviceId}
                      className="flex items-center space-x-2"
                    >
                      <Checkbox
                        id={svc.serviceId}
                        checked={isSelected}
                        onCheckedChange={() => toggleService(svc.serviceId)}
                      />
                      <Label
                        htmlFor={svc.serviceId}
                        className="cursor-pointer font-normal"
                      >
                        {svc.serviceName}
                      </Label>
                    </div>
                  );
                })}
              </div>
              <p className="text-xs text-muted-foreground">
                Seleccionados: {selectedServiceIds.size} de {MOCK_SERVICES.length}
              </p>
            </div>
          </CardContent>
        </Card>

        <div className="mt-6 flex gap-4">
          <Button type="submit">
            <Check className="mr-2 h-4 w-4" />
            Crear Empleado
          </Button>
          <Button type="button" variant="outline" onClick={handleCancel}>
            Cancelar
          </Button>
        </div>
      </form>
    </div>
  );
}
