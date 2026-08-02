"use client";

import { useMemo, useRef, useState, type KeyboardEvent } from "react";
import type { ColumnDef } from "@tanstack/react-table";
import {
  ArchiveRestore, Barcode, CircleDollarSign, PackagePlus, Plus, Save,
  Search, Trash2, Truck, Warehouse,
} from "lucide-react";
import { toast } from "sonner";
import { DataTable } from "@/components/tables/data-table";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import {
  Dialog, DialogContent, DialogDescription, DialogFooter,
  DialogHeader, DialogTitle,
} from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  Select, SelectContent, SelectItem, SelectTrigger, SelectValue,
} from "@/components/ui/select";
import { Textarea } from "@/components/ui/textarea";
import {
  useConfirmGoodsReceipt, useDeleteGoodsReceiptDraft, useGoodsReceiptOptions,
  useGoodsReceiptProducts, useGoodsReceipts, useSaveGoodsReceiptDraft,
} from "@/hooks/use-goods-receipts";
import {
  goodsReceiptsApi, type GoodsReceiptDraft, type GoodsReceiptLine,
  type GoodsReceiptListItem, type GoodsReceiptProduct, type GoodsReceiptStatus,
  type SaveGoodsReceiptDraftRequest,
} from "@/services/api/goods-receipts";
import { useAuthStore } from "@/stores/auth-store";
import { useBusinessContextStore } from "@/stores/business-context-store";
import { formatCurrency, formatDateTime } from "@/lib/utils";

type EditorDraft = {
  draftId: string; warehouseId: string; supplierId: string;
  supplierInvoiceNumber: string; supplierInvoiceDate: string; receivedAt: string;
  createsPayable: boolean; dueDate: string; notes: string;
  lines: GoodsReceiptLine[]; concurrencyToken: string | null;
};

const statusLabels: Record<GoodsReceiptStatus, string> = {
  Draft: "Borrador", Accepted: "En proceso", Processed: "Procesada",
};

