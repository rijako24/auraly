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

export function BusinessSwitcher() {
  const [open, setOpen] = useState(false);
  const { businesses, selectedBusinessId, selectBusiness } =
    useBusinessContextStore();

  const selectedBusiness = businesses.find(
    (b) => b.businessId === selectedBusinessId
  );

  if (businesses.length === 0) return null;

  if (businesses.length === 1) {
    return (
      <div className="flex items-center gap-2 px-2 text-sm font-medium">
        <Building2 className="h-4 w-4 text-muted-foreground" />
        <span className="truncate max-w-[160px]">
          {businesses[0].name}
        </span>
      </div>
    );
  }

  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger asChild>
        <Button
          variant="outline"
          role="combobox"
          aria-expanded={open}
          className="w-[200px] justify-between"
          size="sm"
        >
          <div className="flex items-center gap-2 truncate">
            <Building2 className="h-4 w-4 shrink-0 text-muted-foreground" />
            <span className="truncate">
              {selectedBusiness?.name ?? "Seleccionar negocio"}
            </span>
          </div>
          <ChevronsUpDown className="ml-2 h-4 w-4 shrink-0 opacity-50" />
        </Button>
      </PopoverTrigger>
      <PopoverContent className="w-[200px] p-0" align="start">
        <Command>
          <CommandInput placeholder="Buscar negocio..." />
          <CommandList>
            <CommandEmpty>No se encontraron negocios.</CommandEmpty>
            <CommandGroup>
              {businesses.map((business) => (
                <CommandItem
                  key={business.businessId}
                  value={business.name}
                  onSelect={() => {
                    selectBusiness(business.businessId);
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
