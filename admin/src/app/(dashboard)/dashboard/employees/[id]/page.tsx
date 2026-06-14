"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import { useEffect, useState } from "react";
import { ArrowLeft, Save } from "lucide-react";

import { Avatar, AvatarFallback } from "@/components/ui/avatar";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { PageError } from "@/components/ui/page-error";
import { PageLoading } from "@/components/ui/page-loading";
import { WorkingHoursEditor } from "@/components/settings/working-hours-editor";
import { useEmployee } from "@/hooks/use-employees";
import { useEmployeeWorkingHours, useUpdateEmployeeWorkingHours } from "@/hooks/use-working-hours";
import { useServices } from "@/hooks/use-services";
import { formatDate, getInitials } from "@/lib/utils";
import type { WorkingHour } from "@/types/entities";

export default function EmployeeDetailPage() {
  const params = useParams();
  const id = params.id as string;
  const {
    data: employee,
    isLoading: isEmployeeLoading,
    isError: isEmployeeError,
    refetch: refetchEmployee,
  } = useEmployee(id);
  const { data: employeeHours } = useEmployeeWorkingHours(id);
  const updateHours = useUpdateEmployeeWorkingHours(id);
  const [workingHours, setWorkingHours] = useState<WorkingHour[]>([]);
  const { data: servicesData } = useServices({ page: 1, pageSize: 500 });

  useEffect(() => {
    if (employeeHours) setWorkingHours(employeeHours.workingHours);
  }, [employeeHours]);

  if (isEmployeeLoading) return <PageLoading cards={2} />;
  if (isEmployeeError || !employee) return <PageError onRetry={refetchEmployee} />;

  const serviceNames = (employee.serviceIds ?? [])
    .map((serviceId) => {
      const service = servicesData?.items.find((item) => item.serviceId === serviceId);
      return service?.serviceName ?? serviceId;
    });

  return (
    <div className="space-y-6">
      <div className="flex items-center gap-4">
        <Button variant="ghost" size="icon" asChild>
          <Link href="/dashboard/employees">
            <ArrowLeft className="h-4 w-4" />
          </Link>
        </Button>
        <div className="flex-1">
          <h1 className="text-2xl font-semibold tracking-tight">
            {employee.name}
          </h1>
          <p className="text-muted-foreground">Detalle del empleado</p>
        </div>
      </div>

      <Card>
        <CardHeader>
          <div className="flex items-center gap-4">
            <Avatar className="h-16 w-16">
              <AvatarFallback className="text-xl">
                {getInitials(employee.name)}
              </AvatarFallback>
            </Avatar>
            <div>
              <Badge variant={employee.isActive ? "default" : "secondary"}>
                {employee.isActive ? "Activo" : "Inactivo"}
              </Badge>
              <p className="mt-1 text-sm text-muted-foreground">
                Miembro desde {formatDate(employee.createdAt)}
              </p>
            </div>
          </div>
        </CardHeader>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>Servicios asignados</CardTitle>
          <p className="text-sm text-muted-foreground">
            Servicios que este empleado puede realizar
          </p>
        </CardHeader>
        <CardContent>
          <div className="flex flex-wrap gap-2">
            {serviceNames.map((serviceName) => (
              <Badge key={serviceName} variant="secondary">
                {serviceName}
              </Badge>
            ))}
            {serviceNames.length === 0 && (
              <p className="text-sm text-muted-foreground">
                Sin servicios asignados.
              </p>
            )}
          </div>
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <div className="flex items-start justify-between gap-4">
            <div>
              <CardTitle>Horario</CardTitle>
              <p className="text-sm text-muted-foreground">
                {employeeHours?.usesBusinessFallback
                  ? "Sin horario propio; usa el horario del negocio."
                  : "Horario propio del empleado."}
              </p>
            </div>
            <Button
              onClick={() => updateHours.mutate(workingHours)}
              disabled={updateHours.isPending}
            >
              <Save className="mr-2 h-4 w-4" />
              Guardar
            </Button>
          </div>
        </CardHeader>
        <CardContent>
          <WorkingHoursEditor value={workingHours} onChange={setWorkingHours} />
        </CardContent>
      </Card>
    </div>
  );
}
