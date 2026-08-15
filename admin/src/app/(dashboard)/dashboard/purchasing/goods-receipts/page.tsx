"use client";

import { useEffect, useMemo, useRef, useState, type KeyboardEvent } from "react";
import { useRouter } from "next/navigation";
import type { ColumnDef } from "@tanstack/react-table";
import {
  ArchiveRestore, ArrowRight, Barcode, CircleDollarSign, PackagePlus, Pencil, Plus, Save,
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
import { FormattedNumberInput } from "@/components/ui/formatted-number-input";
import { DatePicker } from "@/components/ui/date-picker";
import { DateTimePicker } from "@/components/ui/date-time-picker";
import { Label } from "@/components/ui/label";
import {
  Select, SelectContent, SelectItem, SelectTrigger, SelectValue,
} from "@/components/ui/select";
import { Textarea } from "@/components/ui/textarea";
import {
  useAssociateGoodsReceiptProduct, useConfirmGoodsReceipt, useDeleteGoodsReceiptDraft,
  useGoodsReceiptOptions, useGoodsReceiptProducts, useGoodsReceipts,
  useSaveGoodsReceiptDraft,
} from "@/hooks/use-goods-receipts";
import {
  goodsReceiptsApi, type GoodsReceiptDetail, type GoodsReceiptDraft, type GoodsReceiptLine,
  type GoodsReceiptListItem, type GoodsReceiptProduct, type GoodsReceiptStatus,
  type SaveGoodsReceiptDraftRequest,
} from "@/services/api/goods-receipts";
import { useAuthStore } from "@/stores/auth-store";
import { useBusinessContextStore } from "@/stores/business-context-store";
import { formatCurrency, formatDateTime } from "@/lib/utils";
import {
  calculateBaseQuantity, calculateGoodsReceiptLine, calculateGoodsReceiptTotals, nextGoodsReceiptQuantityIndex,
} from "@/lib/goods-receipt-calculator";

type ReceiptEditorStep = "details" | "products";

type PendingSupplierChange = { supplierId: string; supplierName: string };

type EditorDraft = {
  draftId: string; warehouseId: string; supplierId: string;
  supplierInvoiceNumber: string; supplierInvoiceDate: string; receivedAt: string;
  createsPayable: boolean; dueDate: string; notes: string;
  withholdingConceptCode: string; withholdingJurisdictionCode: string;
  lines: GoodsReceiptLine[]; concurrencyToken: string | null;
};

const statusLabels: Record<GoodsReceiptStatus, string> = {
  Draft: "Borrador", Accepted: "En proceso", Processed: "Procesada",
};
const purchaseTaxTreatmentLabels: Record<string, string> = {
  DeductibleInputVat: "IVA descontable",
  CapitalizedCost: "Mayor valor del costo",
  NotApplicable: "No aplica",
};


export default function GoodsReceiptsPage() {
  const businessId = useBusinessContextStore((state) => state.selectedBusinessId);
  const permissions = useAuthStore((state) => new Set(state.user?.permissions ?? []));
  const canCreate = permissions.has("purchasing.goods-receipts.create");
  const canConfirm = permissions.has("purchasing.goods-receipts.confirm");
  const canAssociateProducts = permissions.has("catalog.costs.manage");
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(25);
  const [search, setSearch] = useState("");
  const [status, setStatus] = useState<GoodsReceiptStatus | "all">("all");
  const [editor, setEditor] = useState<EditorDraft>();
  const [detail, setDetail] = useState<GoodsReceiptDetail>();
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
    try {
      if (item.status === "Draft") {
        setEditor(fromDraft(await goodsReceiptsApi.getDraft(item.documentId)));
      } else {
        setDetail(await goodsReceiptsApi.getDetail(item.documentId));
      }
    } catch {
      toast.error(item.status === "Draft"
        ? "No fue posible recuperar el borrador."
        : "No fue posible consultar el detalle de la entrada.");
    }
  };

  return <div className="space-y-6">
    <header className="flex flex-col justify-between gap-4 lg:flex-row lg:items-end">
      <div>
        <p className="text-sm font-medium text-primary">Compras e inventario</p>
        <h1 className="text-3xl font-semibold tracking-tight">Recepción de mercancía</h1>
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

    <ReceiptEditor key={editor?.draftId ?? "closed"} open={!!editor} draft={editor} businessId={businessId}
      canConfirm={canConfirm} canAssociateProducts={canAssociateProducts} onChange={setEditor}
      onClose={() => setEditor(undefined)} />
    <ReceiptDetailDialog detail={detail} onClose={() => setDetail(undefined)} />

  </div>;
}

