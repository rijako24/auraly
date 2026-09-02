"use client";

import { useEffect, useMemo, useState } from "react";
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
}

export function DatePicker({ value, onChange, placeholder = "Selecciona una fecha", disabled, className, min, max, id }: DatePickerProps) {
  const selected = value ? parseDate(value) : undefined;
  const [open, setOpen] = useState(false);
  const [month, setMonth] = useState(() => startOfMonth(selected ?? new Date()));
  const days = useMemo(() => calendarDays(month), [month]);

  useEffect(() => {
    if (value) setMonth(startOfMonth(parseDate(value)));
  }, [value]);

  const choose = (date: Date) => {
    if (disabled) return;
    onChange?.(toInputValue(date));
    setOpen(false);
  };

  return (
    <Popover modal open={open} onOpenChange={setOpen}>
      <PopoverTrigger asChild>
        <Button id={id} type="button" variant="outline" disabled={disabled} className={cn("w-full justify-start gap-2 px-3 font-normal", !value && "text-muted-foreground", className)}>
          <CalendarDays className="h-4 w-4 text-primary" />
          {selected ? formatLongDate(selected) : placeholder}
        </Button>
      </PopoverTrigger>
      <PopoverContent align="start" className="w-[19rem] rounded-xl border-border/80 p-3 shadow-xl">
        <div className="mb-3 flex items-center justify-between">
          <Button type="button" variant="ghost" size="icon" className="h-8 w-8" onClick={() => setMonth(previousMonth(month))} aria-label="Mes anterior">
            <ChevronLeft className="h-4 w-4" />
          </Button>
          <p className="text-sm font-semibold capitalize">{MONTHS[month.getMonth()]} de {month.getFullYear()}</p>
          <Button type="button" variant="ghost" size="icon" className="h-8 w-8" onClick={() => setMonth(nextMonth(month))} aria-label="Mes siguiente">
            <ChevronRight className="h-4 w-4" />
          </Button>
        </div>
        <div className="grid grid-cols-7 gap-1 text-center text-xs">
          {WEEKDAYS.map((day) => <span key={day} className="py-1 font-medium text-muted-foreground">{day}</span>)}
          {days.map((date, index) => {
            const selectedDay = selected && sameDay(date, selected);
            const isCurrentMonth = date.getMonth() === month.getMonth();
            const isToday = sameDay(date, new Date());
            const unavailable = Boolean((min && toInputValue(date) < min) || (max && toInputValue(date) > max));
            return (
              <button key={`${date.toISOString()}-${index}`} type="button" disabled={unavailable} onClick={() => choose(date)} aria-label={formatLongDate(date)} className={cn(
                "mx-auto flex h-9 w-9 items-center justify-center rounded-lg text-sm transition-colors hover:bg-primary/10 hover:text-primary focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring",
                !isCurrentMonth && "text-muted-foreground/45",
                isToday && !selectedDay && "font-semibold text-primary ring-1 ring-primary/30",
                selectedDay && "bg-primary font-semibold text-primary-foreground shadow-sm hover:bg-primary hover:text-primary-foreground",
                unavailable && "cursor-not-allowed opacity-30 hover:bg-transparent hover:text-current"
              )}>{date.getDate()}</button>
            );
          })}
        </div>
        <div className="mt-3 flex items-center justify-between border-t pt-3">
          <Button type="button" variant="ghost" size="sm" onClick={() => setMonth(startOfMonth(new Date()))}>Hoy</Button>
          <Button type="button" variant="ghost" size="sm" onClick={() => choose(new Date())}>Elegir hoy</Button>
        </div>
      </PopoverContent>
    </Popover>
  );
}

function parseDate(value: string) {
  const [year, month, day] = value.split("-").map(Number);
  return new Date(year, month - 1, day);
}
function toInputValue(date: Date) {
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, "0")}-${String(date.getDate()).padStart(2, "0")}`;
}
function formatLongDate(date: Date) {
  return `${date.getDate()} de ${MONTHS[date.getMonth()]} de ${date.getFullYear()}`;
}
function startOfMonth(date: Date) { return new Date(date.getFullYear(), date.getMonth(), 1); }
function previousMonth(date: Date) { return new Date(date.getFullYear(), date.getMonth() - 1, 1); }
function nextMonth(date: Date) { return new Date(date.getFullYear(), date.getMonth() + 1, 1); }
function sameDay(a: Date, b: Date) { return a.getFullYear() === b.getFullYear() && a.getMonth() === b.getMonth() && a.getDate() === b.getDate(); }
function calendarDays(month: Date) {
  const first = new Date(month.getFullYear(), month.getMonth(), 1);
  const start = new Date(first);
  start.setDate(first.getDate() - ((first.getDay() + 6) % 7));
  return Array.from({ length: 42 }, (_, index) => {
    const day = new Date(start);
    day.setDate(start.getDate() + index);
    return day;
  });
}
