"use client";

import { useEffect, useMemo, useState, type UIEvent } from "react";
import { useInfiniteQuery } from "@tanstack/react-query";
import { Check, ChevronsUpDown, Loader2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Command, CommandEmpty, CommandInput, CommandItem, CommandList } from "@/components/ui/command";
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover";
import { cn } from "@/lib/utils";

export interface PagedEntityOption {
  value: string;
  label: string;
  description?: string | null;
}

export interface PagedEntityPage<T> {
  items: T[];
  page: number;
  totalPages: number;
  totalCount: number;
}

export function PagedEntitySelect<T>({
  queryKey,
  value,
  onChange,
  loadPage,
  getOption,
  selectedOption,
  leadingOptions = [],
  placeholder = "Selecciona",
  ariaLabel,
  searchPlaceholder = "Buscar por nombre o identificación…",
  emptyMessage = "No hay resultados.",
  disabled = false,
  preload = false,
  pageSize = 30,
  className,
}: {
  queryKey: readonly unknown[];
  value: string;
  onChange: (value: string, option: PagedEntityOption, item?: T) => void;
  loadPage: (search: string, page: number, pageSize: number) => Promise<PagedEntityPage<T>>;
  getOption: (item: T) => PagedEntityOption | null;
  selectedOption?: PagedEntityOption | null;
  leadingOptions?: PagedEntityOption[];
  placeholder?: string;
  ariaLabel?: string;
  searchPlaceholder?: string;
  emptyMessage?: string;
  disabled?: boolean;
  preload?: boolean;
  pageSize?: number;
  className?: string;
}) {
  const [open, setOpen] = useState(false);
  const [search, setSearch] = useState("");
  const [debouncedSearch, setDebouncedSearch] = useState("");
  useEffect(() => {
    const timeout = window.setTimeout(() => setDebouncedSearch(search.trim()), 250);
    return () => window.clearTimeout(timeout);
  }, [search]);

  const query = useInfiniteQuery({
    queryKey: [...queryKey, debouncedSearch, pageSize],
    queryFn: ({ pageParam }) => loadPage(debouncedSearch, pageParam, pageSize),
    initialPageParam: 1,
    getNextPageParam: page => page.page < page.totalPages ? page.page + 1 : undefined,
    enabled: (open || preload) && !disabled,
    staleTime: 5 * 60 * 1000,
  });
  const entries = useMemo(() => {
    const values = new Map<string, { option: PagedEntityOption; item?: T }>();
    for (const item of query.data?.pages.flatMap(page => page.items) ?? []) {
      const option = getOption(item);
      if (option) values.set(option.value, { option, item });
    }
    for (const option of leadingOptions) values.set(option.value, { option });
    if (selectedOption && !values.has(selectedOption.value)) values.set(selectedOption.value, { option: selectedOption });
    return [...values.values()];
  }, [getOption, leadingOptions, query.data, selectedOption]);
  const selected = entries.find(entry => entry.option.value === value)?.option ?? selectedOption;
  const total = query.data?.pages[0]?.totalCount ?? 0;

  function onScroll(event: UIEvent<HTMLDivElement>) {
    const element = event.currentTarget;
    if (element.scrollHeight - element.scrollTop - element.clientHeight < 80 && query.hasNextPage && !query.isFetchingNextPage)
      void query.fetchNextPage();
  }

  return <Popover open={open} onOpenChange={setOpen}>
    <PopoverTrigger asChild>
      <Button type="button" variant="outline" role="combobox" aria-label={ariaLabel} aria-expanded={open} disabled={disabled} className={cn("w-full justify-between font-normal", className)}>
        <span className="truncate">{selected?.label ?? placeholder}</span>
        <ChevronsUpDown className="ml-2 h-4 w-4 shrink-0 opacity-50"/>
      </Button>
    </PopoverTrigger>
    <PopoverContent className="w-[var(--radix-popover-trigger-width)] p-0" align="start">
      <Command shouldFilter={false}>
        <CommandInput value={search} onValueChange={setSearch} placeholder={searchPlaceholder}/>
        <CommandList onScroll={onScroll}>
          {query.isLoading && <div className="flex items-center gap-2 p-4 text-sm text-muted-foreground"><Loader2 className="h-4 w-4 animate-spin"/>Buscando…</div>}
          {query.isError && <div className="p-4 text-sm text-destructive">No fue posible consultar. <button className="underline" onClick={() => void query.refetch()}>Reintentar</button></div>}
          {!query.isLoading && !query.isError && entries.length === 0 && <CommandEmpty>{emptyMessage}</CommandEmpty>}
          {!query.isLoading && !query.isError && entries.length > 0 && <div className="px-3 py-2 text-xs text-muted-foreground">{Math.min(entries.length, total).toLocaleString("es-CO")} de {total.toLocaleString("es-CO")}</div>}
          {entries.map(({ option, item }) => <CommandItem key={option.value} value={option.value} onSelect={() => { onChange(option.value, option, item); setOpen(false); }} className="items-start">
            <Check className={cn("mr-2 mt-0.5 h-4 w-4 shrink-0", value === option.value ? "opacity-100" : "opacity-0")}/>
            <span className="min-w-0"><span className="block truncate">{option.label}</span>{option.description && <small className="block truncate text-muted-foreground">{option.description}</small>}</span>
          </CommandItem>)}
          {query.hasNextPage && <Button type="button" variant="ghost" className="w-full" disabled={query.isFetchingNextPage} onClick={() => void query.fetchNextPage()}>{query.isFetchingNextPage && <Loader2 className="mr-2 h-4 w-4 animate-spin"/>}Cargar {pageSize} más</Button>}
        </CommandList>
      </Command>
    </PopoverContent>
  </Popover>;
}