function ReceiptDetailDialog({
  detail, onClose,
}: {
  detail?: GoodsReceiptDetail;
  onClose: () => void;
}) {
  if (!detail) return null;
  return <Dialog open onOpenChange={(value) => !value && onClose()}>
    <DialogContent className="flex max-h-[92dvh] max-w-5xl flex-col overflow-hidden p-0">
      <DialogHeader className="border-b px-6 py-5">
        <DialogTitle className="flex items-center gap-2">
          <Truck className="h-5 w-5 text-primary" /> {detail.documentNumber}
        </DialogTitle>
        <DialogDescription>
          Entrada de mercancía confirmada. Este documento es inmutable y conserva su trazabilidad completa.
        </DialogDescription>
      </DialogHeader>
      <div className="space-y-5 overflow-y-auto px-6 py-5">
        <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
          <DetailValue label="Proveedor" value={detail.supplierName} />
          <DetailValue label="Bodega" value={detail.warehouseName} />
          <DetailValue label="Estado" value={statusLabels[detail.status]} />
          <DetailValue label="Recibida" value={formatDateTime(detail.receivedAt)} />
          <DetailValue label="Factura del proveedor" value={detail.supplierInvoiceNumber ?? "Sin factura"} />
          <DetailValue label="Fecha de factura" value={detail.supplierInvoiceDate ? formatDateTime(detail.supplierInvoiceDate) : "Sin fecha"} />
          <DetailValue label="Forma de pago" value={detail.createsPayable ? "Crédito" : "Contado"} />
          <DetailValue label="Vencimiento" value={detail.dueDate ? formatDateTime(detail.dueDate) : "No aplica"} />
        </div>
        <div className="overflow-hidden rounded-2xl border">
          <div className="overflow-x-auto">
            <table className="w-full min-w-[850px] text-sm">
              <thead className="bg-muted/60 text-xs uppercase text-muted-foreground">
                <tr>
                  <th className="px-4 py-3 text-left">Producto</th>
                  <th className="px-4 py-3 text-left">Presentación</th>
                  <th className="px-4 py-3 text-right">Cantidad</th>
                  <th className="px-4 py-3 text-right">Costo unitario</th>
                  <th className="px-4 py-3 text-right">IVA</th>
                  <th className="px-4 py-3 text-right">Total</th>
                </tr>
              </thead>
              <tbody className="divide-y">
                {detail.lines.map((line) => <tr key={line.lineNumber}>
                  <td className="px-4 py-3">
                    <p>{line.description}</p>
                    <p className="text-xs text-muted-foreground">Línea {line.lineNumber}</p>
                  </td>
                  <td className="px-4 py-3">
                    <p>{line.presentationName}</p>
                    <p className="text-xs text-muted-foreground">
                      {line.presentationQuantity} × {line.unitsPerPresentation}
                    </p>
                  </td>
                  <td className="px-4 py-3 text-right tabular-nums">{line.quantity}</td>
                  <td className="px-4 py-3 text-right tabular-nums">{formatCurrency(line.unitCost)}</td>
                  <td className="px-4 py-3 text-right">
                    <p className="tabular-nums">{line.taxRate}%</p>
                    <p className="text-xs text-muted-foreground">
                      {purchaseTaxTreatmentLabels[line.taxTreatment] ?? line.taxTreatment}
                    </p>
                  </td>
                  <td className="px-4 py-3 text-right font-medium tabular-nums">{formatCurrency(line.lineTotal)}</td>
                </tr>)}
              </tbody>
            </table>
          </div>
        </div>
        <div className="ml-auto grid w-full max-w-sm gap-2 rounded-2xl bg-slate-950 p-5 text-white">
          <Amount label="Subtotal" value={detail.netAmount} />
          <Amount label="IVA" value={detail.taxAmount} />
          <Amount label="Total" value={detail.grandTotal} strong />
        </div>
        {detail.notes && <div className="rounded-2xl border bg-muted/30 p-4">
          <p className="text-xs font-medium uppercase text-muted-foreground">Notas</p>
          <p className="mt-1 text-sm">{detail.notes}</p>
        </div>}
      </div>
      <DialogFooter className="border-t px-6 py-4">
        <Button type="button" onClick={onClose}>Cerrar</Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>;
}

function DetailValue({ label, value }: { label: string; value: string }) {
  return <div className="rounded-2xl border bg-muted/20 p-4">
    <p className="text-xs font-medium uppercase text-muted-foreground">{label}</p>
    <p className="mt-1 text-sm">{value}</p>
  </div>;
}

