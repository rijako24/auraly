"use client";

import * as React from "react";
import {
  ChevronLeft,
  ChevronRight,
  ChevronsLeft,
  ChevronsRight,
} from "lucide-react";

import { cn } from "@/lib/utils";
import { Button } from "@/components/ui/button";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";

export interface DataTablePaginationProps {
  pageIndex: number;
  pageSize: number;
  pageCount: number;
  totalItems?: number;
  onPageChange: (page: number) => void;
  onPageSizeChange: (pageSize: number) => void;
  pageSizeOptions?: number[];
  className?: string;
}

const DEFAULT_PAGE_SIZE_OPTIONS = [10, 20, 30, 50, 100];

export function DataTablePagination({
  pageIndex,
  pageSize,
  pageCount,
  totalItems,
  onPageChange,
  onPageSizeChange,
  pageSizeOptions = DEFAULT_PAGE_SIZE_OPTIONS,
  className,
}: DataTablePaginationProps) {
  const canPreviousPage = pageIndex > 0;
  const canNextPage = pageIndex < pageCount - 1;

  const startItem = pageIndex * pageSize + 1;
  const endItem = Math.min((pageIndex + 1) * pageSize, totalItems ?? 0);

  const getPageNumbers = (): (number | "ellipsis")[] => {
    if (pageCount <= 7) {
      return Array.from({ length: pageCount }, (_, i) => i);
    }
    if (pageIndex < 4) {
      return [0, 1, 2, 3, 4, "ellipsis", pageCount - 1];
    }
    if (pageIndex > pageCount - 5) {
      return [0, "ellipsis", ...Array.from({ length: 5 }, (_, i) => pageCount - 5 + i)];
    }
    return [
      0,
      "ellipsis",
      pageIndex - 1,
      pageIndex,
      pageIndex + 1,
      "ellipsis",
      pageCount - 1,
    ];
  };

  return (
    <div
      className={cn(
        "flex flex-col items-stretch justify-between gap-3 px-1 py-3 sm:flex-row sm:items-center sm:gap-4 sm:px-2 sm:py-4",
        className
      )}
    >
      <div className="flex flex-1 flex-wrap items-center justify-between gap-2 sm:justify-start sm:gap-4">
        <div className="flex items-center justify-end gap-1 sm:gap-2">
          <p className="text-xs text-muted-foreground sm:text-sm">
            Filas por página
          </p>
          <Select
            value={pageSize.toString()}
            onValueChange={(value) => onPageSizeChange(Number(value))}
          >
            <SelectTrigger className="h-8 w-[70px]">
              <SelectValue />
            </SelectTrigger>
            <SelectContent side="top">
              {pageSizeOptions.map((size) => (
                <SelectItem key={size} value={size.toString()}>
                  {size}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
        {totalItems !== undefined && (
          <p className="text-xs text-muted-foreground sm:text-sm">
            {startItem}-{endItem} de {totalItems}
          </p>
        )}
      </div>
      <div className="flex items-center justify-end gap-1 sm:gap-2">
        <Button
          variant="outline"
          size="icon"
          className="hidden h-8 w-8 sm:inline-flex"
          onClick={() => onPageChange(0)}
          disabled={!canPreviousPage}
        >
          <ChevronsLeft className="h-4 w-4" />
          <span className="sr-only">Primera página</span>
        </Button>
        <Button
          variant="outline"
          size="icon"
          className="h-8 w-8"
          onClick={() => onPageChange(pageIndex - 1)}
          disabled={!canPreviousPage}
        >
          <ChevronLeft className="h-4 w-4" />
          <span className="sr-only">Página anterior</span>
        </Button>
        <div className="hidden items-center gap-1 sm:flex">
          {getPageNumbers().map((page, idx) =>
            page === "ellipsis" ? (
              <span key={`ellipsis-${idx}`} className="px-2 text-sm">
                ...
              </span>
            ) : (
              <Button
                key={page}
                variant={pageIndex === page ? "default" : "outline"}
                size="icon"
                className="h-8 w-8"
                onClick={() => onPageChange(page)}
              >
                {page + 1}
              </Button>
            )
          )}
        </div>
        <Button
          variant="outline"
          size="icon"
          className="h-8 w-8"
          onClick={() => onPageChange(pageIndex + 1)}
          disabled={!canNextPage}
        >
          <ChevronRight className="h-4 w-4" />
          <span className="sr-only">Siguiente página</span>
        </Button>
        <Button
          variant="outline"
          size="icon"
          className="hidden h-8 w-8 sm:inline-flex"
          onClick={() => onPageChange(pageCount - 1)}
          disabled={!canNextPage}
        >
          <ChevronsRight className="h-4 w-4" />
          <span className="sr-only">Última página</span>
        </Button>
      </div>
    </div>
  );
}
