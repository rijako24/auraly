"use client";

import type { ReactNode } from "react";
import { ChevronDown } from "lucide-react";

import {
  Collapsible,
  CollapsibleContent,
  CollapsibleTrigger,
} from "@/components/ui/collapsible";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { cn } from "@/lib/utils";

interface ConfigurationDisclosureProps {
  title: ReactNode;
  description?: ReactNode;
  meta?: ReactNode;
  children: ReactNode;
  defaultOpen?: boolean;
  className?: string;
  contentClassName?: string;
}

export function ConfigurationDisclosure({
  title,
  description,
  meta,
  children,
  defaultOpen = false,
  className,
  contentClassName,
}: ConfigurationDisclosureProps) {
  return (
    <Collapsible
      defaultOpen={defaultOpen}
      className={cn(
        "group overflow-hidden rounded-xl border bg-background transition-colors data-[state=open]:border-primary/25",
        className
      )}
    >
      <CollapsibleTrigger asChild>
        <button
          type="button"
          className="flex w-full items-center gap-3 px-4 py-3 text-left outline-none transition-colors hover:bg-muted/40 focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-inset"
        >
          <div className="min-w-0 flex-1">
            <div className="font-medium text-foreground">{title}</div>
            {description ? (
              <div className="mt-0.5 text-sm font-normal text-muted-foreground">
                {description}
              </div>
            ) : null}
          </div>
          {meta ? <div className="shrink-0">{meta}</div> : null}
          <ChevronDown className="h-4 w-4 shrink-0 text-muted-foreground transition-transform duration-200 group-data-[state=open]:rotate-180" />
        </button>
      </CollapsibleTrigger>
      <CollapsibleContent>
        <div className={cn("border-t p-4", contentClassName)}>{children}</div>
      </CollapsibleContent>
    </Collapsible>
  );
}

interface ConfigurationSelectOption {
  value: string;
  label: string;
}

interface ConfigurationSelectProps {
  value?: string | null;
  onChange: (value: string) => void;
  options: ConfigurationSelectOption[];
  placeholder?: string;
  className?: string;
  allowEmpty?: boolean;
  emptyLabel?: string;
}

const EMPTY_VALUE = "__auraly_empty_value__";

export function ConfigurationSelect({
  value,
  onChange,
  options,
  placeholder = "Seleccionar",
  className,
  allowEmpty = false,
  emptyLabel = "Sin seleccionar",
}: ConfigurationSelectProps) {
  const selectedValue = value?.trim() || (allowEmpty ? EMPTY_VALUE : undefined);

  return (
    <Select
      value={selectedValue}
      onValueChange={(next) => onChange(next === EMPTY_VALUE ? "" : next)}
    >
      <SelectTrigger className={cn("h-10", className)}>
        <SelectValue placeholder={placeholder} />
      </SelectTrigger>
      <SelectContent>
        {allowEmpty ? <SelectItem value={EMPTY_VALUE}>{emptyLabel}</SelectItem> : null}
        {options.map((option) => (
          <SelectItem key={option.value} value={option.value}>
            {option.label}
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  );
}