export default function GoodsReceiptsPage() {
  const businessId = useBusinessContextStore((state) => state.selectedBusinessId);
  const permissions = useAuthStore((state) => new Set(state.user?.permissions ?? []));
  const canCreate = permissions.has("purchasing.goods-receipts.create");
  const canConfirm = permissions.has("purchasing.goods-receipts.confirm");
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(25);
  const [search, setSearch] = useState("");
  const [status, setStatus] = useState<GoodsReceiptStatus | "all">("all");
  const [editor, setEditor] = useState<EditorDraft>();
  const list = useGoodsReceipts({
    page, pageSize, search: search.trim() || undefined,
    status: status === "all" ? undefined : status,
  });

  const columns = useMemo<ColumnDef<GoodsReceiptListItem>[]>(() => [
    {
      accessorKey: "documentNumber", header: "Documento",
      cell: ({ row }) => <div>
        <p className="font-semibold">{row.original.documentNumber ?? "Borrador sin numerar"}</p>
        <p className="text-xs text-muted-foreground">
          {row.original.supplierInvoiceNumber
            ? `Factura proveedor ${row.original.supplierInvoiceNumber}`
            : "Sin factura de proveedor"}
        </p>
      </div>,
    },
    {
      accessorKey: "supplierName", header: "Proveedor",
      cell: ({ row }) => row.original.supplierName ?? "Por seleccionar",
    },
    {
      accessorKey: "warehouseName", header: "Bodega",
      cell: ({ row }) => row.original.warehouseName ?? "Por seleccionar",
    },
    {
      accessorKey: "grandTotal", header: "Total",
      cell: ({ row }) => formatCurrency(row.original.grandTotal),
    },
    {
      accessorKey: "status", header: "Estado",
      cell: ({ row }) => <Badge variant={row.original.status === "Processed" ? "secondary" : "outline"}>
        {statusLabels[row.original.status]}
      </Badge>,
    },
    {
      accessorKey: "updatedAt", header: "Actualizada",
      cell: ({ row }) => formatDateTime(row.original.updatedAt),
    },
  ], []);

  const newEntry = () => {
    if (!businessId) return;
    setEditor(emptyDraft());
  };

  const openEntry = async (item: GoodsReceiptListItem) => {
    if (item.status !== "Draft") {
      toast.info("La entrada confirmada es inmutable. Consulta su trazabilidad en inventario y cuentas por pagar.");
      return;
    }
    try {
      setEditor(fromDraft(await goodsReceiptsApi.getDraft(item.documentId)));
    } catch {
      toast.error("No fue posible recuperar el borrador.");
    }
  };

  return <div className="space-y-6">
    <header className="flex flex-col justify-between gap-4 lg:flex-row lg:items-end">
      <div>
        <p className="text-sm font-medium text-primary">Compras e inventario</p>
        <h1 className="text-3xl font-semibold tracking-tight">Entradas de mercanc?a</h1>
        <p className="mt-1 max-w-3xl text-muted-foreground">
          Recibe productos por proveedor. Al confirmar, el motor actualiza inventario,
          costo promedio, cuenta por pagar y propuesta de precio en el orden del negocio.
        </p>
      </div>
      {canCreate && <Button onClick={newEntry}><Plus className="mr-2 h-4 w-4" /> Nueva entrada</Button>}
    </header>

    <section className="grid gap-3 md:grid-cols-3">
      <Summary icon={PackagePlus} label="Documentos encontrados" value={String(list.data?.totalCount ?? 0)} />
      <Summary icon={Warehouse} label="Movimiento" value="Inventario y costo" />
      <Summary icon={CircleDollarSign} label="Resultado" value="Cuenta por pagar" />
    </section>

    <section className="grid gap-3 rounded-2xl border bg-card p-4 md:grid-cols-[minmax(0,1fr)_14rem]">
      <div className="relative">
        <Search className="pointer-events-none absolute left-3 top-2.5 h-4 w-4 text-muted-foreground" />
        <Input className="pl-9" value={search}
          onChange={(event) => { setSearch(event.target.value); setPage(1); }}
          placeholder="Documento, proveedor, factura o bodega" />
      </div>
      <Select value={status} onValueChange={(value) => {
        setStatus(value as GoodsReceiptStatus | "all"); setPage(1);
      }}>
        <SelectTrigger><SelectValue /></SelectTrigger>
        <SelectContent>
          <SelectItem value="all">Todos los estados</SelectItem>
          <SelectItem value="Draft">Borradores</SelectItem>
          <SelectItem value="Accepted">En proceso</SelectItem>
          <SelectItem value="Processed">Procesadas</SelectItem>
        </SelectContent>
      </Select>
    </section>

    <DataTable columns={columns} data={list.data?.items ?? []} isLoading={list.isLoading}
      page={list.data?.page} pageSize={list.data?.pageSize}
      pageCount={list.data?.totalPages} totalItems={list.data?.totalCount}
      onPaginationChange={(next, size) => { setPage(next); setPageSize(size); }}
      onRowClick={openEntry} enableRowSelection={false} />

    <ReceiptEditor open={!!editor} draft={editor} businessId={businessId}
      canConfirm={canConfirm} onChange={setEditor}
      onClose={() => setEditor(undefined)} />
  </div>;
}

