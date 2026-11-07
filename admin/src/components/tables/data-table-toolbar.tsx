"use client";

import * as React from "react";
import {
  LayoutGrid,
  LayoutList,
  RotateCcw,
  Table2,
  Images,
} from "lucide-react";

import { cn } from "@/lib/utils";
import { Button } from "@/components/ui/button";
import { ServerSearchInput } from "./server-search-input";
import {
  DropdownMenu,
  DropdownMenuCheckboxItem,
  DropdownMenuContent,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";

import { DataTableFacetedFilter } from "./data-table-faceted-filter";

export type ViewMode = "table" | "card" | "list";

const presentationViews = [
  { value: "table", label: "Tabla", icon: Table2 },
  { value: "card", label: "Tarjetas", icon: LayoutGrid },
  { value: "list", label: "Lista", icon: LayoutList },
] as const;

export interface FacetedFilterConfig {
  column: string;
  title: string;
  options: { label: string; value: string; count?: number }[];
}

export interface ColumnVisibilityOption {
  id: string;
  label: string;
}

export interface DataTableToolbarProps {
  searchKey?: string;
  searchPlaceholder?: string;
  searchValue?: string;
  onSearch?: (value: string) => void;
  isSearching?: boolean;
  facetedFilters?: FacetedFilterConfig[];
  facetedFilterValues?: Record<string, Set<string>>;
  onFacetedFilterChange?: (column: string, values: Set<string>) => void;
  viewMode?: ViewMode;
  onViewModeChange?: (mode: ViewMode) => void;
  listLayout?: "rows" | "gallery";
  showColumnToggle?: boolean;
  columnVisibilityOptions?: ColumnVisibilityOption[];
  columnVisibility?: Record<string, boolean>;
  onColumnVisibilityChange?: (visibility: Record<string, boolean>) => void;
  onReset?: () => void;
  table?: {
    getAllColumns: () => { id: string; getIsVisible: () => boolean }[];
    getAllLeafColumns: () => { id: string; getIsVisible: () => boolean }[];
  };
  children?: React.ReactNode;
  className?: string;
}

export function DataTableToolbar({
  searchKey,
  searchPlaceholder = "Buscar...",
  searchValue = "",
  onSearch,
  isSearching = false,
  facetedFilters = [],
  facetedFilterValues = {},
  onFacetedFilterChange,
  viewMode = "table",
  onViewModeChange,
  listLayout = "rows",
  showColumnToggle = true,
  columnVisibilityOptions,
  columnVisibility = {},
  onColumnVisibilityChange,
  onReset,
  table,
  children,
  className,
}: DataTableToolbarProps) {
  const columns =
    table?.getAllLeafColumns?.() ?? table?.getAllColumns?.() ?? [];
  const hasFilters =
    facetedFilters.some(
      (f) => (facetedFilterValues[f.column]?.size ?? 0) > 0
    ) || searchValue;

  const handleColumnVisibilityToggle = (columnId: string, visible: boolean) => {
    onColumnVisibilityChange?.({
      ...columnVisibility,
      [columnId]: visible,
    });
  };

  const visibleColumns =
    columnVisibilityOptions ??
    columns
      .filter((col) => col.id !== "select")
      .map((col) => ({ id: col.id, label: col.id }));

  return (
    <div className={cn("flex flex-col gap-4", className)}>
      <div className="flex flex-wrap items-center gap-2">
        {searchKey && onSearch && (
          <ServerSearchInput
            value={searchValue}
            onSearch={onSearch}
            isSearching={isSearching}
            placeholder={searchPlaceholder}
            className="flex-[1_1_100%] sm:max-w-sm sm:flex-1"
            inputClassName="h-10 sm:h-8"
          />
        )}
        {facetedFilters.map((filter) => (
          <DataTableFacetedFilter
            key={filter.column}
            column={filter.column}
            title={filter.title}
            options={filter.options}
            selectedValues={facetedFilterValues[filter.column] ?? new Set()}
            onSelectedValuesChange={(values) =>
              onFacetedFilterChange?.(filter.column, values)
            }
          />
        ))}
        {onViewModeChange && (
          <div className="flex items-center rounded-md border border-input p-1" role="group" aria-label="Vista de los resultados">
            {presentationViews.map(view => { const {value} = view; const label = value === "list" && listLayout === "gallery" ? "Galería" : view.label; const Icon = value === "list" && listLayout === "gallery" ? Images : view.icon; return <Button key={value}
              variant={viewMode === value ? "secondary" : "ghost"} size="sm" className="h-8 px-2 sm:h-7"
              title={label} aria-label={label} aria-pressed={viewMode === value} onClick={() => onViewModeChange(value)}>
              <Icon className="h-4 w-4"/><span className="sr-only">{label}</span>
            </Button>; })}
          </div>
        )}
        {showColumnToggle && (visibleColumns.length > 0 || table) &&
          (onColumnVisibilityChange || table) && (
            <DropdownMenu>
              <DropdownMenuTrigger asChild>
                <Button variant="outline" size="sm" className="h-9 sm:h-8">
                  Columnas
                </Button>
              </DropdownMenuTrigger>
              <DropdownMenuContent align="end" className="w-[180px]">
                <DropdownMenuLabel>Alternar columnas</DropdownMenuLabel>
                <DropdownMenuSeparator />
                {visibleColumns.map((col) => {
                  const isVisible =
                    columnVisibility[col.id] ??
                    columns.find((c) => c.id === col.id)?.getIsVisible?.() ??
                    true;
                  return (
                    <DropdownMenuCheckboxItem
                      key={col.id}
                      checked={isVisible}
                      onCheckedChange={(checked) =>
                        handleColumnVisibilityToggle(col.id, !!checked)
                      }
                    >
                      {col.label}
                    </DropdownMenuCheckboxItem>
                  );
                })}
              </DropdownMenuContent>
            </DropdownMenu>
          )}
        {hasFilters && onReset && (
          <Button variant="ghost" size="sm" className="h-9 sm:h-8" onClick={onReset}>
            <RotateCcw className="mr-2 h-4 w-4" />
            Restablecer
          </Button>
        )}
        {children}
      </div>
    </div>
  );
}
