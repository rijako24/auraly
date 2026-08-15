"use client";

import { DatePicker } from "@/components/ui/date-picker";
import { TimePicker } from "@/components/ui/time-picker";
import { cn } from "@/lib/utils";

interface DateTimePickerProps {
  value?: string;
  onChange: (value: string) => void;
  disabled?: boolean;
  className?: string;
}

export function DateTimePicker({ value, onChange, disabled, className }: DateTimePickerProps) {
  const [date = "", time = "00:00"] = (value ?? "").split("T");
  const updateDate = (nextDate: string) => onChange(`${nextDate}T${time.slice(0, 5) || "00:00"}`);
  const updateTime = (nextTime: string) => {
    const nextDate = date || new Date().toISOString().slice(0, 10);
    onChange(`${nextDate}T${nextTime}`);
  };

  return (
    <div className={cn("grid gap-2 sm:grid-cols-[minmax(0,1fr)_10rem]", className)}>
      <DatePicker value={date} onChange={updateDate} disabled={disabled} />
      <TimePicker value={time.slice(0, 5)} onChange={updateTime} disabled={disabled} />
    </div>
  );
}
