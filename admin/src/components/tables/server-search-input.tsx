"use client";

import { Loader2, Search } from "lucide-react";
import * as React from "react";

import { Input } from "@/components/ui/input";
import { cn } from "@/lib/utils";
import { shouldShowServerSearchSpinner } from "./server-search-state";

export function ServerSearchInput({
  value,
  onSearch,
  isSearching = false,
  placeholder = "Buscar...",
  className,
  inputClassName,
  delay = 300,
}: {
  value: string;
  onSearch: (value: string) => void;
  isSearching?: boolean;
  placeholder?: string;
  className?: string;
  inputClassName?: string;
  delay?: number;
}) {
  const [draft, setDraft] = React.useState(value);
  const onSearchRef = React.useRef(onSearch);
  const lastExternalValueRef = React.useRef(value);
  const normalizedDraft = draft.trim();
  const normalizedValue = value.trim();
  const pending = normalizedDraft !== normalizedValue;
  const showSpinner = shouldShowServerSearchSpinner({
    draft,
    value,
    isSearching,
  });

  React.useEffect(() => {
    onSearchRef.current = onSearch;
  }, [onSearch]);

  React.useEffect(() => {
    if (lastExternalValueRef.current === value) return;
    lastExternalValueRef.current = value;
    setDraft(value);
  }, [value]);

  React.useEffect(() => {
    if (!pending) return;
    const timeout = window.setTimeout(
      () => onSearchRef.current(normalizedDraft),
      delay,
    );
    return () => window.clearTimeout(timeout);
  }, [delay, normalizedDraft, pending]);

  return (
    <label className={cn("relative block min-w-0", className)}>
      <span className="sr-only">{placeholder}</span>
      <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
      <Input
        autoComplete="off"
        value={draft}
        onChange={(event) => setDraft(event.target.value)}
        placeholder={placeholder}
        className={cn("pl-9 pr-9", inputClassName)}
      />
      {showSpinner && (
        <Loader2
          aria-label="Buscando"
          className="pointer-events-none absolute right-3 top-1/2 h-4 w-4 -translate-y-1/2 animate-spin text-teal-600"
        />
      )}
    </label>
  );
}
