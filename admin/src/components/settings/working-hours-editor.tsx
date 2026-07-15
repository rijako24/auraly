"use client";

import { Plus, Trash2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { TimePicker } from "@/components/ui/time-picker";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Switch } from "@/components/ui/switch";
import type { WorkingHour } from "@/types/entities";

const DAYS = [
  { value: 1, label: "Lunes" },
  { value: 2, label: "Martes" },
  { value: 3, label: "Miercoles" },
  { value: 4, label: "Jueves" },
  { value: 5, label: "Viernes" },
  { value: 6, label: "Sabado" },
  { value: 0, label: "Domingo" },
];

interface WorkingHoursEditorProps {
  value: WorkingHour[];
  onChange: (value: WorkingHour[]) => void;
}

export function WorkingHoursEditor({ value, onChange }: WorkingHoursEditorProps) {
  const sorted = [...value].sort((a, b) =>
    a.dayOfWeek === b.dayOfWeek
      ? a.openTime.localeCompare(b.openTime)
      : dayOrder(a.dayOfWeek) - dayOrder(b.dayOfWeek)
  );

  const updateAt = (index: number, patch: Partial<WorkingHour>) => {
    const original = sorted[index];
    const originalIndex = value.indexOf(original);
    const next = [...value];
    next[originalIndex] = { ...original, ...patch };
    onChange(next);
  };

  const removeAt = (index: number) => {
    const original = sorted[index];
    onChange(value.filter((item) => item !== original));
  };

  return (
    <div className="space-y-3">
      <div className="grid gap-2">
        {sorted.map((item, index) => (
          <div
            key={`${item.workingHourId ?? "new"}-${index}`}
            className="grid grid-cols-[minmax(180px,1.2fr)_136px_136px_70px_40px] items-center gap-2"
          >
            <Select
              value={String(item.dayOfWeek)}
              onValueChange={(day) => updateAt(index, { dayOfWeek: Number(day) })}
            >
              <SelectTrigger>
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {DAYS.map((day) => (
                  <SelectItem key={day.value} value={String(day.value)}>
                    {day.label}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
            <TimePicker value={item.openTime} onChange={(openTime) => updateAt(index, { openTime })} />
            <TimePicker value={item.closeTime} onChange={(closeTime) => updateAt(index, { closeTime })} />
            <div className="flex justify-center">
              <Switch
                checked={item.isActive}
                onCheckedChange={(isActive) => updateAt(index, { isActive })}
              />
            </div>
            <Button variant="ghost" size="icon" onClick={() => removeAt(index)}>
              <Trash2 className="h-4 w-4" />
            </Button>
          </div>
        ))}
      </div>

      <Button
        type="button"
        variant="outline"
        size="sm"
        onClick={() =>
          onChange([
            ...value,
            {
              dayOfWeek: 1,
              openTime: "08:00",
              closeTime: "12:00",
              isActive: true,
            },
          ])
        }
      >
        <Plus className="mr-2 h-4 w-4" />
        Agregar bloque
      </Button>
    </div>
  );
}

function dayOrder(day: number) {
  return day === 0 ? 7 : day;
}
