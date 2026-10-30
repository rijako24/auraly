"use client";

import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { goodsReceiptsApi, type GoodsReceiptLine, type GoodsReceiptOptions, type GoodsReceiptVoucherCost, type GoodsReceiptVoucherCostLine } from "@/services/api/goods-receipts";
import { PartySelect } from "@/components/parties/party-role-select";
import { ServerSearchInput } from "@/components/tables/server-search-input";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { FormattedNumberInput } from "@/components/ui/formatted-number-input";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { formatCurrency } from "@/lib/utils";

export function GoodsReceiptVoucherDialog({ businessId, initialPartyId, existing,
  associations, receiptLines, options, onAdd, onUpdateReceiptLine, onClose }: {
  businessId: string; initialPartyId?: string;
  existing?: GoodsReceiptVoucherCost; associations: GoodsReceiptVoucherCost[];
  receiptLines: GoodsReceiptLine[]; options: GoodsReceiptOptions;
  onAdd: (value: GoodsReceiptVoucherCost) => void;
  onUpdateReceiptLine?: (lineNumber: number, values: Partial<GoodsReceiptLine>) => void;
  onClose: () => void;
}) {
  const [partyId, setPartyId] = useState(initialPartyId ?? "");
  const [search, setSearch] = useState("");
  const [page, setPage] = useState(1);
  const [entryId, setEntryId] = useState(existing?.entryId ?? "");
  const [mode, setMode] = useState<GoodsReceiptVoucherCost["mode"]>(existing?.mode ?? "Associate");
  const [selected, setSelected] = useState<GoodsReceiptVoucherCostLine[]>(existing?.lines ?? []);
  const voucher = useQuery({ queryKey: ["goods-receipt-cost-voucher", businessId, entryId],
    queryFn: () => goodsReceiptsApi.costVoucher(entryId), enabled: Boolean(entryId), staleTime: 0 });
  const candidates = useQuery({ queryKey: ["goods-receipt-cost-vouchers", businessId, partyId, search, page],
    queryFn: () => goodsReceiptsApi.costVouchers(partyId, search, page), enabled: Boolean(partyId), staleTime: 60_000 });
  const methods = options.purchaseCostAllocationMethods.filter(item => item.code !== "None");
  const updateLine = (number: number, patch: Partial<GoodsReceiptVoucherCostLine>) => {
    setSelected(current => {
      const previous = current.find(line => line.entryLineNumber === number);
      const next: GoodsReceiptVoucherCostLine = { entryLineNumber: number, functionalAmount: 0,
        allocationMethod: methods.find(item => item.code === "Value")?.code ?? methods[0]?.code ?? "None",
        ...previous, ...patch };
      return [...current.filter(line => line.entryLineNumber !== number), next].sort((a, b) => a.entryLineNumber - b.entryLineNumber);
    });
  };
  const chosen = selected.filter(line => line.functionalAmount > 0);
  const selectedExisting = associations.find(item => item.entryId === entryId);
  const validDistribution = receiptLines.length > 0 && chosen.length > 0 && chosen.every(line => {
    const original = voucher.data?.lines.find(item => item.lineNumber === line.entryLineNumber);
    return original && line.functionalAmount <= original.eligibleFunctionalAmount - original.assignedFunctionalAmount &&
      methods.some(method => method.code === line.allocationMethod) &&
      (line.allocationMethod !== "Weight" || receiptLines.every(product => (product.totalGrossWeightKg ?? 0) > 0)) &&
      (line.allocationMethod !== "Volume" || receiptLines.every(product => (product.totalVolumeM3 ?? 0) > 0)) &&
      (line.allocationMethod !== "Manual" || (line.manualAllocations?.length === receiptLines.length &&
        Math.abs(line.manualAllocations.reduce((sum, item) => sum + item.functionalAmount, 0) - line.functionalAmount) < .00005));
  });
  return <Dialog open onOpenChange={open => { if (!open) onClose(); }}>
    <DialogContent className="flex max-h-[92dvh] flex-col sm:max-w-5xl">
      <DialogHeader><DialogTitle>Agregar comprobante a la compra</DialogTitle>
        <DialogDescription>Asocia un comprobante contabilizado o distribuye parte de sus costos. Su factura, impuestos y cuenta por pagar se conservan.</DialogDescription></DialogHeader>
      <div className="space-y-4 overflow-y-auto px-1">
        <div className="grid gap-3 sm:grid-cols-2">
          <div className="space-y-2"><Label>Tercero</Label><PartySelect value={partyId} ariaLabel="Tercero de los comprobantes de compra"
            onChange={value => { setPartyId(value); setEntryId(""); setSelected([]); setSearch(""); setPage(1); }} /></div>
          <div className="space-y-2"><Label>Buscar por documento, cuenta o concepto</Label>
            <ServerSearchInput value={search} onSearch={value => { setSearch(value); setPage(1); }}
              isSearching={candidates.isFetching} placeholder="Buscar comprobantes del tercero…" /></div>
        </div>
        {partyId && <div className="space-y-2"><Label>Costos del tercero</Label>
          {candidates.isPending && <p role="status" className="text-sm text-muted-foreground">Consultando comprobantes…</p>}
          {candidates.isError && <p role="alert" className="text-sm text-destructive">No se pudieron consultar los comprobantes. <Button variant="link" onClick={() => void candidates.refetch()}>Reintentar</Button></p>}
          {candidates.data && <><div className="overflow-x-auto rounded-xl border"><table className="w-full min-w-[920px] text-sm">
            <thead className="bg-muted"><tr><th className="p-2 text-left">Tercero</th><th className="p-2 text-left">Documento</th>
              <th className="p-2 text-left">Descripción / observación</th><th className="p-2 text-left">Cuenta y concepto de costo</th>
              <th className="p-2 text-right">Costo</th><th className="p-2 text-right">Disponible</th><th className="p-2 text-right">Acción</th></tr></thead>
            <tbody>{candidates.data.items.map(item => <tr key={item.entryId} className={`border-t ${entryId === item.entryId ? "bg-primary/10" : ""}`}>
              <td className="p-2">{item.partyName}</td><td className="p-2"><strong>{item.documentNumber}</strong><p className="text-xs text-muted-foreground">{item.entryNumber} · {item.occurredAt.slice(0, 10)}</p></td>
              <td className="p-2">{item.description}</td><td className="p-2"><strong>{item.accountCode} · {item.accountName}</strong><p>{item.concept}</p>
                {item.eligibleLineCount > 1 && <p className="text-xs text-muted-foreground">{item.eligibleLineCount} conceptos de costo</p>}</td>
              <td className="p-2 text-right">{formatCurrency(item.eligibleFunctionalAmount, "COP", 4)}</td>
              <td className="p-2 text-right">{formatCurrency(Math.max(0, item.eligibleFunctionalAmount - item.assignedFunctionalAmount), "COP", 4)}</td>
              <td className="p-2 text-right"><Button type="button" size="sm" variant={entryId === item.entryId ? "secondary" : "outline"}
                aria-label={`Seleccionar documento ${item.documentNumber}`} aria-pressed={entryId === item.entryId}
                onClick={() => { const previous = associations.find(value => value.entryId === item.entryId);
                  setEntryId(item.entryId); setSelected(previous?.lines ?? []); setMode(previous?.mode ?? "Associate"); }}>Seleccionar</Button></td>
            </tr>)}</tbody></table></div>
            {candidates.data.items.length === 0 && <p className="text-sm text-muted-foreground">No hay comprobantes de costo contabilizados para este tercero.</p>}
            {candidates.data.totalPages > 1 && <div className="flex items-center justify-end gap-2 text-sm"><Button type="button" variant="outline" size="sm" disabled={page <= 1} onClick={() => setPage(value => value - 1)}>Anterior</Button>
              <span>Página {page} de {candidates.data.totalPages}</span><Button type="button" variant="outline" size="sm" disabled={page >= candidates.data.totalPages} onClick={() => setPage(value => value + 1)}>Siguiente</Button></div>}
          </>}
        </div>}
        {voucher.isFetching && <p role="status" className="text-sm text-muted-foreground">Consultando partidas y costos disponibles…</p>}
        {voucher.isError && <p role="alert" className="text-sm text-destructive">No se pudo consultar el comprobante. <Button variant="link" onClick={() => void voucher.refetch()}>Reintentar</Button></p>}
        {entryId && voucher.isSuccess && !voucher.data && <p role="alert" className="text-sm text-destructive">El comprobante no está disponible en esta sede.</p>}
        {voucher.data && <>
          {selectedExisting && <p className="text-sm text-muted-foreground">Este comprobante ya está en esta captura. Los cambios reemplazan su asociación.</p>}
          <div className="space-y-2"><Label>Cómo agregarlo</Label><Select value={mode} onValueChange={value => {
            if (value === "Associate" || value === "Distribute") setMode(value);
          }}><SelectTrigger aria-label="Cómo aplicar el comprobante"><SelectValue /></SelectTrigger><SelectContent>
            {options.purchaseCostAssociationModes?.map(item => <SelectItem key={item.code} value={item.code}>{item.label}</SelectItem>)}
          </SelectContent></Select><p className="text-xs text-muted-foreground">{options.purchaseCostAssociationModes?.find(item => item.code === mode)?.description}</p></div>
          <div className="overflow-x-auto rounded-xl border"><table className="w-full text-sm"><thead className="bg-muted"><tr>
            <th className="p-2 text-left">Cuenta / tercero / concepto de costo</th><th className="p-2 text-right">Valor del costo</th>
            {mode === "Distribute" && <><th className="p-2 text-right">Disponible para repartir</th><th className="p-2">Aplicar en esta compra (COP)</th><th className="p-2">Reparto</th></>}
          </tr></thead><tbody>{voucher.data.lines.filter(line => line.eligibleFunctionalAmount > 0).map(line => {
            const capture = selected.find(item => item.entryLineNumber === line.lineNumber);
            const available = Math.max(0, line.eligibleFunctionalAmount - line.assignedFunctionalAmount);
            return <tr key={line.lineNumber} className="border-t align-top"><td className="p-2"><b>{line.accountCode} · {line.accountName}</b><p>{line.partyName}</p><p className="text-muted-foreground">{line.description}</p></td>
              <td className="p-2 text-right">{formatCurrency(line.eligibleFunctionalAmount, "COP", 4)}</td>
              {mode === "Distribute" && <><td className="p-2 text-right">{formatCurrency(available, "COP", 4)}<p className="text-xs text-muted-foreground">Repartido: {formatCurrency(line.assignedFunctionalAmount, "COP", 4)}</p></td>
                <td className="min-w-40 p-2"><FormattedNumberInput ariaLabel={`Importe a repartir de ${line.accountCode}`}
                  disabled={available <= 0} value={capture?.functionalAmount ?? 0}
                  onValueChange={value => updateLine(line.lineNumber, { functionalAmount: value ?? 0 })} />
                  {capture && capture.functionalAmount > available && <p role="alert" className="text-xs text-destructive">Supera el disponible.</p>}</td>
                <td className="min-w-40 p-2">{available > 0 && <><Select value={capture?.allocationMethod ?? ""} onValueChange={value => {
                  const method = methods.find(item => item.code === value);
                  if (method) updateLine(line.lineNumber, { allocationMethod: method.code, manualAllocations: null });
                }}><SelectTrigger aria-label={`Método de reparto de ${line.accountCode}`}><SelectValue placeholder={methods.find(item => item.code === "Value")?.label ?? "Seleccionar reparto"} /></SelectTrigger><SelectContent>
                  {methods.map(item => <SelectItem key={item.code} value={item.code}>{item.label}</SelectItem>)}
                </SelectContent></Select>{capture?.allocationMethod === "Manual" && <div className="mt-2 space-y-2">{receiptLines.map(product => <div key={product.lineNumber}>
                  <Label className="text-xs">{product.lineNumber}. {product.description}</Label><FormattedNumberInput
                    ariaLabel={`Reparto manual partida ${line.lineNumber} producto ${product.lineNumber}`}
                    value={capture.manualAllocations?.find(item => item.receiptLineNumber === product.lineNumber)?.functionalAmount ?? 0}
                    onValueChange={value => updateLine(line.lineNumber, { manualAllocations: receiptLines.map(item => ({
                      receiptLineNumber: item.lineNumber, functionalAmount: item.lineNumber === product.lineNumber ? value ?? 0 :
                        capture.manualAllocations?.find(previous => previous.receiptLineNumber === item.lineNumber)?.functionalAmount ?? 0,
                    })) })} /></div>)}</div>}</>}</td></>}
            </tr>;
          })}</tbody></table></div>
          {mode === "Distribute" && selected.some(line => line.allocationMethod === "Weight") &&
            <div className="space-y-2 rounded-xl border p-3"><p className="font-medium">Peso de los productos</p>
              {receiptLines.map(product => <div key={product.lineNumber} className="flex flex-wrap items-center justify-between gap-2 text-sm">
                <span>{product.lineNumber}. {product.description}</span>
                {product.unitGrossWeightKg && product.unitGrossWeightKg > 0
                  ? <strong>{product.totalGrossWeightKg ?? 0} kg</strong>
                  : <label className="flex items-center gap-2">Peso total (kg)
                    <Input type="number" min="0.000001" step="0.001" className="w-32"
                      aria-label={`Peso total de ${product.description}`} disabled={!onUpdateReceiptLine}
                      value={product.totalGrossWeightKg ?? ""} onChange={event => onUpdateReceiptLine?.(product.lineNumber,
                        { totalGrossWeightKg: event.target.value === "" ? null : Number(event.target.value) })} /></label>}
              </div>)}
              {receiptLines.some(product => (product.totalGrossWeightKg ?? 0) <= 0) &&
                <p role="alert" className="text-sm text-destructive">Para repartir por peso, cada producto necesita un peso positivo. El valor no se agregará hasta completarlo.</p>}
            </div>}
          {mode === "Distribute" && <p className="text-sm">Se repartirán <b>{formatCurrency(chosen.reduce((sum, line) => sum + line.functionalAmount, 0), "COP", 4)}</b> entre los productos. Se validará nuevamente el disponible al confirmar la entrada.</p>}
          {mode === "Distribute" && !voucher.data.lines.some(line => line.eligibleFunctionalAmount > line.assignedFunctionalAmount) &&
            <p role="alert" className="text-sm text-muted-foreground">Este comprobante no tiene costos disponibles para repartir. Puedes asociarlo informativamente.</p>}
        </>}
      </div>
      <DialogFooter><Button variant="outline" onClick={onClose}>Cancelar</Button><Button disabled={!voucher.data || voucher.isFetching || voucher.isError ||
        !options.purchaseCostAssociationModes?.some(item => item.code === mode) || (mode === "Distribute" && !validDistribution)}
        onClick={() => { if (voucher.data) onAdd({ associationId: selectedExisting?.associationId ?? existing?.associationId ?? crypto.randomUUID(), entryId,
          entryNumber: voucher.data.entryNumber, mode, lines: mode === "Distribute" ? chosen : [] }); }}>
        {existing || selectedExisting ? "Actualizar comprobante" : "Agregar comprobante"}</Button></DialogFooter>
    </DialogContent>
  </Dialog>;
}
