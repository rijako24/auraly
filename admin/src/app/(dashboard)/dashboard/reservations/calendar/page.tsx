"use client";

import { useMemo, useState } from "react";
import Link from "next/link";
import {
  ArrowLeft,
  CalendarDays,
  ChevronLeft,
  ChevronRight,
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
import type { Reservation } from "@/types/entities";
import { formatDateTime, cn } from "@/lib/utils";
import { useReservations } from "@/hooks/use-reservations";

const STATUS_DOT_COLORS: Record<number, string> = {
  [ReservationStatus.Pending]: "bg-yellow-500",
  [ReservationStatus.Confirmed]: "bg-green-500",
  [ReservationStatus.Completed]: "bg-blue-500",
  [ReservationStatus.Cancelled]: "bg-red-500",
  [ReservationStatus.PendingCalendar]: "bg-orange-500",
  [ReservationStatus.OnHold]: "bg-gray-500",
};

const DAYS = ["Dom", "Lun", "Mar", "Mie", "Jue", "Vie", "Sab"];

export default function ReservationsCalendarPage() {
  const [currentDate, setCurrentDate] = useState(() => {
    const now = new Date();
    return new Date(now.getFullYear(), now.getMonth(), 1);
  });
  const [selectedDate, setSelectedDate] = useState<{
    year: number;
    month: number;
    day: number;
  } | null>(null);

  const range = useMemo(() => {
    const start = new Date(currentDate.getFullYear(), currentDate.getMonth(), 1);
    const end = new Date(
      currentDate.getFullYear(),
      currentDate.getMonth() + 1,
      0,
      23,
      59,
      59,
      999
    );

    return {
      startDate: start.toISOString(),
      endDate: end.toISOString(),
    };
  }, [currentDate]);

  const { data, isLoading, isError, refetch } = useReservations({
    page: 1,
    pageSize: 500,
    ...range,
  });
  const reservations = useMemo(() => data?.items ?? [], [data?.items]);

  const { days, startOffset } = useMemo(() => {
    const year = currentDate.getFullYear();
    const month = currentDate.getMonth();
    const first = new Date(year, month, 1);
    const last = new Date(year, month + 1, 0);
    const daysInMonth = last.getDate();
    const startOffset = first.getDay();

    const days: { date: number; isCurrentMonth: true }[] = [];
    for (let d = 1; d <= daysInMonth; d++) {
      days.push({ date: d, isCurrentMonth: true });
    }

    return { days, startOffset };
  }, [currentDate]);

  const reservationsByDate = useMemo(() => {
    const map: Record<string, Reservation[]> = {};
    reservations.forEach((reservation) => {
      if (!reservation.reservationDateTime) return;
      const date = new Date(reservation.reservationDateTime);
      const key = `${date.getFullYear()}-${date.getMonth()}-${date.getDate()}`;
      if (!map[key]) map[key] = [];
      map[key].push(reservation);
    });

    Object.values(map).forEach((items) =>
      items.sort((a, b) =>
        (a.reservationDateTime ?? "").localeCompare(b.reservationDateTime ?? "")
      )
    );

    return map;
  }, [reservations]);

  const selectedReservations = useMemo(() => {
    if (!selectedDate) return [];
    const key = `${selectedDate.year}-${selectedDate.month}-${selectedDate.day}`;
    return reservationsByDate[key] ?? [];
  }, [selectedDate, reservationsByDate]);

  const goPrev = () => {
    setCurrentDate((date) => new Date(date.getFullYear(), date.getMonth() - 1, 1));
    setSelectedDate(null);
  };

  const goNext = () => {
    setCurrentDate((date) => new Date(date.getFullYear(), date.getMonth() + 1, 1));
    setSelectedDate(null);
  };

  const goToday = () => {
    const now = new Date();
    setCurrentDate(new Date(now.getFullYear(), now.getMonth(), 1));
    setSelectedDate(null);
  };

  const monthLabel = currentDate.toLocaleDateString("es-CO", {
    month: "long",
    year: "numeric",
  });

  const handleDayClick = (date: number) => {
    setSelectedDate({
      year: currentDate.getFullYear(),
      month: currentDate.getMonth(),
      day: date,
    });
  };

  const today = new Date();
  const isToday = (date: number) =>
    today.getDate() === date &&
    today.getMonth() === currentDate.getMonth() &&
    today.getFullYear() === currentDate.getFullYear();

  if (isLoading) return <PageLoading cards={1} />;
  if (isError) return <PageError onRetry={refetch} />;

  return (
    <div className="space-y-6">
      <div className="flex items-center gap-4">
        <Button variant="ghost" size="icon" asChild>
          <Link href="/dashboard/reservations">
            <ArrowLeft className="h-4 w-4" />
          </Link>
        </Button>
        <div className="flex-1">
          <h1 className="text-2xl font-semibold tracking-tight">
            Calendario de Reservaciones
          </h1>
          <p className="text-muted-foreground">Vista mensual de reservaciones</p>
        </div>
      </div>

      <Card>
        <CardHeader className="flex flex-row items-center justify-between">
          <CardTitle className="flex items-center gap-2">
            <CalendarDays className="h-5 w-5" />
            {monthLabel.charAt(0).toUpperCase() + monthLabel.slice(1)}
          </CardTitle>
          <div className="flex items-center gap-2">
            <Button variant="outline" size="icon" onClick={goPrev}>
              <ChevronLeft className="h-4 w-4" />
            </Button>
            <Button variant="outline" size="sm" onClick={goToday}>
              Hoy
            </Button>
            <Button variant="outline" size="icon" onClick={goNext}>
              <ChevronRight className="h-4 w-4" />
            </Button>
          </div>
        </CardHeader>
        <CardContent>
          <div className="mb-2 grid grid-cols-7 gap-1">
            {DAYS.map((day) => (
              <div
                key={day}
                className="py-1 text-center text-xs font-medium text-muted-foreground"
              >
                {day}
              </div>
            ))}
          </div>
          <div className="grid grid-cols-7 gap-1">
            {Array.from({ length: startOffset }).map((_, index) => (
              <div key={`empty-${index}`} className="aspect-square p-1" />
            ))}
            {days.map(({ date }) => {
              const key = `${currentDate.getFullYear()}-${currentDate.getMonth()}-${date}`;
              const dayReservations = reservationsByDate[key] ?? [];
              const count = dayReservations.length;
              const isSelected =
                selectedDate?.day === date &&
                selectedDate.month === currentDate.getMonth() &&
                selectedDate.year === currentDate.getFullYear();

              return (
                <button
                  key={date}
                  type="button"
                  onClick={() => handleDayClick(date)}
                  className={cn(
                    "aspect-square rounded-md border p-1 text-left text-sm transition-colors hover:bg-muted/50",
                    isToday(date) && "ring-2 ring-primary ring-offset-2",
                    isSelected && "border-primary bg-primary/10",
                    count > 0 && "bg-muted/30"
                  )}
                >
                  <span className="block font-medium">{date}</span>
                  {count > 0 && (
                    <div className="mt-1.5 space-y-1">
                      {dayReservations.slice(0, 4).map((reservation) => (
                        <Link
                          key={reservation.reservationId}
                          href={`/dashboard/reservations/${reservation.reservationId}`}
                          onClick={(event) => event.stopPropagation()}
                          className={cn("block truncate rounded border border-current/15 px-1.5 py-1 text-xs font-medium leading-snug shadow-sm", ReservationStatusColors[reservation.status as keyof typeof ReservationStatusColors])}
                          title={`${reservation.reservationDateTime ? formatDateTime(reservation.reservationDateTime) : "Sin hora"} · ${reservation.serviceName || "Reserva"}`}
                        >
                          {reservation.reservationDateTime ? new Date(reservation.reservationDateTime).toLocaleTimeString("es-CO", { hour: "2-digit", minute: "2-digit" }) : "--:--"} {reservation.serviceName || "Reserva"}
                        </Link>
                      ))}
                      {count > 4 && <span className="block px-1 text-xs font-medium text-primary">+{count - 4} más</span>}
                    </div>
                  )}
                </button>
              );
            })}
          </div>
        </CardContent>
      </Card>

      {selectedDate && (
        <Card>
          <CardHeader>
            <CardTitle>
              Reservaciones del{" "}
              {new Date(
                selectedDate.year,
                selectedDate.month,
                selectedDate.day
              ).toLocaleDateString("es-CO", {
                weekday: "long",
                day: "numeric",
                month: "long",
              })}
            </CardTitle>
          </CardHeader>
          <CardContent>
            {selectedReservations.length === 0 ? (
              <p className="py-8 text-center text-muted-foreground">
                No hay reservaciones para este dia
              </p>
            ) : (
              <div className="space-y-2">
                {selectedReservations.map((reservation) => (
                  <Link
                    key={reservation.reservationId}
                    href={`/dashboard/reservations/${reservation.reservationId}`}
                    className="flex items-center justify-between rounded-lg border p-3 transition-colors hover:bg-muted/50"
                  >
                    <div>
                      <p className="font-medium">
                        {reservation.serviceName || "Sin servicio"}
                      </p>
                      <p className="text-sm text-muted-foreground">
                        {reservation.employeeName || "Sin empleado"} -{" "}
                        {reservation.reservationDateTime
                          ? formatDateTime(reservation.reservationDateTime)
                          : "Sin fecha"}
                      </p>
                    </div>
                    <Badge
                      variant="secondary"
                      className={cn(
                        ReservationStatusColors[
                          reservation.status as keyof typeof ReservationStatusColors
                        ]
                      )}
                    >
                      {ReservationStatusLabels[
                        reservation.status as keyof typeof ReservationStatusLabels
                      ] ?? "Sin estado"}
                    </Badge>
                  </Link>
                ))}
              </div>
            )}
          </CardContent>
        </Card>
      )}

      <div className="flex flex-wrap gap-4 text-sm text-muted-foreground">
        <span className="flex items-center gap-1">
          <span className="h-2 w-2 rounded-full bg-yellow-500" />
          Pendiente
        </span>
        <span className="flex items-center gap-1">
          <span className="h-2 w-2 rounded-full bg-green-500" />
          Confirmada
        </span>
        <span className="flex items-center gap-1">
          <span className="h-2 w-2 rounded-full bg-blue-500" />
          Completada
        </span>
        <span className="flex items-center gap-1">
          <span className="h-2 w-2 rounded-full bg-red-500" />
          Cancelada
        </span>
      </div>
    </div>
  );
}
