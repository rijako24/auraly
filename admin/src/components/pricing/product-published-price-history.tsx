"use client";

import { useState } from "react";
import { History } from "lucide-react";
import { DataTablePagination } from "@/components/tables/data-table-pagination";
import { ProductFormSection } from "@/components/products/product-create-workspace";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { useProductPriceHistory } from "@/hooks/use-pricing";
import { formatCurrency, formatDateTime } from "@/lib/utils";

export function ProductPublishedPriceHistory({ productId }: { productId: string }) {
  const [page, setPage] = useState(1);
  const history = useProductPriceHistory(productId, true, page, 5, "Publication");
  const publications = history.data?.items ?? [];

  return <ProductFormSection
    id="product-price-history"
    icon={History}
    title="Historial de precios publicados"
    description="Cada publicación de esta preparación, empezando por la más reciente."
  >
    {history.isLoading ? <div className="h-28 animate-pulse rounded-xl bg-muted" />
      : history.isError ? <p className="rounded-xl border border-destructive/30 p-5 text-sm text-destructive">No fue posible cargar el historial de precios.</p>
      : publications.length === 0 ? <p className="rounded-xl border border-dashed p-6 text-center text-sm text-muted-foreground">Este producto todavía no tiene precios publicados.</p>
      : <div className="overflow-x-auto rounded-xl border">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Fecha</TableHead>
              <TableHead>Costo</TableHead>
              <TableHead>Margen</TableHead>
              <TableHead>Precio publicado</TableHead>
              <TableHead>Publicado por</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {publications.map((item) => <TableRow key={item.activityId}>
              <TableCell className="whitespace-nowrap">{formatDateTime(item.occurredAt)}</TableCell>
              <TableCell className="font-medium">{item.costBasisAmount == null ? "Sin costo" : formatCurrency(item.costBasisAmount)}</TableCell>
              <TableCell>{percent(item.effectiveMarginPercent)}</TableCell>
              <TableCell className="font-semibold">{formatCurrency(item.preparedAmount)}</TableCell>
              <TableCell>{item.userName}</TableCell>
            </TableRow>)}
          </TableBody>
        </Table>
      </div>}
    <DataTablePagination pageIndex={Math.max(0,(history.data?.page??page)-1)} pageSize={5}
      pageCount={history.data?.totalPages??0} totalItems={history.data?.totalCount??0}
      pageSizeOptions={[5]} onPageChange={index=>setPage(index+1)} onPageSizeChange={()=>{}} />
  </ProductFormSection>;
}

function percent(value: number | null) {
  return value == null ? "Sin calcular" : `${new Intl.NumberFormat("es-CO", { maximumFractionDigits: 4 }).format(value)} %`;
}
