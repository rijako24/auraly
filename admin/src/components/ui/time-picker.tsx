"use client";

import { useState } from "react";
import { Clock3 } from "lucide-react";

import { Button } from "@/components/ui/button";
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover";
import { cn } from "@/lib/utils";

interface TimePickerProps {
  value?: string | null;
  onChange: (value: string) => void;
  placeholder?: string;
  disabled?: boolean;
  className?: string;
}

const hours = Array.from({ length: 24 }, (_, value) => String(value).padStart(2, "0"));
const minutes = Array.from({ length: 60 }, (_, value) => String(value).padStart(2, "0"));

export function TimePicker({ value, onChange, placeholder = "Seleccionar hora", disabled, className }: TimePickerProps) {
  const [open, setOpen] = useState(false);
  const [hour = "", minute = ""] = (value ?? "").split(":");
  const choose = (nextHour: string, nextMinute: string) => {
    if (!nextHour || !nextMinute) return;
    onChange(`${nextHour}:${nextMinute}`);
    setOpen(false);
  };

  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger asChild>
        <Button type="button" variant="outline" disabled={disabled} className={cn("w-full justify-start gap-2 px-3 font-normal", !value && "text-muted-foreground", className)}>
          <Clock3 className="h-4 w-4 text-primary" />
          {value ? formatTime(value) : placeholder}
        </Button>
      </PopoverTrigger>
      <PopoverContent align="start" className="w-[17rem] rounded-xl border-border/80 p-3 shadow-xl">
        <p className="mb-3 text-sm font-semibold">Elige una hora</p>
        <div className="grid grid-cols-[1fr_auto_1fr] gap-3">
          <TimeColumn label="Hora" values={hours} selected={hour} onChoose={(nextHour) => choose(nextHour, minute)} />
          <span className="pt-10 text-lg font-semibold text-muted-foreground">:</span>
          <TimeColumn label="Minutos" values={minutes} selected={minute} onChoose={(nextMinute) => choose(hour, nextMinute)} />
        </div>
      </PopoverContent>
    </Popover>
  );
}

function TimeColumn({ label, values, selected, onChoose }: { label: string; values: string[]; selected: string; onChoose: (value: string) => void }) {
  return <div><p className="mb-1 text-xs font-medium text-muted-foreground">{label}</p><div className="h-48 space-y-1 overflow-y-auto pr-1">{values.map((value) => <button key={value} type="button" onClick={() => onChoose(value)} className={cn("flex h-8 w-full items-center justify-center rounded-md text-sm transition-colors hover:bg-primary/10 hover:text-primary", selected === value && "bg-primary text-primary-foreground hover:bg-primary hover:text-primary-foreground")}>{value}</button>)}</div></div>;
}

function formatTime(value: string) {
  const [hoursValue, minutesValue] = value.split(":").map(Number);
  const suffix = hoursValue >= 12 ? "p. m." : "a. m.";
  const displayHour = hoursValue % 12 || 12;
  return `${String(displayHour).padStart(2, "0")}:${String(minutesValue).padStart(2, "0")} ${suffix}`;
}