function ReceiptEditor({ open, draft, businessId, canConfirm, onChange, onClose }: {
  open: boolean; draft?: EditorDraft; businessId: string | null; canConfirm: boolean;
  onChange: (draft: EditorDraft) => void; onClose: () => void;
}) {
  const options = useGoodsReceiptOptions();
  const [productSearch, setProductSearch] = useState("");
  const products = useGoodsReceiptProducts(draft?.supplierId || undefined, productSearch);
  const save = useSaveGoodsReceiptDraft();
  const remove = useDeleteGoodsReceiptDraft();
  const confirm = useConfirmGoodsReceipt();
  const scanRef = useRef<HTMLInputElement>(null);

  if (!draft || !businessId) return null;
  const totals = calculateTotals(draft.lines);

  const change = (values: Partial<EditorDraft>) => onChange({ ...draft, ...values });
  const request = (): SaveGoodsReceiptDraftRequest => ({
    draftId: draft.draftId, businessId,
    warehouseId: draft.warehouseId || null, supplierId: draft.supplierId || null,
    supplierInvoiceNumber: draft.supplierInvoiceNumber.trim() || null,
    supplierInvoiceDate: toIsoOrNull(draft.supplierInvoiceDate),
    receivedAt: toIso(draft.receivedAt), createsPayable: draft.createsPayable,
    dueDate: draft.createsPayable ? toIsoOrNull(draft.dueDate) : null,
    currencyCode: "COP", notes: draft.notes.trim() || null,
    lines: draft.lines, concurrencyToken: draft.concurrencyToken,
  });

  const persist = async (notify = true) => {
    try {
      const saved = await save.mutateAsync(request());
      change({ concurrencyToken: saved.concurrencyToken });
      if (notify) toast.success("Borrador guardado y disponible para recuperar.");
      return saved;
    } catch {
      toast.error("No fue posible guardar. El borrador pudo cambiar en otra sesi?n.");
      return null;
    }
  };

  const addProduct = (product: GoodsReceiptProduct) => {
    const existing = draft.lines.find((line) => line.productId === product.productId);
    const lines = existing
      ? draft.lines.map((line) => line.productId === product.productId
        ? { ...line, quantity: line.quantity + 1 } : line)
      : [...draft.lines, {
        lineNumber: draft.lines.length + 1, productId: product.productId,
        description: product.name, quantity: 1, unitCost: product.latestUnitCost ?? 0,
        discountAmount: 0, taxCode: product.taxCode, taxRate: product.taxRate,
        taxTreatment: product.taxRate > 0 ? "DeductibleInputVat" : "NotApplicable",
      } satisfies GoodsReceiptLine];
    change({ lines });
    setProductSearch("");
    requestAnimationFrame(() => scanRef.current?.focus());
  };

  const capture = (event: KeyboardEvent<HTMLInputElement>) => {
    if (event.key !== "Enter") return;
    event.preventDefault();
    const first = products.data?.items[0];
    if (first) addProduct(first);
  };

  const updateLine = (productId: string, values: Partial<GoodsReceiptLine>) =>
    change({ lines: draft.lines.map((line) =>
      line.productId === productId ? { ...line, ...values } : line) });

  const deleteDraft = async () => {
    if (!draft.concurrencyToken) { onClose(); return; }
    try {
      await remove.mutateAsync({ draftId: draft.draftId, concurrencyToken: draft.concurrencyToken });
      toast.success("El borrador fue eliminado.");
      onClose();
    } catch { toast.error("No fue posible eliminar el borrador."); }
  };

  const confirmEntry = async () => {
    if (!draft.warehouseId || !draft.supplierId || draft.lines.length === 0) {
      toast.error("Selecciona proveedor y bodega, y agrega al menos un producto.");
      return;
    }
    try {
      let confirmationToken = draft.concurrencyToken;
      if (!confirmationToken) {
        const saved = await persist(false);
        if (!saved) return;
        confirmationToken = saved.concurrencyToken;
      }
      const accepted = await confirm.mutateAsync({
        documentId: draft.draftId, businessId, warehouseId: draft.warehouseId,
        supplierId: draft.supplierId,
        supplierInvoiceNumber: draft.supplierInvoiceNumber.trim() || null,
        supplierInvoiceDate: toIsoOrNull(draft.supplierInvoiceDate),
        receivedAt: toIso(draft.receivedAt), createsPayable: draft.createsPayable,
        dueDate: draft.createsPayable ? toIsoOrNull(draft.dueDate) : null,
        currencyCode: "COP", notes: draft.notes.trim() || null, lines: draft.lines,
        draftConcurrencyToken: confirmationToken,
      });
      toast.success(`${accepted.documentNumber} fue confirmada y enviada al motor.`);
      onClose();
    } catch {
      toast.error("No fue posible confirmar. Revisa factura duplicada, permisos y datos.");
    }
  };

  return <Dialog open={open} onOpenChange={(value) => !value && onClose()}>
    <DialogContent className="flex max-h-[96dvh] max-w-[96vw] flex-col overflow-hidden p-0">
      <DialogHeader className="border-b px-6 py-5">
        <DialogTitle className="flex items-center gap-2">
          <Truck className="h-5 w-5 text-primary" /> Entrada de mercanc?a
        </DialogTitle>
        <DialogDescription>
          Borrador recuperable. El consecutivo se asigna ?nicamente al confirmar.
        </DialogDescription>
      </DialogHeader>

      <div className="flex-1 space-y-5 overflow-y-auto px-6 py-5">
        <section className="grid gap-4 rounded-2xl border bg-muted/20 p-4 md:grid-cols-4">
          <Field label="Proveedor">
            <Select value={draft.supplierId} onValueChange={(value) =>
              change({ supplierId: value, lines: [] })}>
              <SelectTrigger><SelectValue placeholder="Seleccionar proveedor" /></SelectTrigger>
              <SelectContent>{options.data?.suppliers.map((item) =>
                <SelectItem key={item.supplierId} value={item.supplierId}>
                  {item.name} ? {item.identification}
                </SelectItem>)}</SelectContent>
            </Select>
          </Field>
          <Field label="Bodega">
            <Select value={draft.warehouseId} onValueChange={(value) => change({ warehouseId: value })}>
              <SelectTrigger><SelectValue placeholder="Seleccionar bodega" /></SelectTrigger>
              <SelectContent>{options.data?.warehouses.map((item) =>
                <SelectItem key={item.warehouseId} value={item.warehouseId}>
                  {item.name} ? {item.code}
                </SelectItem>)}</SelectContent>
            </Select>
          </Field>
          <Field label="Factura del proveedor">
            <Input value={draft.supplierInvoiceNumber}
              onChange={(event) => change({ supplierInvoiceNumber: event.target.value })}
              placeholder="Opcional" maxLength={80} />
          </Field>
          <Field label="Fecha factura">
            <Input type="date" value={draft.supplierInvoiceDate}
              onChange={(event) => change({ supplierInvoiceDate: event.target.value })} />
          </Field>
          <Field label="Fecha de recepci?n">
            <Input type="datetime-local" value={draft.receivedAt}
              onChange={(event) => change({ receivedAt: event.target.value })} />
          </Field>
          <Field label="Condici?n">
            <Select value={draft.createsPayable ? "Credit" : "Cash"}
              onValueChange={(value) => change({
                createsPayable: value === "Credit",
                dueDate: value === "Credit" ? draft.dueDate || plusDays(30) : "",
              })}>
              <SelectTrigger><SelectValue /></SelectTrigger>
              <SelectContent>
                <SelectItem value="Credit">Cr?dito ? genera cuenta por pagar</SelectItem>
                <SelectItem value="Cash">Contado</SelectItem>
              </SelectContent>
            </Select>
          </Field>
          <Field label="Vencimiento">
            <Input type="date" disabled={!draft.createsPayable} value={draft.dueDate}
              onChange={(event) => change({ dueDate: event.target.value })} />
          </Field>
          <Field label="Notas">
            <Input value={draft.notes} onChange={(event) => change({ notes: event.target.value })}
              placeholder="Recepci?n, estado o referencia" maxLength={1000} />
          </Field>
        </section>

        <section className="rounded-2xl border">
          <div className="grid gap-3 border-b p-4 md:grid-cols-[minmax(0,1fr)_auto]">
            <div className="relative">
              <Barcode className="pointer-events-none absolute left-3 top-2.5 h-4 w-4 text-primary" />
              <Input ref={scanRef} className="pl-9" value={productSearch}
                disabled={!draft.supplierId}
                onChange={(event) => setProductSearch(event.target.value)}
                onKeyDown={capture}
                placeholder={draft.supplierId
                  ? "Escanea o busca por c?digo, referencia, nombre o c?digo del proveedor"
                  : "Selecciona primero el proveedor"} />
            </div>
            <Button type="button" variant="outline"
              disabled={!products.data?.items[0]}
              onClick={() => products.data?.items[0] && addProduct(products.data.items[0])}>
              <Plus className="mr-2 h-4 w-4" /> Agregar
            </Button>
          </div>
          {productSearch && (products.data?.items.length ?? 0) > 0 &&
            <div className="max-h-40 overflow-y-auto border-b bg-background p-2">
              {products.data?.items.slice(0, 8).map((product) =>
                <button key={product.productId} type="button"
                  className="flex w-full items-center justify-between rounded-lg px-3 py-2 text-left hover:bg-muted"
                  onClick={() => addProduct(product)}>
                  <span><strong>{product.name}</strong><span className="ml-2 text-xs text-muted-foreground">
                    {product.productCode}{product.supplierProductCode ? ` ? Prov. ${product.supplierProductCode}` : ""}
                  </span></span>
                  <span className="text-sm font-medium">{formatCurrency(product.latestUnitCost ?? 0)}</span>
                </button>)}
            </div>}
          <div className="overflow-x-auto">
            <table className="w-full min-w-[900px] text-sm">
              <thead className="bg-muted/50 text-left">
                <tr><th className="px-4 py-3">Producto</th><th className="w-32 px-3 py-3">Cantidad</th>
                  <th className="w-40 px-3 py-3">Costo unitario</th><th className="w-36 px-3 py-3">Descuento</th>
                  <th className="w-28 px-3 py-3">IVA</th><th className="w-36 px-3 py-3 text-right">Total</th>
                  <th className="w-14" /></tr>
              </thead>
              <tbody>{draft.lines.map((line) => {
                const lineTotal = calculateLine(line).total;
                return <tr key={line.productId} className="border-t">
                  <td className="px-4 py-3"><p className="font-semibold">{line.description}</p>
                    <p className="text-xs text-muted-foreground">{line.taxCode} ? {line.taxTreatment}</p></td>
                  <td className="px-3 py-2"><Input type="number" min="0.000001" step="0.001"
                    value={line.quantity} onChange={(event) =>
                      updateLine(line.productId, { quantity: Number(event.target.value) })} /></td>
                  <td className="px-3 py-2"><Input type="number" min="0" step="0.01"
                    value={line.unitCost} onChange={(event) =>
                      updateLine(line.productId, { unitCost: Number(event.target.value) })} /></td>
                  <td className="px-3 py-2"><Input type="number" min="0" step="0.01"
                    value={line.discountAmount} onChange={(event) =>
                      updateLine(line.productId, { discountAmount: Number(event.target.value) })} /></td>
                  <td className="px-3 py-2">{line.taxRate} %</td>
                  <td className="px-3 py-2 text-right font-semibold">{formatCurrency(lineTotal)}</td>
                  <td className="pr-3"><Button type="button" size="icon" variant="ghost"
                    aria-label={`Eliminar ${line.description}`}
                    onClick={() => change({ lines: draft.lines
                      .filter((item) => item.productId !== line.productId)
                      .map((item, index) => ({ ...item, lineNumber: index + 1 })) })}>
                    <Trash2 className="h-4 w-4" /></Button></td>
                </tr>;
              })}</tbody>
            </table>
            {draft.lines.length === 0 && <div className="py-14 text-center text-muted-foreground">
              Selecciona el proveedor y escanea el primer producto.
            </div>}
          </div>
        </section>

        <section className="grid gap-3 md:grid-cols-[minmax(0,1fr)_18rem]">
          <Textarea value={draft.notes} onChange={(event) => change({ notes: event.target.value })}
            placeholder="Observaciones de recepci?n" maxLength={1000} />
          <dl className="space-y-2 rounded-2xl bg-slate-950 p-5 text-white">
            <Amount label="Subtotal" value={totals.net} />
            <Amount label="Impuestos" value={totals.tax} />
            <Amount label="Total recibido" value={totals.total} strong />
          </dl>
        </section>
      </div>

      <DialogFooter className="border-t bg-background px-6 py-4 sm:justify-between">
        <Button type="button" variant="ghost" className="text-destructive" onClick={deleteDraft}>
          <Trash2 className="mr-2 h-4 w-4" /> Eliminar borrador
        </Button>
        <div className="flex gap-2">
          <Button type="button" variant="outline" onClick={onClose}>Cerrar</Button>
          <Button type="button" variant="secondary" disabled={save.isPending} onClick={() => persist()}>
            <Save className="mr-2 h-4 w-4" /> Guardar borrador
          </Button>
          {canConfirm && <Button type="button" disabled={confirm.isPending} onClick={confirmEntry}>
            <ArchiveRestore className="mr-2 h-4 w-4" /> Confirmar entrada
          </Button>}
        </div>
      </DialogFooter>
    </DialogContent>
  </Dialog>;
}

