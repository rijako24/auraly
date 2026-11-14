"use client";

import { DatePicker } from "@/components/ui/date-picker";
import { TimePicker } from "@/components/ui/time-picker";
import { cn } from "@/lib/utils";

interface DateTimePickerProps {
  value?: string;
  onChange: (value: string) => void;
  disabled?: boolean;
  className?: string;
  today?: string;
}

export function DateTimePicker({ value, onChange, disabled, className, today }: DateTimePickerProps) {
  const [date = "", time = "00:00"] = (value ?? "").split("T");
  const updateDate = (nextDate: string) => onChange(nextDate ? `${nextDate}T${time.slice(0, 5) || "00:00"}` : "");
  const updateTime = (nextTime: string) => {
    const now = new Date();
    const nextDate = date || today || `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, "0")}-${String(now.getDate()).padStart(2, "0")}`;
    onChange(`${nextDate}T${nextTime}`);
  };

  return (
    <div className={cn("flex min-w-0 flex-wrap gap-2", className)}>
      <div className="min-w-[12rem] flex-1">
        <DatePicker today={today} value={date} onChange={updateDate} disabled={disabled} />
      </div>
      <div className="min-w-[8rem] flex-[0_1_10rem]">
        <TimePicker value={time.slice(0, 5)} onChange={updateTime} disabled={disabled} />
      </div>
    </div>
  );
}
