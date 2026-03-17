"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import {
  ArrowLeft,
  Calendar,
  Clock,
  CreditCard,
  Package,
  User,
  CheckCircle,
  XCircle,
  Check,
} from "lucide-react";

import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import {
  ReservationStatus,
  ReservationStatusLabels,
  ReservationStatusColors,
} from "@/types/enums";
import { formatCurrency, formatDateTime, truncate } from "@/lib/utils";
import { cn } from "@/lib/utils";

interface MockReservationDetail {
  reservationId: string;
  clientName: string;
  clientPhone: string;
  serviceName: string;
  serviceDuration: number;
  employeeName: string;
  reservationDateTime: string;
  durationMinutes: number;
  status: ReservationStatus;
  addOns: { name: string; price: number }[];
  totalAmount: number;
  paymentStatus: string;
}

const MOCK_RESERVATION: MockReservationDetail = {
  reservationId: "res-abc-001",
  clientName: "María González",
  clientPhone: "+57 300 123 4567",
  serviceName: "Spa Bebé Premium",
  serviceDuration: 90,
  employeeName: "Ana Martínez",
  reservationDateTime: "2025-03-16T10:00:00",
  durationMinutes: 90,
  status: 1, // Confirmed
  addOns: [
    { name: "Fotografía Profesional", price: 2500000 },
    { name: "Aceite de Almendras Premium", price: 1500000 },
  ],
  totalAmount: 16000000, // 160.000 COP in cents
  paymentStatus: "Confirmado",
};

export default function ReservationDetailPage() {
  const params = useParams();
  const id = params.id as string;

  const res = MOCK_RESERVATION;
  const statusKey = res.status as keyof typeof ReservationStatusLabels;

  return (
    <div className="space-y-6">
      <div className="flex items-center gap-4">
        <Button variant="ghost" size="icon" asChild>
          <Link href="/dashboard/reservations">
            <ArrowLeft className="h-4 w-4" />
          </Link>
        </Button>
        <div className="flex-1 flex items-center justify-between flex-wrap gap-4">
          <div>
            <h1 className="text-2xl font-semibold tracking-tight">
              Reservación {truncate(id, 12)}
            </h1>
            <p className="text-muted-foreground">Detalle de la reservación</p>
          </div>
          <Badge
            className={cn(
              "text-sm",
              ReservationStatusColors[statusKey] ??
                "bg-gray-100 text-gray-800 dark:bg-gray-900 dark:text-gray-300"
            )}
          >
            {ReservationStatusLabels[statusKey] ?? "N/A"}
          </Badge>
        </div>
      </div>

      <div className="flex flex-wrap gap-2">
        {res.status === 0 && (
          <Button>
            <CheckCircle className="mr-2 h-4 w-4" />
            Confirmar
          </Button>
        )}
        {res.status !== 2 && res.status !== 3 && (
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
            <div className="flex justify-between">
              <span className="text-muted-foreground">Servicio</span>
              <span className="font-medium">{res.serviceName}</span>
            </div>
            <div className="flex justify-between">
              <span className="text-muted-foreground">Fecha y hora</span>
              <span>{formatDateTime(res.reservationDateTime)}</span>
            </div>
            <div className="flex justify-between">
              <span className="text-muted-foreground">Duración</span>
              <span>{res.durationMinutes} min</span>
            </div>
            <div className="flex justify-between">
              <span className="text-muted-foreground">Empleado</span>
              <span>{res.employeeName}</span>
            </div>
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle className="flex items-center gap-2">
              <User className="h-5 w-5" />
              Cliente
            </CardTitle>
          </CardHeader>
          <CardContent className="space-y-4">
            <div className="flex justify-between">
              <span className="text-muted-foreground">Nombre</span>
              <span className="font-medium">{res.clientName}</span>
            </div>
            <div className="flex justify-between">
              <span className="text-muted-foreground">Teléfono</span>
              <span>{res.clientPhone}</span>
            </div>
            {res.addOns.length > 0 && (
              <div className="pt-2 border-t">
                <p className="text-sm font-medium text-muted-foreground mb-2">
                  Add-ons
                </p>
                <ul className="space-y-1">
                  {res.addOns.map((a) => (
                    <li
                      key={a.name}
                      className="flex justify-between text-sm"
                    >
                      <span>{a.name}</span>
                      <span>{formatCurrency(a.price)}</span>
                    </li>
                  ))}
                </ul>
              </div>
            )}
            <div className="pt-2 border-t flex justify-between font-medium">
              <span>Total</span>
              <span className="text-primary">
                {formatCurrency(res.totalAmount)}
              </span>
            </div>
          </CardContent>
        </Card>
      </div>

      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2">
            <CreditCard className="h-5 w-5" />
            Pago
          </CardTitle>
        </CardHeader>
        <CardContent>
          <div className="flex justify-between items-center">
            <span className="text-muted-foreground">Estado del pago</span>
            <Badge variant="default">{res.paymentStatus}</Badge>
          </div>
        </CardContent>
      </Card>
    </div>
  );
}
