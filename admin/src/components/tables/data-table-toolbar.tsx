"use client";

import * as React from "react";
import {
  LayoutGrid,
  LayoutList,
  RotateCcw,
  Search,
  Table2,
} from "lucide-react";

import { cn } from "@/lib/utils";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
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

export interface FacetedFilterConfig {
  column: string;
  title: string;
  options: { label: string; value: string; count?: number }[];
}

export interface ColumnVisibilityOption {
  id: string;
  label: string;
}

export interface DataTableToolbarProps<TData> {
  searchKey?: string;
  searchPlaceholder?: string;
  searchValue?: string;
  onSearch?: (value: string) => void;
  facetedFilters?: FacetedFilterConfig[];
  facetedFilterValues?: Record<string, Set<string>>;
  onFacetedFilterChange?: (column: string, values: Set<string>) => void;
  viewMode?: ViewMode;
  onViewModeChange?: (mode: ViewMode) => void;
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

export function DataTableToolbar<TData>({
  searchKey,
  searchPlaceholder = "Buscar...",
  searchValue = "",
  onSearch,
  facetedFilters = [],
  facetedFilterValues = {},
  onFacetedFilterChange,
  viewMode = "table",
  onViewModeChange,
  columnVisibilityOptions,
  columnVisibility = {},
  onColumnVisibilityChange,
  onReset,
  table,
  children,
  className,
}: DataTableToolbarProps<TData>) {
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
          <div className="relative min-w-0 flex-[1_1_100%] sm:max-w-sm sm:flex-1">
            <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
            <Input
              placeholder={searchPlaceholder}
              value={searchValue}
              onChange={(e) => onSearch(e.target.value)}
              className="h-10 pl-9 sm:h-8"
            />
          </div>
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
          <div className="flex items-center rounded-md border border-input p-1">
            <Button
              variant={viewMode === "table" ? "secondary" : "ghost"}
              size="sm"
              className="h-8 px-2 sm:h-7"
              onClick={() => onViewModeChange("table")}
            >
              <Table2 className="h-4 w-4" />
              <span className="sr-only">Tabla</span>
            </Button>
            <Button
              variant={viewMode === "card" ? "secondary" : "ghost"}
              size="sm"
              className="h-8 px-2 sm:h-7"
              onClick={() => onViewModeChange("card")}
            >
              <LayoutGrid className="h-4 w-4" />
              <span className="sr-only">Tarjetas</span>
            </Button>
            <Button
              variant={viewMode === "list" ? "secondary" : "ghost"}
              size="sm"
              className="h-8 px-2 sm:h-7"
              onClick={() => onViewModeChange("list")}
            >
              <LayoutList className="h-4 w-4" />
              <span className="sr-only">Lista</span>
            </Button>
          </div>
        )}
        {(visibleColumns.length > 0 || table) &&
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
