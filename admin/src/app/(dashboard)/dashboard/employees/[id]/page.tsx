"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import { ArrowLeft, Calendar } from "lucide-react";

import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { Avatar, AvatarFallback } from "@/components/ui/avatar";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { formatDate, formatDateTime, getInitials } from "@/lib/utils";
import { ReservationStatusLabels, ReservationStatusColors } from "@/types/enums";
import type { ReservationStatus } from "@/types/enums";

// Mock employee
const MOCK_EMPLOYEE = {
  employeeId: "emp-1",
  name: "María Elena Rodríguez",
  isActive: true,
  createdAt: "2024-01-15T10:00:00Z",
  services: [
    "Spa Bebé Premium",
    "Masaje Relajante",
    "Hidroterapia",
    "Flotación Neonatal",
  ],
};

// Mock recent reservations
const MOCK_RECENT_RESERVATIONS = [
  {
    reservationId: "res-001",
    serviceName: "Spa Bebé Premium",
    clientName: "María González",
    reservationDateTime: "2025-03-16T10:00:00",
    durationMinutes: 60,
    status: 1 as ReservationStatus, // Confirmed
  },
  {
    reservationId: "res-002",
    serviceName: "Masaje Relajante",
    clientName: "Carlos Pérez",
    reservationDateTime: "2025-03-15T14:30:00",
    durationMinutes: 45,
    status: 2 as ReservationStatus, // Completed
  },
  {
    reservationId: "res-003",
    serviceName: "Hidroterapia",
    clientName: "Ana Martínez",
    reservationDateTime: "2025-03-15T09:00:00",
    durationMinutes: 60,
    status: 0 as ReservationStatus, // Pending
  },
  {
    reservationId: "res-004",
    serviceName: "Flotación Neonatal",
    clientName: "Laura Rodríguez",
    reservationDateTime: "2025-03-14T16:00:00",
    durationMinutes: 60,
    status: 2 as ReservationStatus, // Completed
  },
];

export default function EmployeeDetailPage() {
  const params = useParams();
  const id = params.id as string;

  const employee = MOCK_EMPLOYEE;

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
            {employee.services.map((svc) => (
              <Badge key={svc} variant="secondary">
                {svc}
              </Badge>
            ))}
          </div>
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>Reservaciones recientes</CardTitle>
          <p className="text-sm text-muted-foreground">
            Últimas reservaciones asignadas a este empleado
          </p>
        </CardHeader>
        <CardContent>
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Servicio</TableHead>
                <TableHead>Cliente</TableHead>
                <TableHead>Fecha/Hora</TableHead>
                <TableHead>Duración</TableHead>
                <TableHead>Estado</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {MOCK_RECENT_RESERVATIONS.map((r) => (
                <TableRow key={r.reservationId}>
                  <TableCell className="font-medium">{r.serviceName}</TableCell>
                  <TableCell>{r.clientName}</TableCell>
                  <TableCell>{formatDateTime(r.reservationDateTime)}</TableCell>
                  <TableCell>{r.durationMinutes} min</TableCell>
                  <TableCell>
                    <Badge
                      variant="outline"
                      className={ReservationStatusColors[r.status]}
                    >
                      {ReservationStatusLabels[r.status]}
                    </Badge>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </CardContent>
      </Card>
    </div>
  );
}
