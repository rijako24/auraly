"use client";

import { useState, useMemo } from "react";
import Link from "next/link";
import {
  ArrowLeft,
  ChevronLeft,
  ChevronRight,
  CalendarDays,
} from "lucide-react";

import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import {
  ReservationStatus,
  ReservationStatusLabels,
  ReservationStatusColors,
} from "@/types/enums";
import { formatDateTime } from "@/lib/utils";
import { cn } from "@/lib/utils";

interface MockReservation {
  reservationId: string;
  clientName: string;
  serviceName: string;
  reservationDateTime: string;
  status: ReservationStatus;
}

const MOCK_RESERVATIONS: MockReservation[] = [
  {
    reservationId: "res-001",
    clientName: "María González",
    serviceName: "Spa Bebé Premium",
    reservationDateTime: "2025-03-16T10:00:00",
    status: 1,
  },
  {
    reservationId: "res-002",
    clientName: "Carlos Pérez",
    serviceName: "Masaje Relajante",
    reservationDateTime: "2025-03-16T09:30:00",
    status: 0,
  },
  {
    reservationId: "res-003",
    clientName: "Ana Martínez",
    serviceName: "Flotación Neonatal",
    reservationDateTime: "2025-03-16T14:00:00",
    status: 1,
  },
  {
    reservationId: "res-004",
    clientName: "Laura Rodríguez",
    serviceName: "Spa Bebé Premium",
    reservationDateTime: "2025-03-15T11:00:00",
    status: 2,
  },
  {
    reservationId: "res-005",
    clientName: "Pedro Sánchez",
    serviceName: "Hidroterapia",
    reservationDateTime: "2025-03-15T09:00:00",
    status: 2,
  },
  {
    reservationId: "res-006",
    clientName: "Sofía Herrera",
    serviceName: "Spa Bebé Express",
    reservationDateTime: "2025-03-17T10:30:00",
    status: 0,
  },
  {
    reservationId: "res-007",
    clientName: "Miguel Torres",
    serviceName: "Masaje Relajante",
    reservationDateTime: "2025-03-17T15:00:00",
    status: 1,
  },
  {
    reservationId: "res-008",
    clientName: "Elena Vega",
    serviceName: "Flotación Neonatal",
    reservationDateTime: "2025-03-14T16:00:00",
    status: 3,
  },
];

const STATUS_DOT_COLORS: Record<number, string> = {
  [ReservationStatus.Pending]: "bg-yellow-500",
  [ReservationStatus.Confirmed]: "bg-green-500",
  [ReservationStatus.Completed]: "bg-blue-500",
  [ReservationStatus.Cancelled]: "bg-red-500",
  [ReservationStatus.PendingCalendar]: "bg-orange-500",
  [ReservationStatus.OnHold]: "bg-gray-500",
};

const DAYS = ["Dom", "Lun", "Mar", "Mié", "Jue", "Vie", "Sáb"];

export default function ReservationsCalendarPage() {
  const [currentDate, setCurrentDate] = useState(new Date(2025, 2, 1)); // March 2025

  const { days, startOffset, daysInMonth } = useMemo(() => {
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

    return { days, startOffset, daysInMonth };
  }, [currentDate]);

  const reservationsByDate = useMemo(() => {
    const map: Record<string, MockReservation[]> = {};
    MOCK_RESERVATIONS.forEach((r) => {
      const d = new Date(r.reservationDateTime);
      const key = `${d.getFullYear()}-${d.getMonth()}-${d.getDate()}`;
      if (!map[key]) map[key] = [];
      map[key].push(r);
    });
    return map;
  }, []);

  const [selectedDate, setSelectedDate] = useState<{
    year: number;
    month: number;
    day: number;
  } | null>(null);

  const selectedReservations = useMemo(() => {
    if (!selectedDate) return [];
    const key = `${selectedDate.year}-${selectedDate.month}-${selectedDate.day}`;
    return reservationsByDate[key] ?? [];
  }, [selectedDate, reservationsByDate]);

  const goPrev = () => {
    setCurrentDate(
      (d) => new Date(d.getFullYear(), d.getMonth() - 1, 1)
    );
    setSelectedDate(null);
  };

  const goNext = () => {
    setCurrentDate(
      (d) => new Date(d.getFullYear(), d.getMonth() + 1, 1)
    );
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
          <p className="text-muted-foreground">
            Vista mensual de reservaciones
          </p>
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
          <div className="grid grid-cols-7 gap-1 mb-2">
            {DAYS.map((day) => (
              <div
                key={day}
                className="text-center text-xs font-medium text-muted-foreground py-1"
              >
                {day}
              </div>
            ))}
          </div>
          <div className="grid grid-cols-7 gap-1">
            {Array.from({ length: startOffset }).map((_, i) => (
              <div key={`empty-${i}`} className="aspect-square p-1" />
            ))}
            {days.map(({ date }) => {
              const key = `${currentDate.getFullYear()}-${currentDate.getMonth()}-${date}`;
              const reservations = reservationsByDate[key] ?? [];
              const count = reservations.length;
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
                    "aspect-square p-1 rounded-md border text-left text-sm transition-colors hover:bg-muted/50",
                    isToday(date) && "ring-2 ring-primary ring-offset-2",
                    isSelected && "bg-primary/10 border-primary",
                    count > 0 && "bg-muted/30"
                  )}
                >
                  <span className="block font-medium">{date}</span>
                  {count > 0 && (
                    <div className="mt-1 flex flex-wrap gap-0.5">
                      {reservations.slice(0, 3).map((r) => (
                        <div
                          key={r.reservationId}
                          className={cn(
                            "h-1.5 w-1.5 rounded-full flex-shrink-0",
                            STATUS_DOT_COLORS[r.status] ?? "bg-gray-400"
                          )}
                          title={`${r.serviceName} - ${ReservationStatusLabels[r.status as keyof typeof ReservationStatusLabels]}`}
                        />
                      ))}
                      {count > 3 && (
                        <span className="text-[10px] text-muted-foreground">
                          +{count - 3}
                        </span>
                      )}
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
              <p className="text-muted-foreground text-center py-8">
                No hay reservaciones para este día
              </p>
            ) : (
              <div className="space-y-2">
                {selectedReservations.map((r) => (
                  <Link
                    key={r.reservationId}
                    href={`/dashboard/reservations/${r.reservationId}`}
                    className="flex items-center justify-between p-3 rounded-lg border hover:bg-muted/50 transition-colors"
                  >
                    <div>
                      <p className="font-medium">{r.serviceName}</p>
                      <p className="text-sm text-muted-foreground">
                        {r.clientName} • {formatDateTime(r.reservationDateTime)}
                      </p>
                    </div>
                    <Badge
                      variant="secondary"
                      className={cn(
                        ReservationStatusColors[
                          r.status as keyof typeof ReservationStatusColors
                        ]
                      )}
                    >
                      {ReservationStatusLabels[
                        r.status as keyof typeof ReservationStatusLabels
                      ]}
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
