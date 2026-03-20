"use client";

import { useState } from "react";
import { Check, ChevronsUpDown, Building2 } from "lucide-react";

import { Button } from "@/components/ui/button";
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from "@/components/ui/popover";
import {
  Command,
  CommandEmpty,
  CommandGroup,
  CommandInput,
  CommandItem,
  CommandList,
} from "@/components/ui/command";
import { cn } from "@/lib/utils";
import { useBusinessContextStore } from "@/stores/business-context-store";

/** Value for cmdk: searchable by name and id, unique per business */
function commandValue(business: { businessId: string; name: string }) {
  return `${business.name} ${business.businessId}`.trim();
}

export function BusinessSwitcher() {
  const [open, setOpen] = useState(false);
  const { businesses, selectedBusinessId, selectBusiness } =
    useBusinessContextStore();

  const selectedBusiness = businesses.find(
    (b) => b.businessId === selectedBusinessId
  );

  if (businesses.length === 0) return null;

  const displayName = selectedBusiness?.name ?? "Seleccionar negocio";

  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger asChild>
        <Button
          variant="outline"
          role="combobox"
          aria-expanded={open}
          aria-label="Cambiar negocio"
          title={displayName}
          className="h-9 min-w-[12rem] max-w-[min(100vw-8rem,20rem)] justify-between px-3"
          size="sm"
        >
          <div className="flex min-w-0 flex-1 items-center gap-2">
            <Building2 className="h-4 w-4 shrink-0 text-muted-foreground" />
            <span className="truncate text-left font-medium">{displayName}</span>
          </div>
          <ChevronsUpDown className="ml-2 h-4 w-4 shrink-0 opacity-50" />
        </Button>
      </PopoverTrigger>
      <PopoverContent
        className="w-[min(100vw-2rem,22rem)] p-0"
        align="start"
        sideOffset={6}
      >
        <Command shouldFilter>
          <CommandInput placeholder="Filtrar por nombre…" />
          <CommandList>
            <CommandEmpty>No hay negocios que coincidan.</CommandEmpty>
            <CommandGroup heading="Negocios del tenant">
              {businesses.map((business) => (
                <CommandItem
                  key={business.businessId}
                  value={commandValue(business)}
                  onSelect={() => {
                    selectBusiness(business.businessId);
                    setOpen(false);
                  }}
                >
                  <Check
                    className={cn(
                      "mr-2 h-4 w-4 shrink-0",
                      selectedBusinessId === business.businessId
                        ? "opacity-100"
                        : "opacity-0"
                    )}
                  />
                  <span className="truncate">{business.name}</span>
                </CommandItem>
              ))}
            </CommandGroup>
          </CommandList>
        </Command>
      </PopoverContent>
    </Popover>
  );
}