function ReceiptEditor({
  open, draft, businessId, canConfirm, canAssociateProducts, onChange, onClose,
}: {
  open: boolean; draft?: EditorDraft; businessId: string | null; canConfirm: boolean;
  canAssociateProducts: boolean;
  onChange: (draft: EditorDraft) => void; onClose: () => void;
}) {
  const options = useGoodsReceiptOptions();
  const [productSearch, setProductSearch] = useState("");
  const [includeUnassociated, setIncludeUnassociated] = useState(false);
  const [productMenuOpen, setProductMenuOpen] = useState(false);
  const [pendingAssociation, setPendingAssociation] = useState<GoodsReceiptProduct>();
  const [supplierProductCode, setSupplierProductCode] = useState("");
  const [purchasePresentationName, setPurchasePresentationName] = useState("Unidad");
  const [unitsPerPresentation, setUnitsPerPresentation] = useState(1);
  const [step, setStep] = useState<ReceiptEditorStep>(() => draft?.lines.length ? "products" : "details");
  const [pendingSupplierChange, setPendingSupplierChange] = useState<PendingSupplierChange>();
  const products = useGoodsReceiptProducts(
    draft?.supplierId || undefined, productSearch, includeUnassociated,
  );
  const associateProduct = useAssociateGoodsReceiptProduct();
  const save = useSaveGoodsReceiptDraft();
  const remove = useDeleteGoodsReceiptDraft();
  const confirm = useConfirmGoodsReceipt();
  const router = useRouter();
  const scanRef = useRef<HTMLInputElement>(null);
  const productListRef = useRef<HTMLDivElement>(null);
  const productPickerRef = useRef<HTMLDivElement>(null);
  const [activeProductIndex, setActiveProductIndex] = useState(0);
  const quantityRefs = useRef(new Map<string, HTMLInputElement>());
  const productItems = useMemo(
    () => products.data?.pages.flatMap((page) => page.items) ?? [], [products.data],
  );
  useEffect(() => {
    if (!productMenuOpen) return;
    const closeOnOutsidePointer = (event: PointerEvent) => {
      if (!productPickerRef.current?.contains(event.target as Node)) setProductMenuOpen(false);
    };
    document.addEventListener("pointerdown", closeOnOutsidePointer, true);
    return () => document.removeEventListener("pointerdown", closeOnOutsidePointer, true);
  }, [productMenuOpen]);

  useEffect(() => setActiveProductIndex(0), [productSearch, includeUnassociated, draft?.supplierId]);

  if (!draft || !businessId) return null;
  const totals = calculateGoodsReceiptTotals(draft.lines);

  const change = (values: Partial<EditorDraft>) => onChange({ ...draft, ...values });
  const supplierName = options.data?.suppliers.find((item) => item.supplierId === draft.supplierId)?.name ?? "Sin proveedor";
  const warehouseName = options.data?.warehouses.find((item) => item.warehouseId === draft.warehouseId)?.name ?? "Sin bodega";

  const applySupplierChange = (supplierId: string) => {
    change({ supplierId, lines: [] });
    setPendingSupplierChange(undefined);
    setIncludeUnassociated(false);
    setProductSearch("");
  };

  const requestSupplierChange = (supplierId: string) => {
    if (supplierId === draft.supplierId) return;
    if (draft.lines.length === 0) {
      applySupplierChange(supplierId);
      return;
    }
    const supplierName = options.data?.suppliers.find((item) => item.supplierId === supplierId)?.name ?? "el nuevo proveedor";
    setPendingSupplierChange({ supplierId, supplierName });
  };
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
      if (notify) {
        toast.success("Borrador guardado y disponible para recuperar.");
        onClose();
      }
      return saved;
    } catch {
      toast.error("No fue posible guardar. El borrador pudo cambiar en otra sesión.");
      return null;
    }
  };

  const continueToProducts = async () => {
    if (!draft.supplierId || !draft.warehouseId) {
      toast.error("Selecciona el proveedor y la bodega antes de continuar.");
      return;
    }
    const saved = await persist(false);
    if (!saved) return;
    setStep("products");
    requestAnimationFrame(() => scanRef.current?.focus());
  };

  const addProduct = (product: GoodsReceiptProduct) => {
    const existing = draft.lines.find((line) => line.productId === product.productId);
    const lineIndex = existing ? draft.lines.findIndex((line) => line.productId === product.productId) : draft.lines.length;
    const lines = existing
      ? draft.lines.map((line) => line.productId === product.productId
        ? {
          ...line,
          presentationQuantity: line.presentationQuantity + 1,
          quantity: line.quantity + line.unitsPerPresentation,
        } : line)
      : [...draft.lines, {
        lineNumber: draft.lines.length + 1, productId: product.productId,
        description: product.name, quantity: product.unitsPerPresentation,
        unitCost: product.latestUnitCost ?? 0,
        discountAmount: 0, taxCode: product.taxCode, taxRate: product.taxRate,
        taxTreatment: product.taxTreatment,
        presentationName: product.purchasePresentationName,
        presentationQuantity: 1, unitsPerPresentation: product.unitsPerPresentation,
        baseUnitCode: product.baseUnitCode,
        preferredPresentationName: product.purchasePresentationName,
        preferredUnitsPerPresentation: product.unitsPerPresentation,
      } satisfies GoodsReceiptLine];
    change({ lines });
    setProductSearch("");
    requestAnimationFrame(() => requestAnimationFrame(() => {
      const quantity = quantityRefs.current.get(lines[lineIndex].productId);
      quantity?.focus();
      quantity?.select();
      quantity?.scrollIntoView({ block: "nearest" });
    }));
  };

  const selectProduct = (product: GoodsReceiptProduct) => {
    setProductMenuOpen(false);
    if (product.isAssociated) {
      addProduct(product);
      return;
    }
    if (!canAssociateProducts) {
      toast.error("Necesitas permiso para asociar productos a este proveedor.");
      return;
    }
    setPurchasePresentationName(product.purchasePresentationName || "Unidad");
    setUnitsPerPresentation(product.unitsPerPresentation || 1);
    setSupplierProductCode(product.productCode);
    setPendingAssociation(product);
  };

  const confirmAssociation = async () => {
    if (!pendingAssociation || !draft.supplierId) return;
    try {
      const associated = await associateProduct.mutateAsync({
        supplierId: draft.supplierId,
        productId: pendingAssociation.productId,
        supplierProductCode: supplierProductCode.trim() || null,
        isPrimary: false,
        purchasePresentationName,
        unitsPerPresentation,
      });
      setPendingAssociation(undefined);
      setSupplierProductCode("");
      toast.success(`${associated.name} quedó asociado al proveedor.`);
      addProduct(associated);
    } catch {
      toast.error("No fue posible asociar el producto al proveedor.");
    }
  };

  const focusQuantity = (index: number) => {
    if (draft.lines.length === 0) return;
    const bounded = nextGoodsReceiptQuantityIndex(index, 0, draft.lines.length);
    quantityRefs.current.get(draft.lines[bounded].productId)?.focus();
  };

  const moveQuantityFocus = (productId: string, offset: number) => {
    const index = draft.lines.findIndex((line) => line.productId === productId);
    if (index >= 0) focusQuantity(index + offset);
  };

  const capture = (event: KeyboardEvent<HTMLInputElement>) => {
    if (event.key === "Escape") {
      event.preventDefault();
      event.stopPropagation();
      setProductMenuOpen(false);
      return;
    }
    if (event.key === "ArrowDown" && productItems.length > 0) {
      event.preventDefault();
      setProductMenuOpen(true);
      setActiveProductIndex((current) => Math.min(current + 1, productItems.length - 1));
      return;
    }
    if (event.key === "ArrowUp" && productItems.length > 0) {
      event.preventDefault();
      setProductMenuOpen(true);
      setActiveProductIndex((current) => Math.max(current - 1, 0));
      return;
    }
    if (event.key !== "Enter") return;
    event.preventDefault();
    const selectedProduct = productItems[activeProductIndex] ?? productItems[0];
    if (selectedProduct) selectProduct(selectedProduct);
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
        withholdingConceptCode: draft.withholdingConceptCode.trim() || null,
        withholdingJurisdictionCode: draft.withholdingJurisdictionCode.trim() || null,
      });
      toast.success(`${accepted.documentNumber} fue confirmada y enviada al motor.`, {
        duration: 12000,
        action: {
          label: "Revisar precios",
          onClick: () => router.push(`/dashboard/products/pricing?sourceDocumentId=${accepted.documentId}`),
        },
      });
      onClose();
    } catch (error) {
      const message = error && typeof error === "object" && "message" in error
        ? String(error.message)
        : "No fue posible confirmar. Revisa factura duplicada, permisos y datos.";
      toast.error(message);
    }
  };

  return <Dialog open={open} onOpenChange={(value) => !value && onClose()}>
    <DialogContent onEscapeKeyDown={(event) => { if (productMenuOpen) { event.preventDefault(); setProductMenuOpen(false); } }} className="flex max-h-[96dvh] max-w-[96vw] flex-col overflow-hidden p-0">
      <DialogHeader className="border-b px-6 py-5">
        <DialogTitle className="flex items-center gap-2">
          <Truck className="h-5 w-5 text-primary" /> Entrada de mercancía
        </DialogTitle>
        <DialogDescription>
          Borrador recuperable. El consecutivo se asigna únicamente al confirmar.
        </DialogDescription>
      </DialogHeader>

      <div className="flex-1 space-y-5 overflow-y-auto px-6 py-5">
        {step === "details" ? <>
        <section className="grid gap-4 rounded-2xl border bg-muted/20 p-4 md:grid-cols-4">
          <Field label="Proveedor">
            <Select value={draft.supplierId} onValueChange={requestSupplierChange}>
              <SelectTrigger><SelectValue placeholder="Seleccionar proveedor" /></SelectTrigger>
              <SelectContent>{options.data?.suppliers.map((item) =>
                <SelectItem key={item.supplierId} value={item.supplierId}>
                  {item.name} · {item.identification}
                </SelectItem>)}</SelectContent>
            </Select>
          </Field>
          <Field label="Bodega">
            <Select value={draft.warehouseId} onValueChange={(value) => change({ warehouseId: value })}>
              <SelectTrigger><SelectValue placeholder="Seleccionar bodega" /></SelectTrigger>
              <SelectContent>{options.data?.warehouses.map((item) =>
                <SelectItem key={item.warehouseId} value={item.warehouseId}>
                  {item.name} · {item.code}
                </SelectItem>)}</SelectContent>
            </Select>
          </Field>
          <Field label="Factura del proveedor">
            <Input value={draft.supplierInvoiceNumber}
              onChange={(event) => change({ supplierInvoiceNumber: event.target.value })}
              placeholder="Opcional" maxLength={80} />
          </Field>
          <Field label="Fecha factura">
            <DatePicker value={draft.supplierInvoiceDate} onChange={(value) => change({ supplierInvoiceDate: value })} />
          </Field>
          <Field label="Fecha de recepción">
            <DateTimePicker value={draft.receivedAt} onChange={(value) => change({ receivedAt: value })} />
          </Field>
          <Field label="Condición">
            <Select value={draft.createsPayable ? "Credit" : "Cash"}
              onValueChange={(value) => change({
                createsPayable: value === "Credit",
                dueDate: value === "Credit" ? draft.dueDate || plusDays(30) : "",
              })}>
              <SelectTrigger><SelectValue /></SelectTrigger>
              <SelectContent>
                <SelectItem value="Credit">Crédito · genera cuenta por pagar</SelectItem>
                <SelectItem value="Cash">Contado</SelectItem>
              </SelectContent>
            </Select>
          </Field>
          <Field label="Vencimiento">
            <DatePicker disabled={!draft.createsPayable} value={draft.dueDate} onChange={(value) => change({ dueDate: value })} />
          </Field>
          <Field label="Notas">
            <Input value={draft.notes} onChange={(event) => change({ notes: event.target.value })}
              placeholder="Recepción, estado o referencia" maxLength={1000} />
          </Field>
        </section>
        </> : <>
        <section className="rounded-2xl border bg-muted/20 p-4" data-testid="goods-receipt-readonly-details">
          <div className="flex flex-wrap items-start justify-between gap-4">
            <div>
              <p className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">Datos de la recepción</p>
              <div className="mt-2 flex flex-wrap gap-x-6 gap-y-2 text-sm">
                <span><strong>Proveedor:</strong> {supplierName}</span>
                <span><strong>Bodega:</strong> {warehouseName}</span>
                <span><strong>Factura:</strong> {draft.supplierInvoiceNumber || "Sin número"}</span>
                <span><strong>Condición:</strong> {draft.createsPayable ? "Crédito" : "Contado"}</span>
              </div>
            </div>
            <Button type="button" variant="outline" size="sm" onClick={() => setStep("details")}>
              <Pencil className="mr-2 h-4 w-4" /> Editar datos
            </Button>
          </div>
        </section>

        <section className="rounded-2xl border">
          <div ref={productPickerRef} onBlur={(event) => { if (!event.currentTarget.contains(event.relatedTarget as Node | null)) setProductMenuOpen(false); }}>
          <div className="grid gap-3 border-b p-4 lg:grid-cols-[minmax(0,1fr)_auto_auto]">
            <div className="relative">
              <Barcode className="pointer-events-none absolute left-3 top-2.5 h-4 w-4 text-primary" />
              <Input ref={scanRef} className="pl-9" value={productSearch}
                disabled={!draft.supplierId}
                onFocus={() => setProductMenuOpen(true)}
                onClick={() => setProductMenuOpen(true)}
                onChange={(event) => { setProductSearch(event.target.value); setProductMenuOpen(true); }}
                onKeyDown={capture}
                placeholder={draft.supplierId
                  ? "Escanea o busca por código, referencia, nombre o código del proveedor"
                  : "Selecciona primero el proveedor"} />
            </div>
            <Button type="button" variant={includeUnassociated ? "secondary" : "outline"}
              disabled={!draft.supplierId}
              onClick={() => { setIncludeUnassociated((current) => !current); setProductMenuOpen(true); }}>
              <Search className="mr-2 h-4 w-4" />
              {includeUnassociated ? "Ver productos del proveedor" : "Buscar en todo el catálogo"}
            </Button>
            <Button type="button" variant="outline"
              disabled={!productItems[activeProductIndex]}
              onClick={() => productItems[activeProductIndex] && selectProduct(productItems[activeProductIndex])}>
              <Plus className="mr-2 h-4 w-4" /> Agregar
            </Button>
          </div>
          {productMenuOpen && productItems.length > 0 &&
            <div ref={productListRef} role="listbox" className="max-h-56 overflow-y-auto border-b bg-background p-2 [&_strong]:font-normal"
              onScroll={(event) => {
                const target = event.currentTarget;
                if (target.scrollHeight - target.scrollTop - target.clientHeight < 80 && products.hasNextPage && !products.isFetchingNextPage) {
                  void products.fetchNextPage();
                }
              }}>
              {productItems.map((product, index) =>
                <button key={product.productId} type="button" role="option" aria-selected={index === activeProductIndex}
                  className={`flex w-full items-center justify-between rounded-lg px-3 py-2 text-left ${index === activeProductIndex ? "bg-emerald-50" : "hover:bg-muted"}`}
                  onMouseEnter={() => setActiveProductIndex(index)}
                  onClick={() => selectProduct(product)}
                  onMouseDown={(event) => event.preventDefault()}>
                  <span>
                    <span className="flex items-center gap-2">
                      <strong>{product.name}</strong>
                      {!product.isAssociated && <Badge variant="outline">Nuevo para este proveedor</Badge>}
                    </span>
                    <span className="text-xs text-muted-foreground">
                      {product.productCode}
                      {product.supplierProductCode ? ` · Prov. ${product.supplierProductCode}` : ""}
                    </span>
                  </span>
                  <span className="text-sm font-medium">
                    {product.latestUnitCost == null ? "Sin costo recibido" : formatCurrency(product.latestUnitCost)}
                  </span>
                </button>)}
              {products.hasNextPage && <Button type="button" variant="ghost" className="mt-2 w-full"
                disabled={products.isFetchingNextPage} onClick={() => products.fetchNextPage()}>
                {products.isFetchingNextPage ? "Cargando..." : "Cargar 50 más"}
              </Button>}
            </div>}
          {productMenuOpen && draft.supplierId && !products.isLoading && productItems.length === 0 && <div role="listbox" className="border-b px-4 py-3 text-sm text-muted-foreground">Sin datos</div>}
          </div>
          <div className="overflow-x-auto">
            <table className="w-full min-w-[900px] text-sm">
              <thead className="bg-muted/50 text-left text-xs font-semibold uppercase tracking-wide text-muted-foreground">
                <tr><th className="px-4 py-3">Producto</th><th className="w-44 px-3 py-3">Cantidad recibida</th>
                  <th className="w-40 px-3 py-3">Costo unitario</th><th className="w-36 px-3 py-3">Descuento</th>
                  <th className="w-28 px-3 py-3">IVA</th><th className="w-36 px-3 py-3 text-right">Total</th>
                  <th className="w-14" /></tr>
              </thead>
              <tbody>{draft.lines.map((line) => {
                const lineTotal = calculateGoodsReceiptLine(line).total;
                return <tr key={line.productId} className="border-t align-middle">
                  <td className="px-4 py-3"><p className="font-semibold">{line.description}</p>
                    <p className="text-xs text-muted-foreground">IVA de compra {line.taxRate} % · {purchaseTaxTreatmentLabels[line.taxTreatment] ?? line.taxTreatment}</p>
                    <p className="text-xs text-muted-foreground">{line.presentationQuantity} {line.presentationName.toLowerCase()} × {line.unitsPerPresentation} = {line.quantity} {line.baseUnitCode ?? "unidades"}</p></td>
                  <td className="px-3 py-2"><Input type="number" min="0.000001" step="0.001"
                    ref={(element) => {
                      if (element) quantityRefs.current.set(line.productId, element);
                      else quantityRefs.current.delete(line.productId);
                    }}
                    value={line.presentationQuantity}
                    aria-label={`Cantidad en ${line.presentationName}`}
                    onChange={(event) => {
                      const presentationQuantity = Number(event.target.value);
                      updateLine(line.productId, { presentationQuantity, quantity: calculateBaseQuantity(presentationQuantity, line.unitsPerPresentation) });
                    }}
                    onKeyDown={(event) => {
                      if (event.key === "ArrowDown") {
                        event.preventDefault(); moveQuantityFocus(line.productId, 1);
                      } else if (event.key === "ArrowUp") {
                        event.preventDefault(); moveQuantityFocus(line.productId, -1);
                      } else if (event.key === "Enter") {
                        event.preventDefault(); scanRef.current?.focus(); scanRef.current?.select();
                      }
                    }} />
                    {(line.preferredUnitsPerPresentation ?? line.unitsPerPresentation) > 1 && <Select
                      value={line.unitsPerPresentation === 1 ? "base" : "package"}
                      onValueChange={(value) => {
                        const factor = value === "base" ? 1 : (line.preferredUnitsPerPresentation ?? line.unitsPerPresentation);
                        const name = value === "base" ? (line.baseUnitCode ?? "Unidad") : (line.preferredPresentationName ?? line.presentationName);
                        updateLine(line.productId, { presentationName: name, unitsPerPresentation: factor, quantity: calculateBaseQuantity(line.presentationQuantity, factor) });
                      }}>
                      <SelectTrigger className="mt-1 h-7 border-0 px-1 text-xs"><SelectValue /></SelectTrigger>
                      <SelectContent>
                        <SelectItem value="base">{line.baseUnitCode ?? "Unidad"}</SelectItem>
                        <SelectItem value="package">{line.preferredPresentationName ?? line.presentationName} · {line.preferredUnitsPerPresentation ?? line.unitsPerPresentation} {line.baseUnitCode ?? "unidades"}</SelectItem>
                      </SelectContent>
                    </Select>}
                    {(line.preferredUnitsPerPresentation ?? line.unitsPerPresentation) <= 1 && <span className="mt-1 block text-xs text-muted-foreground">{line.baseUnitCode ?? line.presentationName}</span>}
                  </td>
                  <td className="px-3 py-2"><FormattedNumberInput kind="currency"
                    ariaLabel={`Costo unitario de ${line.description}`}
                    value={line.unitCost} onValueChange={(value) =>
                      updateLine(line.productId, { unitCost: value ?? 0 })} /></td>
                  <td className="px-3 py-2"><FormattedNumberInput kind="currency"
                    ariaLabel={`Descuento de ${line.description}`}
                    value={line.discountAmount} onValueChange={(value) =>
                      updateLine(line.productId, { discountAmount: value ?? 0 })} /></td>
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
            placeholder="Observaciones de recepción" maxLength={1000} />
          <dl className="space-y-2 rounded-2xl bg-slate-950 p-5 text-white">
            <Amount label="Subtotal" value={totals.net} />
            <Amount label="Impuestos" value={totals.tax} />
            <Amount label="Total recibido" value={totals.total} strong />
          </dl>
        </section>

        <section className="grid gap-3 rounded-2xl border p-4 md:grid-cols-2">
              <Field label="Concepto de retención">
            <Input value={draft.withholdingConceptCode} onChange={(event) => change({ withholdingConceptCode: event.target.value })} placeholder="Ej. MERCANCIA (opcional)" />
          </Field>
          <Field label="Municipio para reteICA">
            <Input value={draft.withholdingJurisdictionCode} onChange={(event) => change({ withholdingJurisdictionCode: event.target.value })} placeholder="Ej. 11001 (opcional)" />
          </Field>
              <p className="text-xs text-muted-foreground md:col-span-2">Al confirmar, el motor tributario aplica las reglas vigentes del proveedor y congela el cálculo en el documento.</p>
        </section>
        </>}
      </div>

      <Dialog open={!!pendingSupplierChange} onOpenChange={(value) => !value && setPendingSupplierChange(undefined)}>
        <DialogContent className="sm:max-w-md">
          <DialogHeader>
            <DialogTitle>Cambiar proveedor</DialogTitle>
            <DialogDescription>
              Esta recepción ya tiene {draft.lines.length} {draft.lines.length === 1 ? "producto agregado" : "productos agregados"}.
              Al cambiar a {pendingSupplierChange?.supplierName}, limpiaremos esas líneas para evitar mezclar productos, costos o códigos de proveedores distintos.
            </DialogDescription>
          </DialogHeader>
          <p className="text-sm text-muted-foreground">Los demás datos de la recepción se conservarán.</p>
          <DialogFooter>
            <Button type="button" variant="outline" onClick={() => setPendingSupplierChange(undefined)}>Conservar proveedor actual</Button>
            <Button type="button" variant="destructive" onClick={() => pendingSupplierChange && applySupplierChange(pendingSupplierChange.supplierId)}>
              Cambiar y limpiar productos
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      <Dialog open={!!pendingAssociation} onOpenChange={(value) => {
        if (!value) { setPendingAssociation(undefined); setSupplierProductCode(""); setPurchasePresentationName("Unidad"); setUnitsPerPresentation(1); }
      }}>
        <DialogContent className="sm:max-w-lg">
          <DialogHeader>
            <DialogTitle>Asociar producto al proveedor</DialogTitle>
            <DialogDescription>
              {pendingAssociation?.name} no pertenece todavía al catálogo de este proveedor.
              Confirma la relación para poder recibirlo ahora y encontrarlo directamente en futuras entradas.
            </DialogDescription>
          </DialogHeader>
          <Field label="Código utilizado por el proveedor">
            <Input value={supplierProductCode}
              onChange={(event) => setSupplierProductCode(event.target.value)}
              placeholder="Opcional" maxLength={80} autoFocus />
          </Field>
          <div className="grid gap-4 sm:grid-cols-2">
            <Field label="Presentación de compra">
              <Input value={purchasePresentationName}
                onChange={(event) => setPurchasePresentationName(event.target.value)}
                placeholder="Caja, paquete, unidad..." maxLength={80} />
            </Field>
            <Field label="Unidades por presentación">
              <Input type="number" min="0.000001" step="0.001" value={unitsPerPresentation}
                onChange={(event) => setUnitsPerPresentation(Number(event.target.value))} />
            </Field>
          </div>
          <p className="text-sm text-muted-foreground">Ejemplo: 3 cajas de 24 se registran como 72 unidades de inventario.</p>
          <DialogFooter>
            <Button type="button" variant="outline"
              onClick={() => { setPendingAssociation(undefined); setSupplierProductCode(""); }}>
              Cancelar
            </Button>
            <Button type="button" disabled={associateProduct.isPending} onClick={confirmAssociation}>
              Asociar y agregar
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
      <DialogFooter className="border-t bg-background px-6 py-4 sm:justify-between">
        <Button type="button" variant="ghost" className="text-destructive" onClick={deleteDraft}>
          <Trash2 className="mr-2 h-4 w-4" /> Eliminar borrador
        </Button>
        <div className="flex flex-wrap justify-end gap-2">
          <Button type="button" variant="outline" onClick={onClose}>Cerrar</Button>
          <Button type="button" variant="secondary" disabled={save.isPending} onClick={() => persist()}>
            <Save className="mr-2 h-4 w-4" /> Guardar borrador
          </Button>
          {step === "details" ?
            <Button type="button" disabled={save.isPending} onClick={continueToProducts} data-testid="goods-receipt-continue">
              Continuar a productos <ArrowRight className="ml-2 h-4 w-4" />
            </Button> :
            canConfirm && <Button type="button" disabled={confirm.isPending} onClick={confirmEntry}>
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
    withholdingConceptCode: "", withholdingJurisdictionCode: "",
  };
}

function fromDraft(draft: GoodsReceiptDraft): EditorDraft {
  return {
    draftId: draft.draftId, warehouseId: draft.warehouseId ?? "",
    supplierId: draft.supplierId ?? "", supplierInvoiceNumber: draft.supplierInvoiceNumber ?? "",
    supplierInvoiceDate: draft.supplierInvoiceDate?.slice(0, 10) ?? "",
    receivedAt: localDateTime(draft.receivedAt), createsPayable: draft.createsPayable,
    dueDate: draft.dueDate?.slice(0, 10) ?? "", notes: draft.notes ?? "",
    lines: draft.lines.map((line) => ({
      lineNumber: line.lineNumber, productId: line.productId, description: line.description,
      quantity: line.quantity, unitCost: line.unitCost, discountAmount: line.discountAmount,
      taxCode: line.taxCode, taxRate: line.taxRate, taxTreatment: line.taxTreatment,
      presentationName: line.presentationName, baseUnitCode: line.baseUnitCode,
      preferredPresentationName: line.preferredPresentationName,
      preferredUnitsPerPresentation: line.preferredUnitsPerPresentation,
      presentationQuantity: line.presentationQuantity, unitsPerPresentation: line.unitsPerPresentation,
    })),
    withholdingConceptCode: "", withholdingJurisdictionCode: "",
    concurrencyToken: draft.concurrencyToken,
  };
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
