"use client";

import { useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
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

export function BusinessSwitcher() {
  const [open, setOpen] = useState(false);
  const queryClient = useQueryClient();
  const { businesses, selectedBusinessId, selectBusiness } =
    useBusinessContextStore();

  const selectedBusiness = businesses.find(
    (b) => b.businessId === selectedBusinessId
  );

  if (businesses.length === 0) return null;

  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger asChild>
        <Button
          variant="outline"
          role="combobox"
          aria-expanded={open}
          className="min-w-0 flex-1 justify-between px-2 sm:w-[220px] sm:flex-none sm:px-3"
          size="sm"
        >
          <div className="flex min-w-0 items-center gap-2 truncate">
            <Building2 className="h-4 w-4 shrink-0 text-muted-foreground" />
            <span className="truncate">
              {selectedBusiness?.name ?? "Seleccionar negocio"}
            </span>
          </div>
          <ChevronsUpDown className="ml-2 h-4 w-4 shrink-0 opacity-50" />
        </Button>
      </PopoverTrigger>
      <PopoverContent className="w-[min(calc(100vw-1.5rem),22rem)] p-0 sm:w-[220px]" align="start">
        <Command>
          <CommandInput placeholder="Buscar negocio..." />
          <CommandList>
            <CommandEmpty>No se encontraron negocios.</CommandEmpty>
            <CommandGroup>
              {businesses.map((business) => (
                <CommandItem
                  key={business.businessId}
                  value={`${business.name} ${business.businessId}`}
                  onSelect={() => {
                    if (business.businessId !== selectedBusinessId) {
                      selectBusiness(business.businessId);
                      queryClient.removeQueries({
                        predicate: (query) => query.queryKey[0] !== "businesses",
                      });
                    }
                    setOpen(false);
                  }}
                >
                  <Check
                    className={cn(
                      "mr-2 h-4 w-4",
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
