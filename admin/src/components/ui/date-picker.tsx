"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import { CalendarDays, ChevronLeft, ChevronRight } from "lucide-react";

import { Button } from "@/components/ui/button";
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover";
import { cn } from "@/lib/utils";

const MONTHS = ["enero", "febrero", "marzo", "abril", "mayo", "junio", "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre"];
const WEEKDAYS = ["Lu", "Ma", "Mi", "Ju", "Vi", "Sá", "Do"];

interface DatePickerProps {
  value?: string;
  onChange?: (value: string) => void;
  placeholder?: string;
  disabled?: boolean;
  className?: string;
  min?: string;
  max?: string;
  id?: string;
  today?: string;
}

export function DatePicker({ value, onChange, placeholder = "Selecciona una fecha", disabled, className, min, max, id, today }: DatePickerProps) {
  const selected = value ? parseDate(value) : undefined;
  const now = new Date();
  const todayDate = today ? parseDate(today) : calendarDate(now.getFullYear(), now.getMonth(), now.getDate());
  const [open, setOpen] = useState(false);
  const calendar = useRef<HTMLDivElement>(null);
  const trigger = useRef<HTMLButtonElement>(null);
  const [month, setMonth] = useState(() => startOfMonth(selected ?? todayDate));
  const days = useMemo(() => calendarDays(month), [month]);

  useEffect(() => {
    if (value) setMonth(startOfMonth(parseDate(value)));
    else if (today) setMonth(startOfMonth(parseDate(today)));
  }, [value, today]);

  useEffect(() => {
    if (!open) return;
    // Dialog and Popover currently resolve different Radix dismissable layers.
    // Handle the focused calendar first so Escape never discards its parent form.
    const closeCalendar = (event: KeyboardEvent) => {
      if (event.key !== "Escape" || !(event.target instanceof Node) || !calendar.current?.contains(event.target)) return;
      event.preventDefault();
      event.stopPropagation();
      setOpen(false);
    };
    window.addEventListener("keydown", closeCalendar, true);
    return () => window.removeEventListener("keydown", closeCalendar, true);
  }, [open]);

  const choose = (date: Date) => {
    const input = toInputValue(date);
    if (disabled || (min && input < min) || (max && input > max)) return;
    onChange?.(toInputValue(date));
    setOpen(false);
  };

  return (
    <Popover modal open={open} onOpenChange={setOpen}>
      <PopoverTrigger asChild>
        <Button ref={trigger} id={id} type="button" variant="outline" disabled={disabled} className={cn("w-full justify-start gap-2 px-3 font-normal", !value && "text-muted-foreground", className)}>
          <CalendarDays className="h-4 w-4 text-primary" />
          {selected ? formatLongDate(selected) : placeholder}
        </Button>
      </PopoverTrigger>
      <PopoverContent ref={calendar} portalContainer={trigger.current?.closest<HTMLElement>("[role='dialog']") ?? undefined} align="start" className="w-[19rem] rounded-xl border-border/80 p-3 shadow-xl">
        <div className="mb-3 flex items-center justify-between">
          <Button type="button" variant="ghost" size="icon" className="h-8 w-8" onClick={() => setMonth(previousMonth(month))} aria-label="Mes anterior">
            <ChevronLeft className="h-4 w-4" />
          </Button>
          <p className="text-sm font-semibold capitalize">{MONTHS[month.getUTCMonth()]} de {month.getUTCFullYear()}</p>
          <Button type="button" variant="ghost" size="icon" className="h-8 w-8" onClick={() => setMonth(nextMonth(month))} aria-label="Mes siguiente">
            <ChevronRight className="h-4 w-4" />
          </Button>
        </div>
        <div className="grid grid-cols-7 gap-1 text-center text-xs">
          {WEEKDAYS.map((day) => <span key={day} className="py-1 font-medium text-muted-foreground">{day}</span>)}
          {days.map((date, index) => {
            const selectedDay = selected && sameDay(date, selected);
            const isCurrentMonth = date.getUTCMonth() === month.getUTCMonth();
            const isToday = sameDay(date, todayDate);
            const unavailable = Boolean((min && toInputValue(date) < min) || (max && toInputValue(date) > max));
            return (
              <button key={`${date.toISOString()}-${index}`} type="button" disabled={unavailable} onClick={() => choose(date)} aria-label={formatLongDate(date)} className={cn(
                "mx-auto flex h-9 w-9 items-center justify-center rounded-lg text-sm transition-colors hover:bg-primary/10 hover:text-primary focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring",
                !isCurrentMonth && "text-muted-foreground/45",
                isToday && !selectedDay && "font-semibold text-primary ring-1 ring-primary/30",
                selectedDay && "bg-primary font-semibold text-primary-foreground shadow-sm hover:bg-primary hover:text-primary-foreground",
                unavailable && "cursor-not-allowed opacity-30 hover:bg-transparent hover:text-current"
              )}>{date.getUTCDate()}</button>
            );
          })}
        </div>
        <div className="mt-3 flex items-center justify-between border-t pt-3">
          <Button type="button" variant="ghost" size="sm" onClick={() => setMonth(startOfMonth(todayDate))}>Hoy</Button>
          <Button type="button" variant="ghost" size="sm" disabled={Boolean((min && toInputValue(todayDate) < min) || (max && toInputValue(todayDate) > max))} onClick={() => choose(todayDate)}>Elegir hoy</Button>
        </div>
      </PopoverContent>
    </Popover>
  );
}

// UTC is a neutral carrier for calendar arithmetic, never a business instant.
function calendarDate(year: number, month: number, day: number) {
  const date = new Date(0);
  date.setUTCFullYear(year, month, day);
  date.setUTCHours(0, 0, 0, 0);
  return date;
}
function parseDate(value: string) {
  const [year, month, day] = value.split("-").map(Number);
  return calendarDate(year, month - 1, day);
}
function toInputValue(date: Date) {
  return `${String(date.getUTCFullYear()).padStart(4, "0")}-${String(date.getUTCMonth() + 1).padStart(2, "0")}-${String(date.getUTCDate()).padStart(2, "0")}`;
}
function formatLongDate(date: Date) {
  return `${date.getUTCDate()} de ${MONTHS[date.getUTCMonth()]} de ${date.getUTCFullYear()}`;
}
function startOfMonth(date: Date) { return calendarDate(date.getUTCFullYear(), date.getUTCMonth(), 1); }
function previousMonth(date: Date) { return calendarDate(date.getUTCFullYear(), date.getUTCMonth() - 1, 1); }
function nextMonth(date: Date) { return calendarDate(date.getUTCFullYear(), date.getUTCMonth() + 1, 1); }
function sameDay(a: Date, b: Date) { return a.getUTCFullYear() === b.getUTCFullYear() && a.getUTCMonth() === b.getUTCMonth() && a.getUTCDate() === b.getUTCDate(); }
function calendarDays(month: Date) {
  const first = calendarDate(month.getUTCFullYear(), month.getUTCMonth(), 1);
  const start = new Date(first);
  start.setUTCDate(first.getUTCDate() - ((first.getUTCDay() + 6) % 7));
  return Array.from({ length: 42 }, (_, index) => {
    const day = new Date(start);
    day.setUTCDate(start.getUTCDate() + index);
    return day;
  });
}
