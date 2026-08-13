"use client";

import type { KeyboardEvent } from "react";
import { Trash2 } from "lucide-react";

import type { InventoryOperationLine } from "./inventory-operation-workspace";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { formatCurrency } from "@/lib/utils";

export type AdjustmentValuationBasis = "Cost" | "SalePrice";

export function adjustmentUnitValue(
  line: InventoryOperationLine,
  basis: AdjustmentValuationBasis,
) {
  const source = basis === "SalePrice" ? line.salePrice : line.cost;
  const value = Number(source);
  return Number.isFinite(value) && value > 0 ? value : 0;
}

export function AdjustmentCaptureGrid({
  lines,
  valuationBasis,
  update,
  remove,
  onKey,
}: {
  lines: InventoryOperationLine[];
  valuationBasis: AdjustmentValuationBasis;
  update: (index: number, patch: Partial<InventoryOperationLine>) => void;
  remove: (index: number) => void;
  onKey: (event: KeyboardEvent<HTMLInputElement>, index: number) => void;
}) {
  return (
    <div className="overflow-x-auto rounded-xl border">
      <table className="w-full min-w-[900px] text-sm">
        <thead className="bg-muted/60">
          <tr>
            <th className="px-3 py-3 text-left">Producto</th>
            <th className="px-3 py-3 text-right">Saldo</th>
            <th className="px-3 py-3 text-left">Cantidad (+ / −)</th>
            <th className="px-3 py-3 text-right">Valor unitario</th>
            <th className="px-3 py-3 text-right">Valor total</th>
            <th className="w-14" />
          </tr>
        </thead>
        <tbody>
          {lines.length === 0 ? (
            <tr>
              <td colSpan={6} className="p-10 text-center text-muted-foreground">
                Selecciona una bodega y agrega los productos del ajuste.
              </td>
            </tr>
          ) : lines.map((line, index) => {
            const quantity = Number(line.quantity);
            const unitValue = adjustmentUnitValue(
              line,
              quantity > 0 ? valuationBasis : "Cost",
            );
            return (
              <tr key={line.productId} className="border-t focus-within:bg-emerald-50/60">
                <td className="px-3 py-2">
                  <span className="block">{line.productName}</span>
                  <span className="block text-xs text-muted-foreground">
                    {line.productCode} · {line.unitCode}
                  </span>
                </td>
                <td className="px-3 py-2 text-right tabular-nums">{line.stock}</td>
                <td className="px-3 py-2">
                  <Input
                    data-inventory-row={index}
                    data-testid={`inventory-quantity-${index}`}
                    className="w-36 text-right tabular-nums"
                    inputMode="decimal"
                    value={line.quantity}
                    onChange={(event) => update(index, { quantity: event.target.value })}
                    onKeyDown={(event) => onKey(event, index)}
                    aria-label={`Cantidad de ${line.productName}`}
                  />
                </td>
                <td className="px-3 py-2 text-right tabular-nums">
                  {unitValue > 0 ? formatCurrency(unitValue) : "Sin valor configurado"}
                </td>
                <td className="px-3 py-2 text-right tabular-nums">
                  {unitValue > 0 && Number.isFinite(quantity)
                    ? formatCurrency(Math.abs(quantity) * unitValue)
                    : "—"}
                </td>
                <td className="px-2 py-2">
                  <Button type="button" size="icon" variant="ghost" onClick={() => remove(index)} aria-label={`Eliminar ${line.productName}`}>
                    <Trash2 className="h-4 w-4" />
                  </Button>
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}