function emptyDraft(): EditorDraft {
  return {
    draftId: crypto.randomUUID(), warehouseId: "", supplierId: "",
    supplierInvoiceNumber: "", supplierInvoiceDate: new Date().toISOString().slice(0, 10),
    receivedAt: localDateTime(), createsPayable: true, dueDate: plusDays(30),
    notes: "", lines: [], concurrencyToken: null,
  };
}

function fromDraft(draft: GoodsReceiptDraft): EditorDraft {
  return {
    draftId: draft.draftId, warehouseId: draft.warehouseId ?? "",
    supplierId: draft.supplierId ?? "", supplierInvoiceNumber: draft.supplierInvoiceNumber ?? "",
    supplierInvoiceDate: draft.supplierInvoiceDate?.slice(0, 10) ?? "",
    receivedAt: localDateTime(draft.receivedAt), createsPayable: draft.createsPayable,
    dueDate: draft.dueDate?.slice(0, 10) ?? "", notes: draft.notes ?? "",
    lines: draft.lines.map(({ netAmount: _net, taxAmount: _tax, lineTotal: _total, ...line }) => line),
    concurrencyToken: draft.concurrencyToken,
  };
}

function calculateLine(line: GoodsReceiptLine) {
  const gross = line.quantity * line.unitCost;
  const net = Math.max(0, gross - line.discountAmount);
  const tax = net * line.taxRate / 100;
  return { net, tax, total: net + tax };
}

