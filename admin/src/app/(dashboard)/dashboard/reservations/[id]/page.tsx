"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import {
  ArrowLeft,
  Calendar,
  Check,
  CheckCircle,
  Clock,
  Package,
  XCircle,
} from "lucide-react";

import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { PageError } from "@/components/ui/page-error";
import { PageLoading } from "@/components/ui/page-loading";
import {
  ReservationStatus,
  ReservationStatusColors,
  ReservationStatusLabels,
} from "@/types/enums";
import { formatDateTime, truncate, cn } from "@/lib/utils";
import { useReservation } from "@/hooks/use-reservations";

export default function ReservationDetailPage() {
  const params = useParams();
  const id = params.id as string;
  const { data: reservation, isLoading, isError, refetch } = useReservation(id);

  if (isLoading) return <PageLoading cards={3} />;
  if (isError || !reservation) return <PageError onRetry={refetch} />;

  const statusKey = reservation.status as keyof typeof ReservationStatusLabels;
  const serviceName =
    reservation.serviceName || reservation.service?.serviceName || "Sin servicio";
  const employeeName =
    reservation.employeeName || reservation.employee?.name || "Sin empleado";

  return (
    <div className="space-y-6">
      <div className="flex items-center gap-4">
        <Button variant="ghost" size="icon" asChild>
          <Link href="/dashboard/reservations">
            <ArrowLeft className="h-4 w-4" />
          </Link>
        </Button>
        <div className="flex flex-1 flex-wrap items-center justify-between gap-4">
          <div>
            <h1 className="text-2xl font-semibold tracking-tight">
              Reservacion {truncate(reservation.reservationId, 12)}
            </h1>
            <p className="text-muted-foreground">Detalle de la reservacion</p>
          </div>
          <Badge
            className={cn(
              "text-sm",
              ReservationStatusColors[statusKey] ??
                "bg-gray-100 text-gray-800 dark:bg-gray-900 dark:text-gray-300"
            )}
          >
            {ReservationStatusLabels[statusKey] ?? "Sin estado"}
          </Badge>
        </div>
      </div>

      <div className="flex flex-wrap gap-2">
        {reservation.status === ReservationStatus.Pending && (
          <Button>
            <CheckCircle className="mr-2 h-4 w-4" />
            Confirmar
          </Button>
        )}
        {reservation.status !== ReservationStatus.Completed &&
          reservation.status !== ReservationStatus.Cancelled && (
            <>
              <Button variant="outline">
                <Check className="mr-2 h-4 w-4" />
                Completar
              </Button>
              <Button variant="destructive">
                <XCircle className="mr-2 h-4 w-4" />
                Cancelar
              </Button>
            </>
          )}
      </div>

      <div className="grid gap-6 lg:grid-cols-2">
        <Card>
          <CardHeader>
            <CardTitle className="flex items-center gap-2">
              <Package className="h-5 w-5" />
              Servicio
            </CardTitle>
          </CardHeader>
          <CardContent className="space-y-4">
            <div className="flex justify-between gap-4">
              <span className="text-muted-foreground">Servicio</span>
              <span className="text-right font-medium">{serviceName}</span>
            </div>
            <div className="flex justify-between gap-4">
              <span className="text-muted-foreground">Empleado</span>
              <span className="text-right">{employeeName}</span>
            </div>
            <div className="flex justify-between gap-4">
              <span className="text-muted-foreground">Fecha y hora</span>
              <span className="text-right">
                {reservation.reservationDateTime
                  ? formatDateTime(reservation.reservationDateTime)
                  : "Sin fecha"}
              </span>
            </div>
            <div className="flex justify-between gap-4">
              <span className="text-muted-foreground">Duracion</span>
              <span>
                {reservation.durationMinutes
                  ? `${reservation.durationMinutes} min`
                  : "Sin duracion"}
              </span>
            </div>
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle className="flex items-center gap-2">
              <Calendar className="h-5 w-5" />
              Registro
            </CardTitle>
          </CardHeader>
          <CardContent className="space-y-4">
            <div className="flex justify-between gap-4">
              <span className="text-muted-foreground">ID</span>
              <span className="break-all text-right font-mono text-sm">
                {reservation.reservationId}
              </span>
            </div>
            <div className="flex justify-between gap-4">
              <span className="text-muted-foreground">Creada</span>
              <span>{formatDateTime(reservation.createdAt)}</span>
            </div>
            <div className="flex justify-between gap-4">
              <span className="text-muted-foreground">Estado</span>
              <span>{ReservationStatusLabels[statusKey] ?? "Sin estado"}</span>
            </div>
            <div className="flex justify-between gap-4">
              <span className="text-muted-foreground">Conversacion</span>
              <span className="break-all text-right font-mono text-sm">
                {reservation.conversationId ?? "Sin conversacion"}
              </span>
            </div>
          </CardContent>
        </Card>
      </div>

      {reservation.addOns && reservation.addOns.length > 0 && (
        <Card>
          <CardHeader>
            <CardTitle className="flex items-center gap-2">
              <Clock className="h-5 w-5" />
              Add-ons
            </CardTitle>
          </CardHeader>
          <CardContent>
            <div className="space-y-2">
              {reservation.addOns.map((addOn) => (
                <div
                  key={addOn.reservationAddOnId}
                  className="flex justify-between gap-4 rounded-md border p-3 text-sm"
                >
                  <span>
                    {addOn.addOnService?.serviceName ?? addOn.addOnServiceId}
                  </span>
                  <span className="text-muted-foreground">
                    {addOn.priceSnapshot}
                  </span>
                </div>
              ))}
            </div>
          </CardContent>
        </Card>
      )}
    </div>
  );
}
