"use client";

import * as React from "react";
import {
  ColumnDef,
  ColumnFiltersState,
  SortingState,
  VisibilityState,
  flexRender,
  getCoreRowModel,
  getFilteredRowModel,
  getPaginationRowModel,
  getSortedRowModel,
  useReactTable,
} from "@tanstack/react-table";
import { Download } from "lucide-react";

import { cn } from "@/lib/utils";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { Skeleton } from "@/components/ui/skeleton";
import { useMediaQuery } from "@/hooks/use-media-query";

import { DataTablePagination } from "./data-table-pagination";
import {
  DataTableToolbar,
  type ViewMode,
  type FacetedFilterConfig,
} from "./data-table-toolbar";

export interface DataTableProps<TData, TValue> {
  columns: ColumnDef<TData, TValue>[];
  data: TData[];
  searchKey?: string;
  searchPlaceholder?: string;
  isLoading?: boolean;
  page?: number;
  pageSize?: number;
  pageCount?: number;
  totalItems?: number;
  onPaginationChange?: (page: number, pageSize: number) => void;
  onSearch?: (value: string) => void;
  bulkActions?: {
    label: string;
    onClick: (rows: TData[]) => void | Promise<void>;
    variant?: "default" | "destructive" | "outline" | "secondary" | "ghost" | "link";
  }[];
  facetedFilters?: FacetedFilterConfig[];
  viewMode?: ViewMode;
  onViewModeChange?: (mode: ViewMode) => void;
  cardRenderer?: (item: TData) => React.ReactNode;
  listRenderer?: (item: TData) => React.ReactNode;
  onExport?: () => void;
  enableRowSelection?: boolean;
  onRowClick?: (item: TData) => void;
  className?: string;
}

