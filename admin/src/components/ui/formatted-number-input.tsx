"use client";

import { useEffect, useState, type KeyboardEventHandler } from "react";
import { Input } from "@/components/ui/input";
import {
  decimalInputFromNumber,
  formatDecimalInput,
  parseDecimalInput,
} from "@/lib/formatted-decimal-input";
import { cn } from "@/lib/utils";

export function FormattedNumberInput({
  value,
  onValueChange,
  kind = "number",
  commitMode = "change",
  className,
  disabled,
  id,
  onKeyDown,
  invalid = false,
  ariaLabel,
  placeholder,
  allowNegative = false,
}: {
  value: string | number;
  onValueChange: (value: number | null) => void;
  kind?: "currency" | "percent" | "number";
  commitMode?: "change" | "blur";
  className?: string;
  disabled?: boolean;
  id?: string;
  onKeyDown?: KeyboardEventHandler<HTMLInputElement>;
  invalid?: boolean;
  ariaLabel?: string;
  placeholder?: string;
  allowNegative?: boolean;
}) {
  const canonical = decimalInputFromNumber(value);
  const parsed = parseDecimalInput(canonical, allowNegative);
  const formatted = formatDecimalInput(canonical, 4, allowNegative);
  const [editing, setEditing] = useState(false);
  const [dirty, setDirty] = useState(false);
  const [draft, setDraft] = useState(formatted);

  useEffect(() => {
    if (!editing || !dirty) setDraft(formatted);
  }, [dirty, editing, formatted]);

  const commitDraft = () => {
    const next = parseDecimalInput(draft, allowNegative);
    setEditing(false);
    setDirty(false);
    if (!sameNumber(next, parsed)) onValueChange(next);
  };

  return <div className="relative">
    {kind === "currency" && <span className="pointer-events-none absolute left-3 top-1/2 z-10 -translate-y-1/2 font-medium text-muted-foreground">$</span>}
    <Input
      id={id}
      aria-label={ariaLabel}
      aria-invalid={invalid}
      placeholder={placeholder}
      className={cn(
        kind === "currency" && "pl-7",
        kind === "percent" && "pr-8",
        invalid && "border-destructive ring-1 ring-destructive/20",
        className,
      )}
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
        const next = formatDecimalInput(event.target.value, 4, allowNegative);
        setDirty(true);
        setDraft(next);
        if (commitMode === "change") onValueChange(parseDecimalInput(next, allowNegative));
      }}
      onKeyDown={onKeyDown}
    />
    {kind === "percent" && <span className="pointer-events-none absolute right-3 top-1/2 -translate-y-1/2 text-muted-foreground">%</span>}
  </div>;
}

function sameNumber(left: number | null, right: number | null): boolean {
  return left === right || (left !== null && right !== null && Math.abs(left - right) < 0.000001);
}
