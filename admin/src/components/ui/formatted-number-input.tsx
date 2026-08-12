"use client";

import { useEffect, useState, type KeyboardEventHandler } from "react";
import { Input } from "@/components/ui/input";
import { cn } from "@/lib/utils";

const formatter = new Intl.NumberFormat("es-CO", {
  minimumFractionDigits: 0,
  maximumFractionDigits: 4,
});

export function FormattedNumberInput({
  value,
  onValueChange,
  kind = "number",
  commitMode = "change",
  className,
  disabled,
  id,
  onKeyDown,
}: {
  value: string | number;
  onValueChange: (value: number | null) => void;
  kind?: "currency" | "percent" | "number";
  commitMode?: "change" | "blur";
  className?: string;
  disabled?: boolean;
  id?: string;
  onKeyDown?: KeyboardEventHandler<HTMLInputElement>;
}) {
  const parsed = parseStoredValue(value);
  const formatted = parsed === null ? "" : formatter.format(parsed);
  const [editing, setEditing] = useState(false);
  const [dirty, setDirty] = useState(false);
  const [draft, setDraft] = useState(formatted);

  useEffect(() => {
    if (!editing || !dirty) setDraft(formatted);
  }, [dirty, editing, formatted]);

  const commitDraft = () => {
    const next = parseColombianInput(draft);
    setEditing(false);
    setDirty(false);
    if (!sameNumber(next, parsed)) onValueChange(next);
  };

  return <div className="relative">
    {kind === "currency" && <span className="pointer-events-none absolute left-3 top-1/2 z-10 -translate-y-1/2 font-medium text-muted-foreground">$</span>}
    <Input
      id={id}
      className={cn(kind === "currency" && "pl-7", kind === "percent" && "pr-8", className)}
      inputMode="decimal"
      value={editing ? draft : formatted}
      disabled={disabled}
      onFocus={(event) => {
        setEditing(true);
        setDirty(false);
        setDraft(formatted);
        event.currentTarget.select();
      }}
      onBlur={() => {
        if (commitMode === "blur") commitDraft();
        else {
          setEditing(false);
          setDirty(false);
        }
      }}
      onChange={(event) => {
        const next = formatColombianDraft(event.target.value);
        setDirty(true);
        setDraft(next);
        if (commitMode === "change") onValueChange(parseColombianInput(next));
      }}
      onKeyDown={onKeyDown}
    />
    {kind === "percent" && <span className="pointer-events-none absolute right-3 top-1/2 -translate-y-1/2 text-muted-foreground">%</span>}
  </div>;
}

function sameNumber(left: number | null, right: number | null): boolean {
  return left === right || (left !== null && right !== null && Math.abs(left - right) < 0.000001);
}

function parseStoredValue(value: string | number): number | null {
  if (typeof value === "number") return Number.isFinite(value) ? value : null;
  if (!value.trim()) return null;
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : null;
}

export function parseColombianInput(value: string): number | null {
  const clean = value.replace(/[$%\s]/g, "").trim();
  if (!clean) return null;
  const normalized = clean.includes(",")
    ? clean.replace(/\./g, "").replace(",", ".")
    : clean.replace(/\./g, "");
  const parsed = Number(normalized);
  return Number.isFinite(parsed) ? parsed : null;
}

function formatColombianDraft(value: string): string {
  const clean = value.replace(/[$%\s]/g, "").replace(/[^\d.,]/g, "");
  if (!clean) return "";
  const commaIndex = clean.indexOf(",");
  const integerSource = (commaIndex >= 0 ? clean.slice(0, commaIndex) : clean).replace(/[.,]/g, "");
  const fraction = commaIndex >= 0 ? clean.slice(commaIndex + 1).replace(/\D/g, "").slice(0, 4) : "";
  const integer = integerSource ? Number(integerSource).toLocaleString("es-CO") : "0";
  return commaIndex >= 0 ? `${integer},${fraction}` : integer;
}