export function DataTable<TData, TValue>({
  columns,
  data,
  searchKey,
  searchPlaceholder = "Buscar...",
  isLoading = false,
  page,
  pageSize,
  pageCount: controlledPageCount,
  totalItems,
  onPaginationChange,
  onSearch,
  bulkActions = [],
  facetedFilters = [],
  viewMode = "table",
  onViewModeChange,
  cardRenderer,
  listRenderer,
  onExport,
  enableRowSelection = true,
  onRowClick,
  className,
}: DataTableProps<TData, TValue>) {
  const [sorting, setSorting] = React.useState<SortingState>([]);
  const [columnFilters, setColumnFilters] = React.useState<ColumnFiltersState>([]);
  const [columnVisibility, setColumnVisibility] = React.useState<VisibilityState>({});
  const [rowSelection, setRowSelection] = React.useState<Record<string, boolean>>({});
  const [pagination, setPagination] = React.useState({
    pageIndex: Math.max((page ?? 1) - 1, 0),
    pageSize: pageSize ?? 20,
  });
  const [searchValue, setSearchValue] = React.useState("");
  const [internalViewMode, setViewMode] = React.useState<ViewMode>(viewMode ?? "table");
  const [facetedFilterValues, setFacetedFilterValues] = React.useState<
    Record<string, Set<string>>
  >({});

  const isMobile = useMediaQuery("(max-width: 640px)");
  const effectiveViewMode = viewMode ?? internalViewMode;
  const handleViewModeChange = onViewModeChange ?? setViewMode;
  const displayViewMode =
    isMobile && effectiveViewMode === "table" && cardRenderer ? "card" : effectiveViewMode;

  React.useEffect(() => {
    if (!onPaginationChange) return;

    setPagination((old) => {
      const next = {
        pageIndex: Math.max((page ?? old.pageIndex + 1) - 1, 0),
        pageSize: pageSize ?? old.pageSize,
      };

      return old.pageIndex === next.pageIndex && old.pageSize === next.pageSize
        ? old
        : next;
    });
  }, [onPaginationChange, page, pageSize]);

  const columnsWithSelection = React.useMemo<ColumnDef<TData, TValue>[]>(() => {
    if (!enableRowSelection) return columns;
    const selectColumn: ColumnDef<TData, TValue> = {
      id: "select",
      header: ({ table }) => (
        <Checkbox
          checked={
            table.getIsAllPageRowsSelected() ||
            (table.getIsSomePageRowsSelected() && "indeterminate")
          }
          onCheckedChange={(value) => table.toggleAllPageRowsSelected(!!value)}
          aria-label="Seleccionar todo"
        />
      ),
      cell: ({ row }) => (
        <Checkbox
          checked={row.getIsSelected()}
          onCheckedChange={(value) => row.toggleSelected(!!value)}
          aria-label="Seleccionar fila"
        />
      ),
      enableSorting: false,
      enableHiding: false,
    };
    return [selectColumn, ...columns];
  }, [columns, enableRowSelection]);

  const filteredData = React.useMemo(() => {
    let result = data;
    if (searchValue && searchKey && !onSearch) {
      const lower = searchValue.toLowerCase();
      result = result.filter((row) => {
        const val = (row as Record<string, unknown>)[searchKey];
        return String(val ?? "").toLowerCase().includes(lower);
      });
    }
    facetedFilters.forEach((filter) => {
      const selected = facetedFilterValues[filter.column];
      if (selected?.size) {
        result = result.filter((row) => {
          const val = (row as Record<string, unknown>)[filter.column];
          return selected.has(String(val ?? ""));
        });
      }
    });
    return result;
  }, [data, searchValue, searchKey, onSearch, facetedFilters, facetedFilterValues]);

  const table = useReactTable({
    data: filteredData,
    columns: columnsWithSelection,
    getCoreRowModel: getCoreRowModel(),
    getPaginationRowModel: onPaginationChange ? undefined : getPaginationRowModel(),
    onPaginationChange: (updater) => {
      setPagination((old) => {
        const next = typeof updater === "function" ? updater(old) : updater;
        if (next.pageIndex !== old.pageIndex || next.pageSize !== old.pageSize) {
          onPaginationChange?.(next.pageIndex + 1, next.pageSize);
        }
        return next;
      });
    },
    onSortingChange: setSorting,
    getSortedRowModel: getSortedRowModel(),
    onColumnFiltersChange: setColumnFilters,
    getFilteredRowModel: getFilteredRowModel(),
    onColumnVisibilityChange: setColumnVisibility,
    onRowSelectionChange: setRowSelection,
    state: {
      sorting,
      columnFilters,
      columnVisibility,
      rowSelection,
      pagination,
    },
    manualPagination: !!onPaginationChange,
    pageCount: controlledPageCount ?? -1,
    manualFiltering: !!onSearch,
  });

  const handleSearch = (value: string) => {
    setSearchValue(value);
    onSearch?.(value);
    if (onPaginationChange) {
      setPagination((old) => {
        const next = { ...old, pageIndex: 0 };
        onPaginationChange(1, next.pageSize);
        return next;
      });
    }
    if (!onSearch && searchKey) {
      table.getColumn(searchKey)?.setFilterValue(value);
    }
  };

  const handleFacetedFilterChange = (column: string, values: Set<string>) => {
    setFacetedFilterValues((prev) => ({ ...prev, [column]: values }));
  };

  const handleReset = () => {
    setSearchValue("");
    setFacetedFilterValues({});
    onSearch?.("");
    if (searchKey) {
      table.getColumn(searchKey)?.setFilterValue("");
    }
  };

  const selectedRows = table.getFilteredSelectedRowModel().rows.map((r) => r.original);
  const hasSelection = selectedRows.length > 0;

  const handleExport = () => {
    onExport?.();
    // Stub: could implement CSV/Excel export here
  };

  const currentViewData =
    onPaginationChange && !searchValue && Object.keys(facetedFilterValues).every(
      (k) => !facetedFilterValues[k]?.size
    )
      ? data
      : filteredData;

  const displayItems = currentViewData;

  return (
    <div className={cn("space-y-4", className)}>
      <DataTableToolbar
        searchKey={searchKey}
        searchPlaceholder={searchPlaceholder}
        searchValue={searchValue}
        onSearch={handleSearch}
        facetedFilters={facetedFilters}
        facetedFilterValues={facetedFilterValues}
        onFacetedFilterChange={handleFacetedFilterChange}
        viewMode={displayViewMode}
        onViewModeChange={handleViewModeChange}
        columnVisibility={columnVisibility}
        onColumnVisibilityChange={setColumnVisibility}
        onReset={handleReset}
        table={table}
      >
        {onExport && (
          <Button variant="outline" size="sm" className="h-8" onClick={handleExport}>
            <Download className="mr-2 h-4 w-4" />
            Exportar
          </Button>
        )}
      </DataTableToolbar>

      {hasSelection && bulkActions.length > 0 && (
        <div className="flex flex-wrap items-center gap-2 rounded-md border bg-muted/50 px-3 py-2 sm:px-4">
          <span className="text-sm font-medium">
            {selectedRows.length} fila(s) seleccionada(s)
          </span>
          {bulkActions.map((action) => (
            <Button
              key={action.label}
              variant={action.variant ?? "default"}
              size="sm"
              onClick={async () => {
                await action.onClick(selectedRows);
                setRowSelection({});
              }}
            >
              {action.label}
            </Button>
          ))}
        </div>
      )}

      {isLoading ? (
        <div className="space-y-2">
          {Array.from({ length: 5 }).map((_, i) => (
            <Skeleton key={i} className="h-12 w-full" />
          ))}
        </div>
      ) : displayViewMode === "table" ? (
        <>
          <div className="w-full overflow-x-auto rounded-md border">
            <Table className="min-w-[720px]">
              <TableHeader>
                {table.getHeaderGroups().map((headerGroup) => (
                  <TableRow key={headerGroup.id}>
                    {headerGroup.headers.map((header) => (
                      <TableHead
                        key={header.id}
                        className={cn(
                          "px-4 py-3",
                          header.column.getCanSort()
                            ? "cursor-pointer select-none"
                            : ""
                        )}
                        onClick={header.column.getCanSort() ? header.column.getToggleSortingHandler() : undefined}
                      >
                        {flexRender(
                          header.column.columnDef.header,
                          header.getContext()
                        )}
                      </TableHead>
                    ))}
                  </TableRow>
                ))}
              </TableHeader>
              <TableBody>
                {table.getRowModel().rows?.length ? (
                  table.getRowModel().rows.map((row) => (
                    <TableRow
                      key={row.id}
                      data-state={row.getIsSelected() && "selected"}
                      className={cn(onRowClick && "cursor-pointer")}
                      tabIndex={onRowClick ? 0 : undefined}
                      onClick={() => onRowClick?.(row.original)}
                      onKeyDown={(event) => {
                        if (event.target !== event.currentTarget) return;
                        if (event.key === "ArrowDown" || event.key === "ArrowUp") {
                          event.preventDefault();
                          const sibling = event.key === "ArrowDown"
                            ? event.currentTarget.nextElementSibling
                            : event.currentTarget.previousElementSibling;
                          if (sibling instanceof HTMLElement) sibling.focus();
                          return;
                        }
                        if (onRowClick && (event.key === "Enter" || event.key === " ")) {
                          event.preventDefault();
                          onRowClick(row.original);
                        }
                      }}
                    >
                      {row.getVisibleCells().map((cell) => (
                        <TableCell key={cell.id} className="px-4 py-3">
                          {flexRender(
                            cell.column.columnDef.cell,
                            cell.getContext()
                          )}
                        </TableCell>
                      ))}
                    </TableRow>
                  ))
                ) : (
                  <TableRow>
                    <TableCell
                      colSpan={columnsWithSelection.length}
                      className="h-24 text-center"
                    >
                      Sin resultados.
                    </TableCell>
                  </TableRow>
                )}
              </TableBody>
            </Table>
          </div>
          {(!onPaginationChange || controlledPageCount !== undefined) && (
            <DataTablePagination
              pageIndex={table.getState().pagination.pageIndex}
              pageSize={table.getState().pagination.pageSize}
              pageCount={table.getPageCount()}
              totalItems={totalItems ?? table.getFilteredRowModel().rows.length}
              onPageChange={table.setPageIndex}
              onPageSizeChange={table.setPageSize}
            />
          )}
        </>
      ) : displayViewMode === "card" && cardRenderer ? (
        <div className="grid gap-3 sm:grid-cols-2 sm:gap-4 lg:grid-cols-3">
          {displayItems.map((item, idx) => (
            <React.Fragment key={idx}>
              {cardRenderer(item)}
            </React.Fragment>
          ))}
          {displayItems.length === 0 && (
            <div className="col-span-full flex h-24 items-center justify-center rounded-md border text-muted-foreground">
              Sin resultados.
            </div>
          )}
        </div>
      ) : displayViewMode === "list" && listRenderer ? (
        <div className="space-y-2">
          {displayItems.map((item, idx) => (
            <React.Fragment key={idx}>
              {listRenderer(item)}
            </React.Fragment>
          ))}
          {displayItems.length === 0 && (
            <div className="flex h-24 items-center justify-center rounded-md border text-muted-foreground">
              Sin resultados.
            </div>
          )}
        </div>
      ) : (
        <div className="w-full overflow-x-auto rounded-md border">
          <Table className="min-w-[720px]">
            <TableHeader>
              {table.getHeaderGroups().map((headerGroup) => (
                <TableRow key={headerGroup.id}>
                  {headerGroup.headers.map((header) => (
                    <TableHead key={header.id} className="px-4 py-3">
                      {flexRender(
                        header.column.columnDef.header,
                        header.getContext()
                      )}
                    </TableHead>
                  ))}
                </TableRow>
              ))}
            </TableHeader>
            <TableBody>
              {table.getRowModel().rows?.length ? (
                table.getRowModel().rows.map((row) => (
                  <TableRow key={row.id}>
                    {row.getVisibleCells().map((cell) => (
                      <TableCell key={cell.id} className="px-4 py-3">
                        {flexRender(
                          cell.column.columnDef.cell,
                          cell.getContext()
                        )}
                      </TableCell>
                    ))}
                  </TableRow>
                ))
              ) : (
                <TableRow>
                  <TableCell
                    colSpan={columnsWithSelection.length}
                    className="h-24 text-center"
                  >
                    Sin resultados.
                  </TableCell>
                </TableRow>
              )}
            </TableBody>
          </Table>
        </div>
      )}
    </div>
  );
}