function calculateTotals(lines: GoodsReceiptLine[]) {
  return lines.reduce((result, line) => {
    const value = calculateLine(line);
    return { net: result.net + value.net, tax: result.tax + value.tax, total: result.total + value.total };
  }, { net: 0, tax: 0, total: 0 });
}

function localDateTime(value = new Date().toISOString()) {
  const date = new Date(value);
  const offset = date.getTimezoneOffset() * 60_000;
  return new Date(date.getTime() - offset).toISOString().slice(0, 16);
}

function plusDays(days: number) {
  const date = new Date(); date.setDate(date.getDate() + days);
  return date.toISOString().slice(0, 10);
}

function toIso(value: string) { return new Date(value).toISOString(); }
function toIsoOrNull(value: string) { return value ? new Date(value).toISOString() : null; }

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return <div className="space-y-2"><Label>{label}</Label>{children}</div>;
}
function Amount({ label, value, strong = false }: { label: string; value: number; strong?: boolean }) {
  return <div className={`flex justify-between ${strong ? "border-t border-white/20 pt-3 text-lg" : "text-sm"}`}>
    <dt className="text-slate-300">{label}</dt><dd className="font-semibold">{formatCurrency(value)}</dd>
  </div>;
}
function Summary({ icon: Icon, label, value }: {
  icon: typeof PackagePlus; label: string; value: string;
}) {
  return <Card><CardContent className="flex items-center gap-4 p-5">
    <div className="rounded-xl bg-primary/10 p-3 text-primary"><Icon className="h-5 w-5" /></div>
    <div><p className="text-xs uppercase tracking-wide text-muted-foreground">{label}</p>
      <p className="mt-1 font-semibold">{value}</p></div>
  </CardContent></Card>;
}
