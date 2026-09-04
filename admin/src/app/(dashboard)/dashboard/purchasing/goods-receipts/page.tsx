"use client";

import { useEffect, useMemo, useRef, useState, type KeyboardEvent } from "react";
import { useRouter } from "next/navigation";
import type { ColumnDef } from "@tanstack/react-table";
import { useQueries, useQuery } from "@tanstack/react-query";
import {
  ArchiveRestore, Barcode, ChevronDown, CircleAlert, CircleDollarSign, PackagePlus, Plus, Save,
  Search, Trash2, Truck, Warehouse, X,
} from "lucide-react";
import { toast } from "sonner";
import { DataTable } from "@/components/tables/data-table";
import { PartyRoleSelect } from "@/components/parties/party-role-select";
import { SupplierChangeConfirmationDialog } from "@/components/purchasing/supplier-change-confirmation-dialog";
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
import { Label } from "@/components/ui/label";
import {
  Select, SelectContent, SelectItem, SelectTrigger, SelectValue,
} from "@/components/ui/select";
import { Textarea } from "@/components/ui/textarea";
import { Tooltip, TooltipContent, TooltipProvider, TooltipTrigger } from "@/components/ui/tooltip";
import {
  useAssociateGoodsReceiptProduct, useConfirmGoodsReceipt, useDeleteGoodsReceiptDraft,
  useGoodsReceiptOptions, useGoodsReceiptProducts, useGoodsReceipts,
  useSaveGoodsReceiptDraft,
} from "@/hooks/use-goods-receipts";
import {
  goodsReceiptsApi, type GoodsReceiptDetail, type GoodsReceiptDraft, type GoodsReceiptLine,
  type GoodsReceiptListItem, type GoodsReceiptProduct, type GoodsReceiptStatus,
  type SaveGoodsReceiptDraftRequest, type PurchaseEvidenceType, type GoodsReceiptCostDocument,
  type PurchaseCostAllocationMethod, type PurchaseCostKind,
} from "@/services/api/goods-receipts";
import { useAuthStore } from "@/stores/auth-store";
import { useBusinessContextStore } from "@/stores/business-context-store";
import { formatCurrency, formatDateTime } from "@/lib/utils";
import { partiesApi, type PartyWorkspaceItem } from "@/services/api/parties";
import { tenantCommercialApi } from "@/services/api/tenants";
import { purchaseOrdersApi } from "@/services/api/purchase-orders";
import {
  calculateBaseQuantity, calculateGoodsReceiptLine, calculateGoodsReceiptTotals, goodsReceiptUnitLabel,
  nextGoodsReceiptQuantityIndex,
} from "@/lib/goods-receipt-calculator";
import {
  goodsReceiptDraftKey, loadGoodsReceiptDraft, removeGoodsReceiptDraft, saveGoodsReceiptDraft,
} from "@/lib/goods-receipt-draft-store";

type PendingSupplierChange = { supplier: PartyWorkspaceItem };
type GoodsReceiptCostLine = GoodsReceiptCostDocument["lines"][number];

type EditorDraft = {
  draftId: string; warehouseId: string; supplierId: string;
  supplierInvoiceNumber: string; supplierInvoiceDate: string; receivedAt: string;
  purchaseEvidenceType: PurchaseEvidenceType | "";
  createsPayable: boolean; dueDate: string; notes: string;
  withholdingConceptCode: string; withholdingJurisdictionCode: string;
  lines: GoodsReceiptLine[]; concurrencyToken: string | null;
  purchaseOrderId: string;
  currencyCode: string; exchangeRate: number; exchangeRateDate: string; exchangeRateSource: string;
  additionalCostDocuments: GoodsReceiptCostDocument[];
  pendingCostDocument?: GoodsReceiptCostDocument | null;
};

const statusLabels: Record<GoodsReceiptStatus, string> = {
  Draft: "Borrador", Accepted: "En proceso", Processed: "Procesada",
};
const purchaseTaxTreatmentLabels: Record<string, string> = {
  DeductibleInputVat: "IVA descontable",
  CapitalizedCost: "Mayor valor del costo",
  NotApplicable: "No aplica",
};
const purchaseEvidenceLabels: Record<string, string> = {
  SupplierElectronicInvoice: "Factura electrónica del proveedor",
  BuyerElectronicSupportDocument: "Documento soporte electrónico",
  InternalReceiptVoucher: "Comprobante interno",
  ForeignCommercialInvoice: "Factura comercial del exterior",
  ImportDeclaration: "Declaración de importación",
};
export default function GoodsReceiptsPage() {
  const businessId = useBusinessContextStore((state) => state.selectedBusinessId);
  const userId = useAuthStore((state) => state.user?.userId);
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
  const localDraftKey = businessId && userId ? goodsReceiptDraftKey(userId, businessId) : null;
  const legacyLocalDraftKey = businessId ? `auraly.goods-receipt.entry.${businessId}` : null;
  const localWriteQueue = useRef(Promise.resolve());
  const localStorageFailureShown = useRef(false);

  const rememberLocalDraft = (next: EditorDraft) => {
    setEditor(next);
  };
  const clearLocalDraft = () => {
    if (localDraftKey) localWriteQueue.current = localWriteQueue.current
      .then(() => removeGoodsReceiptDraft(localDraftKey))
      .catch(() => { toast.error("No fue posible limpiar la recuperación local de esta recepción."); });
    setEditor(undefined);
  };

  useEffect(() => {
    if (!editor || !localDraftKey) return;
    localWriteQueue.current = localWriteQueue.current
      .then(() => saveGoodsReceiptDraft(localDraftKey, editor))
      .catch(() => {
        if (!localStorageFailureShown.current) {
          localStorageFailureShown.current = true;
          toast.error("No fue posible guardar la recuperación automática en este dispositivo.");
        }
      });
  }, [editor, localDraftKey]);

  const columns = useMemo<ColumnDef<GoodsReceiptListItem>[]>(() => [
    {
      accessorKey: "documentNumber", header: "Documento",
      cell: ({ row }) => <div>
        <p className="font-semibold">{row.original.documentNumber ?? "Borrador sin numerar"}</p>
        <p className="text-xs text-muted-foreground">
          {row.original.supplierInvoiceNumber
            ? `Factura proveedor ${row.original.supplierInvoiceNumber}`
            : purchaseEvidenceLabels[row.original.purchaseEvidenceType ?? ""] ?? "Tipo por seleccionar"}
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

  const newEntry = async () => {
    if (!businessId || !localDraftKey) return;
    try {
      let stored = await loadGoodsReceiptDraft<Partial<EditorDraft>>(localDraftKey);
      if (!stored && legacyLocalDraftKey) {
        const legacy = localStorage.getItem(legacyLocalDraftKey);
        if (legacy) {
          stored = JSON.parse(legacy) as Partial<EditorDraft>;
          await saveGoodsReceiptDraft(localDraftKey, stored);
          localStorage.removeItem(legacyLocalDraftKey);
        }
      }
      if (stored) {
        setEditor({ ...emptyDraft(), ...stored,
          additionalCostDocuments: stored.additionalCostDocuments ?? [] });
        return;
      }
    } catch {
      toast.error("No fue posible recuperar la captura guardada en este dispositivo.");
    }
    rememberLocalDraft(emptyDraft());
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
        <h1 className="text-3xl font-semibold tracking-tight">Recepción de compra</h1>
        <p className="mt-1 max-w-3xl text-muted-foreground">
          Recibe productos por proveedor. Al confirmar, el motor actualiza inventario,
          costo promedio, cuenta por pagar y propuesta de precio en el orden del negocio.
        </p>
      </div>
      {canCreate && <Button onClick={() => void newEntry()}><Plus className="mr-2 h-4 w-4" /> Nueva entrada</Button>}
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
      canConfirm={canConfirm} canAssociateProducts={canAssociateProducts} onChange={rememberLocalDraft}
      onClose={() => setEditor(undefined)} onClear={clearLocalDraft} />
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
          Recepción de compra confirmada. Este documento es inmutable y conserva su trazabilidad completa.
        </DialogDescription>
      </DialogHeader>
      <div className="space-y-5 overflow-y-auto px-6 py-5">
        <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
          <DetailValue label="Proveedor" value={detail.supplierName} />
          <DetailValue label="Bodega" value={detail.warehouseName} />
          <DetailValue label="Estado" value={statusLabels[detail.status]} />
          <DetailValue label="Recibida" value={formatDateTime(detail.receivedAt)} />
          <DetailValue label="Tipo de soporte" value={purchaseEvidenceLabels[detail.purchaseEvidenceType]} />
          <DetailValue label="Factura del proveedor" value={detail.supplierInvoiceNumber ?? "No aplica"} />
          <DetailValue label="Fecha de emisión" value={detail.supplierInvoiceDate ? formatDateTime(detail.supplierInvoiceDate) : "Sin fecha"} />
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
                  <th className="px-4 py-3 text-right">Costo puesto</th>
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
                  <td className="px-4 py-3 text-right tabular-nums">
                    {formatCurrency((line.recognizedInventoryCostAmount ?? line.netAmount) / line.quantity)}
                    {(line.allocatedLandedCostAmount ?? 0) > 0 &&
                      <p className="text-xs text-muted-foreground">Incluye +{formatCurrency((line.allocatedLandedCostAmount ?? 0) / line.quantity)}</p>}
                  </td>
                  <td className="px-4 py-3 text-right font-medium tabular-nums">{formatCurrency(line.lineTotal)}</td>
                </tr>)}
              </tbody>
            </table>
          </div>
        </div>
        <div className="ml-auto grid w-full max-w-sm gap-2 rounded-2xl bg-slate-950 p-5 text-white">
          <p className="pb-1 text-xs font-semibold uppercase tracking-wide text-slate-300">Factura principal · COP contable</p>
          <Amount label="Subtotal" value={detail.functionalNetAmount ?? detail.netAmount} />
          <Amount label="IVA" value={detail.functionalTaxAmount ?? detail.taxAmount} />
          <Amount label="Total bruto" value={detail.functionalGrandTotal ?? detail.grandTotal} />
          {(detail.withholding?.lines ?? []).map(line => <Amount key={`${line.ruleId}-${line.ruleVersion}`} label={`${line.name} (${line.rate}%)`} value={-line.amount} />)}
          {detail.withholding && detail.withholding.withholdingTotal > 0 && <Amount label="Total retenciones" value={-detail.withholding.withholdingTotal} />}
          <Amount label={detail.withholding?.withholdingTotal ? "Neto por pagar" : "Total"} value={detail.withholding?.netAmount ?? detail.functionalGrandTotal ?? detail.grandTotal} strong />
          {detail.currencyCode !== "COP" && <p className="pt-2 text-xs text-slate-300">Documento original: {detail.grandTotal} {detail.currencyCode} · tasa {detail.exchangeRate} ({detail.exchangeRateSource})</p>}
        </div>
        <section className="rounded-2xl border p-4">
          <h3 className="font-semibold">Estado contable</h3>
          {(detail.accountingStatuses ?? []).length === 0
            ? <p className="mt-1 text-sm text-muted-foreground">La contabilidad no está activa o el movimiento aún está entrando al motor.</p>
            : <div className="mt-3 grid gap-2 md:grid-cols-2">{detail.accountingStatuses!.map((posting) => {
              const cost = detail.additionalCostDocuments?.find((document) => document.costDocumentId === posting.sourceDocumentId);
              return <div key={posting.sourceDocumentId} className="flex items-start justify-between gap-3 rounded-xl bg-muted/30 p-3">
                <div><p className="text-sm font-medium">{cost?.documentNumber ?? detail.documentNumber}</p>{posting.errorMessage && <p className="text-xs text-amber-700">{posting.errorMessage}</p>}</div>
                <Badge variant={posting.status === "Posted" ? "secondary" : "outline"}>{posting.status === "Posted" ? "Contabilizado" : posting.status === "AccountingPendingConfiguration" ? "Requiere configuración" : "Pendiente"}</Badge>
              </div>;
            })}</div>}
        </section>
        {(detail.additionalCostDocuments ?? []).length > 0 && <section className="space-y-3">
          <div><h3 className="font-semibold">Facturas y costos asociados</h3>
            <p className="text-sm text-muted-foreground">Cada documento conserva su proveedor, vencimiento, IVA y contabilización independiente.</p></div>
          <div className="grid gap-3 md:grid-cols-2">
            {detail.additionalCostDocuments!.map((document) => <div key={document.costDocumentId} className="rounded-2xl border p-4">
              <div className="flex items-start justify-between gap-3"><div><p className="font-semibold">{document.documentNumber}</p><p className="text-xs text-muted-foreground">{purchaseEvidenceLabels[document.purchaseEvidenceType]}</p></div><Badge variant="outline">{document.currencyCode}</Badge></div>
              <div className="mt-3 space-y-1 text-sm">{document.lines.map((line) => <div key={line.lineNumber} className="flex justify-between gap-3"><span>{line.description}</span><span>{formatCurrency(line.functionalAmount ?? line.amount)}</span></div>)}
                <div className="flex justify-between"><span>IVA del documento</span><span>{formatCurrency(document.functionalTaxAmount ?? 0)}</span></div>
                {(document.withholding?.lines ?? []).map(line => <div key={`${line.ruleId}-${line.ruleVersion}`} className="flex justify-between text-amber-700"><span>{line.name} ({line.rate}%)</span><span>-{formatCurrency(line.amount)}</span></div>)}
                <div className="flex justify-between"><span>Total bruto en COP</span><span>{formatCurrency(document.functionalGrandTotal ?? 0)}</span></div>
                <div className="flex justify-between border-t pt-2 font-semibold"><span>Neto por pagar</span><span>{formatCurrency(document.withholding?.netAmount ?? document.functionalGrandTotal ?? 0)}</span></div></div>
            </div>)}
          </div>
        </section>}
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
  open, draft, businessId, canConfirm, canAssociateProducts, onChange, onClose, onClear,
}: {
  open: boolean; draft?: EditorDraft; businessId: string | null; canConfirm: boolean;
  canAssociateProducts: boolean;
  onChange: (draft: EditorDraft) => void; onClose: () => void; onClear: () => void;
}) {
  const options = useGoodsReceiptOptions();
  const availableOrders = useQuery({
    queryKey: ["purchase-orders-receivable", businessId],
    queryFn: async () => {
      const [openOrders, partialOrders] = await Promise.all([
        purchaseOrdersApi.list({ page: 1, pageSize: 100, status: "Open" }),
        purchaseOrdersApi.list({ page: 1, pageSize: 100, status: "PartiallyReceived" }),
      ]);
      return [...openOrders.items, ...partialOrders.items];
    },
    enabled: open && !!businessId,
  });
  const subscription = useQuery({ queryKey: ["tenant-commercial", "subscription"], queryFn: tenantCommercialApi.subscription });
  const [productSearch, setProductSearch] = useState("");
  const [includeUnassociated, setIncludeUnassociated] = useState(false);
  const [productMenuOpen, setProductMenuOpen] = useState(false);
  const [pendingAssociation, setPendingAssociation] = useState<GoodsReceiptProduct>();
  const [supplierProductCode, setSupplierProductCode] = useState("");
  const [purchasePresentationName, setPurchasePresentationName] = useState("Unidad");
  const [unitsPerPresentation, setUnitsPerPresentation] = useState(1);
  const [costsExpanded, setCostsExpanded] = useState(false);
  const [editingCostDocument, setEditingCostDocument] = useState<GoodsReceiptCostDocument | undefined>(
    () => draft?.pendingCostDocument ?? undefined,
  );
  const [costSupplierNames, setCostSupplierNames] = useState<Record<string, string>>({});
  const [pendingSupplierChange, setPendingSupplierChange] = useState<PendingSupplierChange>();
  const [selectedSupplier, setSelectedSupplier] = useState<PartyWorkspaceItem | null>(null);
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

  const withholdingPreview = useQuery({
    queryKey: [
      "goods-receipt-withholding-preview", businessId, draft?.supplierId,
      draft?.supplierInvoiceDate, draft?.purchaseEvidenceType,
      draft?.withholdingConceptCode, draft?.lines,
    ],
    queryFn: () => {
      if (!businessId || !draft) throw new Error("La recepción no está lista para calcular retenciones.");
      return goodsReceiptsApi.previewWithholding({
        businessId, supplierId: draft.supplierId,
        supplierInvoiceDate: toIsoOrNull(draft.supplierInvoiceDate)!,
        lines: draft.lines,
        withholdingConceptCode: draft.withholdingConceptCode.trim() || null,
        withholdingJurisdictionCode: null,
        purchaseEvidenceType: draft.purchaseEvidenceType as PurchaseEvidenceType,
        exchangeRate: draft.currencyCode === "COP" ? 1 : draft.exchangeRate,
      });
    },
    enabled: Boolean(
      open && businessId && draft?.supplierId && draft.supplierInvoiceDate &&
      draft.purchaseEvidenceType && draft.lines.length > 0,
    ),
    staleTime: 10_000,
  });
  const costWithholdingPreviews = useQueries({
    queries: (draft?.additionalCostDocuments ?? []).map((document) => ({
      queryKey: ["goods-receipt-cost-withholding-preview", businessId, document],
      queryFn: () => goodsReceiptsApi.previewCostWithholding({
        businessId: businessId!, document: serializeCostDocuments([document])[0],
      }),
      enabled: Boolean(open && businessId && document.supplierId && document.issuedAt &&
        document.exchangeRate > 0 && document.lines.length > 0),
      staleTime: 10_000,
    })),
  });
  const costSupplierIds = [...new Set((draft?.additionalCostDocuments ?? [])
    .map((document) => document.supplierId).filter(Boolean))];
  const costSupplierQueries = useQueries({
    queries: costSupplierIds.map((supplierId) => ({
      queryKey: ["goods-receipt-cost-supplier", businessId, supplierId],
      queryFn: async () => (await partiesApi.page({
        page: 1, pageSize: 1, role: "Supplier", roleId: supplierId, isActive: true,
      })).items[0] ?? null,
      enabled: Boolean(open && businessId && supplierId),
      staleTime: 5 * 60 * 1000,
    })),
  });
  if (!draft || !businessId) return null;
  const totals = calculateGoodsReceiptTotals(draft.lines);
  const mainRate = draft.currencyCode === "COP" ? 1 : draft.exchangeRate;
  const mainInventoryCost = draft.lines.reduce((sum, line) => {
    const calculated = calculateGoodsReceiptLine(line);
    return sum + (calculated.net + (line.taxTreatment === "CapitalizedCost" ? calculated.tax : 0)) * mainRate;
  }, 0);
  const additionalSummary = draft.additionalCostDocuments.reduce((summary, document, index) => {
    const rate = document.currencyCode === "COP" ? 1 : document.exchangeRate;
    const preview = costWithholdingPreviews[index]?.data;
    for (const line of document.lines) {
      const base = line.amount * rate;
      const capitalizedTax = line.taxTreatment === "CapitalizedCost" ? line.taxAmount * rate : 0;
      if (line.costTreatment === "Capitalize") summary.capitalized += base + capitalizedTax;
      else summary.expensed += base + capitalizedTax;
      if (line.taxTreatment === "DeductibleInputVat") summary.deductibleVat += line.taxAmount * rate;
    }
    const gross = document.lines.reduce((sum, line) => sum + line.amount + line.taxAmount, 0) * rate;
    summary.gross += gross;
    summary.withheld += preview?.withholdingTotal ?? 0;
    summary.payable += preview?.netAmount ?? gross;
    return summary;
  }, { capitalized: 0, expensed: 0, deductibleVat: 0, gross: 0, withheld: 0, payable: 0 });
  const mainDeductibleVat = draft.lines.reduce((sum, line) => {
    const calculated = calculateGoodsReceiptLine(line);
    return sum + (line.taxTreatment === "DeductibleInputVat" ? calculated.tax * mainRate : 0);
  }, 0);
  const totalWithheld = (withholdingPreview.data?.withholdingTotal ?? 0) + additionalSummary.withheld;
  const totalPayable = (draft.createsPayable
    ? (withholdingPreview.data?.netAmount ?? totals.total * mainRate) : 0) + additionalSummary.payable;
  const hasExtendedSummary = draft.currencyCode !== "COP" || draft.additionalCostDocuments.length > 0;
  const resolvedCostSupplierNames = Object.fromEntries(costSupplierIds.map((supplierId, index) => [
    supplierId,
    costSupplierNames[supplierId] ?? costSupplierQueries[index]?.data?.displayName ?? "Proveedor seleccionado",
  ]));
  const allowedEvidenceTypes = allowedPurchaseEvidenceTypes(selectedSupplier?.supplierPurchaseEvidencePolicy ?? null);
  const visibleEvidenceTypes = options.data?.purchaseEvidenceTypes.filter((item) =>
    allowedEvidenceTypes.includes(item.code)) ?? [];

  const change = (values: Partial<EditorDraft>) => onChange({ ...draft, ...values });
  const editCostDocument = (document: GoodsReceiptCostDocument) => {
    setEditingCostDocument(document);
    change({ pendingCostDocument: document });
  };
  const closeCostDocument = () => {
    setEditingCostDocument(undefined);
    change({ pendingCostDocument: null });
  };

  const addCostDocument = (evidence: PurchaseEvidenceType = "SupplierElectronicInvoice") => {
    const importDocument = evidence === "ImportDeclaration";
    editCostDocument({
      costDocumentId: crypto.randomUUID(), supplierId: "", purchaseEvidenceType: evidence,
      documentNumber: "", issuedAt: todayInput(), createsPayable: true,
      dueDate: plusDaysFrom(todayInput(), 30), currencyCode: "COP", exchangeRate: 1,
      exchangeRateDate: todayInput(), exchangeRateSource: "FunctionalCurrency",
      lines: importDocument ? [
        newCostLine(1, "CustomsDuty", "Arancel", "Capitalize", "Value"),
        { ...newCostLine(2, "ImportVat", "IVA de importación", "Expense", "None"),
          taxCode: "01", taxRate: 19, taxTreatment: "DeductibleInputVat" },
      ] : [newCostLine(1, "Freight", "Flete de compra", "Capitalize", "Value")],
    });
  };
  const updateCostDocument = (values: Partial<GoodsReceiptCostDocument>) => {
    if (!editingCostDocument) return;
    editCostDocument({ ...editingCostDocument, ...values });
  };
  const updateCostLine = (document: GoodsReceiptCostDocument, lineNumber: number,
    values: Partial<GoodsReceiptCostLine>) =>
    updateCostDocument({
      lines: document.lines.map((line) => line.lineNumber === lineNumber ? { ...line, ...values } : line),
    });
  const addCostLine = (document: GoodsReceiptCostDocument) => {
    const next = Math.max(0, ...document.lines.map((line) => line.lineNumber)) + 1;
    updateCostDocument({
      lines: [...document.lines, newCostLine(next, "OtherDirectCost", "Otro costo directo", "Capitalize", "Value")],
    });
  };
  const removeCostLine = (document: GoodsReceiptCostDocument, lineNumber: number) =>
    updateCostDocument({
      lines: document.lines.filter((line) => line.lineNumber !== lineNumber)
        .map((line, index) => ({ ...line, lineNumber: index + 1 })),
    });
  const updateManualAllocation = (
    document: GoodsReceiptCostDocument, line: GoodsReceiptCostLine,
    receiptLineNumber: number, functionalAmount: number,
  ) => updateCostLine(document, line.lineNumber, {
    eligibleReceiptLineNumbers: draft.lines.map((item) => item.lineNumber),
    manualAllocations: draft.lines.map((item) => ({
      receiptLineNumber: item.lineNumber,
      functionalAmount: item.lineNumber === receiptLineNumber ? functionalAmount :
        line.manualAllocations?.find((allocation) =>
          allocation.receiptLineNumber === item.lineNumber)?.functionalAmount ?? 0,
    })),
  });
  const saveCostDocument = () => {
    if (!editingCostDocument) return;
    if (!editingCostDocument.supplierId || !editingCostDocument.documentNumber.trim() ||
        !editingCostDocument.issuedAt || editingCostDocument.lines.length === 0) {
      toast.error("Completa proveedor, número, fecha y al menos un concepto.");
      return;
    }
    const exists = draft.additionalCostDocuments.some((document) =>
      document.costDocumentId === editingCostDocument.costDocumentId);
    change({ additionalCostDocuments: exists
      ? draft.additionalCostDocuments.map((document) => document.costDocumentId === editingCostDocument.costDocumentId
        ? editingCostDocument : document)
      : [...draft.additionalCostDocuments, editingCostDocument], pendingCostDocument: null });
    setEditingCostDocument(undefined);
    setCostsExpanded(true);
  };

  const applySupplierChange = (supplier: PartyWorkspaceItem) => {
    const supplierId = supplier.supplierId ?? "";
    setSelectedSupplier(supplier);
    const evidenceType = supplier.supplierPurchaseEvidencePolicy ?? "";
    const issueDate = draft.supplierInvoiceDate || todayInput();
    change({ supplierId, lines: [], purchaseOrderId: "", purchaseEvidenceType: evidenceType,
      supplierInvoiceNumber: "", supplierInvoiceDate: issueDate,
      dueDate: draft.createsPayable
        ? plusDaysFrom(issueDate, supplier.supplierDefaultPaymentDueDays ?? 30) : "" });
    setPendingSupplierChange(undefined);
    setIncludeUnassociated(false);
    setProductSearch("");
  };

  const requestSupplierChange = (supplierId: string, supplier?: PartyWorkspaceItem) => {
    if (!supplier) return;
    if (supplierId === draft.supplierId) return;
    if (draft.lines.length === 0) {
      applySupplierChange(supplier);
      return;
    }
    setPendingSupplierChange({ supplier });
  };
  const request = (): SaveGoodsReceiptDraftRequest => ({
    draftId: draft.draftId, businessId,
    warehouseId: draft.warehouseId || null, supplierId: draft.supplierId || null,
    supplierInvoiceNumber: ["SupplierElectronicInvoice", "ForeignCommercialInvoice"].includes(draft.purchaseEvidenceType)
      ? draft.supplierInvoiceNumber.trim() || null : null,
    supplierInvoiceDate: toIsoOrNull(draft.supplierInvoiceDate),
    receivedAt: new Date().toISOString(), createsPayable: draft.createsPayable,
    dueDate: draft.createsPayable ? toIsoOrNull(draft.dueDate) : null,
    currencyCode: draft.currencyCode, notes: draft.notes.trim() || null,
    lines: draft.lines, concurrencyToken: draft.concurrencyToken,
    purchaseEvidenceType: draft.purchaseEvidenceType || null,
    purchaseOrderId: draft.purchaseOrderId || null,
    exchangeRate: draft.exchangeRate, exchangeRateDate: draft.exchangeRateDate || null,
    exchangeRateSource: draft.exchangeRateSource,
    additionalCostDocuments: serializeCostDocuments(draft.additionalCostDocuments),
  });

  const persist = async (notify = true) => {
    try {
      const saved = await save.mutateAsync(request());
      change({ concurrencyToken: saved.concurrencyToken });
      if (notify) {
        toast.success("Borrador guardado y disponible para recuperar.");
        onClear();
      }
      return saved;
    } catch {
      toast.error("No fue posible guardar. El borrador pudo cambiar en otra sesión.");
      return null;
    }
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
        latestUnitCost: product.latestUnitCost,
        averageUnitCost: product.averageUnitCost,
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
    if (!draft.concurrencyToken) return;
    try {
      await remove.mutateAsync({ draftId: draft.draftId, concurrencyToken: draft.concurrencyToken });
      toast.success("El borrador fue eliminado.");
      onClear();
    } catch { toast.error("No fue posible eliminar el borrador."); }
  };

  const confirmEntry = async () => {
    if (!draft.warehouseId || !draft.supplierId || !draft.purchaseEvidenceType || draft.lines.length === 0) {
      toast.error("Selecciona proveedor, bodega, tipo de soporte y agrega al menos un producto.");
      return;
    }
    if (!draft.supplierInvoiceDate) {
      toast.error("Indica la fecha de emisión del documento de compra.");
      return;
    }
    if (["SupplierElectronicInvoice", "ForeignCommercialInvoice"].includes(draft.purchaseEvidenceType) && !draft.supplierInvoiceNumber.trim()) {
      toast.error("La factura requiere el número del documento del proveedor.");
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
        supplierInvoiceNumber: ["SupplierElectronicInvoice", "ForeignCommercialInvoice"].includes(draft.purchaseEvidenceType)
          ? draft.supplierInvoiceNumber.trim() || null : null,
        supplierInvoiceDate: toIsoOrNull(draft.supplierInvoiceDate),
        receivedAt: new Date().toISOString(), createsPayable: draft.createsPayable,
        dueDate: draft.createsPayable ? toIsoOrNull(draft.dueDate) : null,
        currencyCode: draft.currencyCode, notes: draft.notes.trim() || null, lines: draft.lines,
        draftConcurrencyToken: confirmationToken,
        withholdingConceptCode: draft.withholdingConceptCode.trim() || null,
        withholdingJurisdictionCode: null,
        purchaseEvidenceType: draft.purchaseEvidenceType,
        purchaseOrderId: draft.purchaseOrderId || null,
        exchangeRate: draft.exchangeRate, exchangeRateDate: draft.exchangeRateDate || null,
        exchangeRateSource: draft.exchangeRateSource,
        additionalCostDocuments: serializeCostDocuments(draft.additionalCostDocuments),
      });
      toast.success(`${accepted.documentNumber} fue confirmada y enviada al motor.`, {
        duration: 12000,
        action: {
          label: "Revisar precios",
          onClick: () => router.push(`/dashboard/products/pricing?sourceDocumentId=${accepted.documentId}`),
        },
      });
      onClear();
    } catch (error) {
      const message = error && typeof error === "object" && "message" in error
        ? String(error.message)
        : "No fue posible confirmar. Revisa factura duplicada, permisos y datos.";
      toast.error(message);
    }
  };

  return <Dialog open={open} onOpenChange={(value) => !value && onClose()}>
    <DialogContent onEscapeKeyDown={(event) => { if (productMenuOpen) { event.preventDefault(); setProductMenuOpen(false); } }} className="flex max-h-[94dvh] w-[94vw] max-w-6xl flex-col overflow-hidden p-0">
      <DialogHeader className="border-b px-6 py-5">
        <DialogTitle className="flex items-center gap-2">
          <Truck className="h-5 w-5 text-primary" /> Recepción de compra
        </DialogTitle>
        <DialogDescription>
          {draft.concurrencyToken
            ? "Borrador guardado. Puedes continuarlo, confirmarlo o eliminarlo."
            : "Captura local sin guardar. Cerrar conserva lo digitado en este dispositivo."}
        </DialogDescription>
      </DialogHeader>

      <div className="flex-1 space-y-5 overflow-y-auto px-6 py-5">
        {options.isError && <div role="alert" className="flex items-start gap-2 rounded-xl border border-amber-300 bg-amber-50 p-3 text-sm text-amber-900">
          <CircleAlert className="mt-0.5 h-4 w-4 shrink-0" />
          No fue posible cargar los catálogos de recepción. Los campos dependientes quedan bloqueados para evitar guardar códigos inválidos; vuelve a intentar antes de confirmar.
        </div>}
        <section className="rounded-2xl border border-primary/20 bg-primary/5 p-4">
          <Field label="Orden de compra (opcional)">
            <Select value={draft.purchaseOrderId || "__none"} onValueChange={async (value) => {
              if (value === "__none") {
                change({ purchaseOrderId: "", lines: [] });
                return;
              }
              try {
                const order = await purchaseOrdersApi.receiptSource(value);
                change({
                  purchaseOrderId: order.purchaseOrderId,
                  warehouseId: order.warehouseId,
                  supplierId: order.supplierId,
                  notes: order.notes ?? "",
                  lines: order.lines.map((line, index) => ({
                    lineNumber: index + 1,
                    productId: line.productId,
                    description: line.description,
                    quantity: line.remainingQuantity,
                    unitCost: line.unitCost,
                    discountAmount: 0,
                    taxCode: line.taxCode,
                    taxRate: line.taxRate,
                    taxTreatment: line.taxTreatment as GoodsReceiptLine["taxTreatment"],
                    presentationName: line.presentationName,
                    presentationQuantity: line.remainingQuantity / line.unitsPerPresentation,
                    unitsPerPresentation: line.unitsPerPresentation,
                    purchaseOrderLineId: line.lineId,
                    overReceiptReason: null,
                    orderedQuantity: line.orderedQuantity,
                    remainingQuantity: line.remainingQuantity,
                  })),
                });
                toast.success(`${order.documentNumber} fue recuperada. Puedes ajustar las cantidades realmente recibidas.`);
              } catch {
                toast.error("No fue posible recuperar la orden de compra.");
              }
            }}>
              <SelectTrigger><SelectValue placeholder="Sin orden de compra" /></SelectTrigger>
              <SelectContent>
                <SelectItem value="__none">Sin orden de compra</SelectItem>
                {(availableOrders.data ?? []).map((order) =>
                  <SelectItem key={order.purchaseOrderId} value={order.purchaseOrderId}>
                    {order.documentNumber} · {order.supplierName} · pendiente {Math.max(0, 100 - order.fulfillmentPercent).toFixed(1)} %
                  </SelectItem>)}
              </SelectContent>
            </Select>
          </Field>
          <p className="mt-2 text-xs text-muted-foreground">Puedes recibir directamente sin orden. Si eliges una, Auraly propone únicamente su saldo pendiente.</p>
        </section>
        <section className="grid items-start gap-4 rounded-2xl border bg-muted/20 p-4 md:grid-cols-2 xl:grid-cols-3">
          <Field label="Proveedor">
            <PartyRoleSelect role="Supplier" value={draft.supplierId} placeholder="Buscar proveedor"
              disabled={!!draft.purchaseOrderId}
              onResolved={(supplier)=>{
                setSelectedSupplier(supplier);
                if(supplier?.supplierId===draft.supplierId){
                  const issueDate=draft.supplierInvoiceDate||todayInput();
                  const dueDate=draft.createsPayable
                    ? draft.dueDate||plusDaysFrom(issueDate,supplier.supplierDefaultPaymentDueDays??30)
                    : "";
                  if(issueDate!==draft.supplierInvoiceDate||dueDate!==draft.dueDate)change({supplierInvoiceDate:issueDate,dueDate});
                }
              }}
              onChange={requestSupplierChange}/>
          </Field>
          <Field label="Bodega">
            <Select value={draft.warehouseId} disabled={!!draft.purchaseOrderId} onValueChange={(value) => change({ warehouseId: value })}>
              <SelectTrigger><SelectValue placeholder="Seleccionar bodega" /></SelectTrigger>
              <SelectContent>{options.data?.warehouses.map((item) =>
                <SelectItem key={item.warehouseId} value={item.warehouseId}>
                  {item.name} · {item.code}
                </SelectItem>)}</SelectContent>
            </Select>
          </Field>
          <Field label={<span className="flex items-center gap-2">Tipo de soporte
            {draft.purchaseEvidenceType === "InternalReceiptVoucher" &&
              <TooltipProvider delayDuration={100}>
                <Tooltip>
                  <TooltipTrigger asChild>
                    <button type="button" aria-label="Información sobre el comprobante interno"
                      className="rounded-full text-amber-600 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring">
                      <CircleAlert className="h-4 w-4" />
                    </button>
                  </TooltipTrigger>
                  <TooltipContent className="max-w-xs">
                    Sólo registra inventario, cuenta por pagar y contabilidad. No genera documento fiscal ni IVA descontable.
                  </TooltipContent>
                </Tooltip>
              </TooltipProvider>}
          </span>}>
            <Select value={draft.purchaseEvidenceType} disabled={!draft.supplierId}
              onValueChange={(value: PurchaseEvidenceType) => {
                if (value === "BuyerElectronicSupportDocument" && subscription.data
                    && subscription.data.dianDocumentsUsed >= subscription.data.dianDocumentMonthlyLimit) {
                  toast.error("No hay cupo de documentos DIAN", { description: "Compra un paquete antes de seleccionar documento soporte electrónico." });
                  return;
                }
                change({ purchaseEvidenceType: value,
                supplierInvoiceNumber: value === "SupplierElectronicInvoice" ? draft.supplierInvoiceNumber : "",
                lines: value === "ForeignCommercialInvoice"
                  ? draft.lines.map((line) => ({ ...line, taxRate: 0, taxCode: "00",
                    taxTreatment: "NotApplicable" as const }))
                  : value === "InternalReceiptVoucher"
                  ? draft.lines.map((line) => line.taxRate > 0 && line.taxTreatment === "DeductibleInputVat"
                    ? { ...line, taxTreatment: "CapitalizedCost" as const } : line)
                  : draft.lines,
                });
              }}>
              <SelectTrigger aria-label="Tipo de soporte"><SelectValue placeholder="Seleccionar soporte" /></SelectTrigger>
              <SelectContent>{visibleEvidenceTypes.map((item) =>
                <SelectItem key={item.code} value={item.code}>{item.label}</SelectItem>)}</SelectContent>
            </Select>
          </Field>
          {["SupplierElectronicInvoice", "ForeignCommercialInvoice"].includes(draft.purchaseEvidenceType) &&
          <Field label="Factura del proveedor">
            <Input value={draft.supplierInvoiceNumber}
              onChange={(event) => change({ supplierInvoiceNumber: event.target.value })}
              placeholder="Número de factura" maxLength={80} />
          </Field>
          }
          <Field label="Fecha de emisión">
            <DatePicker value={draft.supplierInvoiceDate} onChange={(value) => change({
              supplierInvoiceDate:value,
              dueDate:draft.createsPayable?plusDaysFrom(value,selectedSupplier?.supplierDefaultPaymentDueDays??30):"",
            })} />
          </Field>
          <Field label="Moneda">
            <Select value={draft.currencyCode} onValueChange={(currencyCode) => change({
              currencyCode, exchangeRate: currencyCode === "COP" ? 1 : draft.exchangeRate,
              exchangeRateDate: currencyCode === "COP" ? draft.supplierInvoiceDate : (draft.exchangeRateDate || draft.supplierInvoiceDate),
              exchangeRateSource: currencyCode === "COP" ? "FunctionalCurrency" :
                (draft.exchangeRateSource === "FunctionalCurrency"
                  ? options.data?.exchangeRateSources[0]?.code ?? "" : draft.exchangeRateSource),
            })}><SelectTrigger disabled={options.isLoading || !(options.data?.purchaseCurrencies.length)}><SelectValue placeholder="Cargando monedas…" /></SelectTrigger><SelectContent>
              {(options.data?.purchaseCurrencies ?? []).map((item) => <SelectItem key={item.code} value={item.code}>{item.label}</SelectItem>)}
            </SelectContent></Select>
          </Field>
          {draft.currencyCode !== "COP" && <>
            <Field label="Tasa pactada/registrada a COP"><FormattedNumberInput kind="currency" value={draft.exchangeRate}
              onValueChange={(value) => change({ exchangeRate: value ?? 0 })} /></Field>
            <Field label="Fecha de la tasa"><DatePicker value={draft.exchangeRateDate} onChange={(exchangeRateDate) => change({ exchangeRateDate })} /></Field>
            <Field label="Fuente de la tasa"><Select value={draft.exchangeRateSource} onValueChange={(exchangeRateSource) => change({ exchangeRateSource })}>
              <SelectTrigger disabled={options.isLoading || !(options.data?.exchangeRateSources.length)}><SelectValue placeholder="Selecciona la fuente" /></SelectTrigger>
              <SelectContent>{(options.data?.exchangeRateSources ?? []).map((item) => <SelectItem key={item.code} value={item.code}>{item.label}</SelectItem>)}</SelectContent>
            </Select></Field>
          </>}
          {draft.purchaseEvidenceType === "BuyerElectronicSupportDocument" &&
            <p className="self-end rounded-lg bg-primary/5 p-3 text-sm text-muted-foreground md:col-span-2">
              Auraly asignará numeración, generará el CUDS y enviará el documento soporte a la DIAN.
            </p>}
          <Field label="Condición">
            <Select value={draft.createsPayable ? "Credit" : "Cash"}
              onValueChange={(value) => change({
                createsPayable: value === "Credit",
                dueDate: value === "Credit"
                  ? plusDaysFrom(draft.supplierInvoiceDate||todayInput(),selectedSupplier?.supplierDefaultPaymentDueDays??30) : "",
              })}>
              <SelectTrigger><SelectValue /></SelectTrigger>
              <SelectContent>
                <SelectItem value="Credit">Crédito · genera cuenta por pagar</SelectItem>
                <SelectItem value="Cash">Contado</SelectItem>
              </SelectContent>
            </Select>
          </Field>
          <Field label="Fecha de vencimiento">
            <DatePicker disabled={!draft.createsPayable} min={draft.supplierInvoiceDate||undefined}
              value={draft.dueDate} onChange={(dueDate)=>change({dueDate})} />
            <p className="text-xs text-muted-foreground">Se propone con los {selectedSupplier?.supplierDefaultPaymentDueDays??30} días configurados en el proveedor, pero puede ajustarse para este documento.</p>
          </Field>
        </section>
        <section className="rounded-2xl border">
          <div ref={productPickerRef} onBlur={(event) => { if (!event.currentTarget.contains(event.relatedTarget as Node | null)) setProductMenuOpen(false); }}>
          <div className="grid gap-3 border-b p-4 lg:grid-cols-[minmax(0,1fr)_auto_auto]">
            <div className="relative">
              <Barcode className="pointer-events-none absolute left-3 top-2.5 h-4 w-4 text-primary" />
              <Input ref={scanRef} className="pl-9" value={productSearch}
                disabled={!draft.supplierId || !!draft.purchaseOrderId}
                onFocus={() => setProductMenuOpen(true)}
                onClick={() => setProductMenuOpen(true)}
                onChange={(event) => { setProductSearch(event.target.value); setProductMenuOpen(true); }}
                onKeyDown={capture}
                placeholder={draft.supplierId
                  ? "Escanea o busca por código, referencia, nombre o código del proveedor"
                  : "Selecciona primero el proveedor"} />
            </div>
            <Button type="button" variant={includeUnassociated ? "secondary" : "outline"}
              disabled={!draft.supplierId || !!draft.purchaseOrderId}
              onClick={() => {
                setIncludeUnassociated((current) => !current);
                setProductMenuOpen(true);
                requestAnimationFrame(() => scanRef.current?.focus());
              }}>
              <Search className="mr-2 h-4 w-4" />
              {includeUnassociated ? "Ver productos del proveedor" : "Buscar en todo el catálogo"}
            </Button>
            <Button type="button" variant="outline"
              disabled={!!draft.purchaseOrderId || !productItems[activeProductIndex]}
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
                  <span className="text-right text-xs">
                    <span className="block font-medium">Último {product.latestUnitCost == null ? "—" : formatCurrency(product.latestUnitCost)}</span>
                    <span className="block text-muted-foreground">Promedio {product.averageUnitCost == null ? "—" : formatCurrency(product.averageUnitCost)}</span>
                  </span>
                </button>)}
              {products.hasNextPage && <Button type="button" variant="ghost" className="mt-2 w-full"
                disabled={products.isFetchingNextPage} onClick={() => products.fetchNextPage()}>
                {products.isFetchingNextPage ? "Cargando..." : "Cargar 50 más"}
              </Button>}
            </div>}
          {productMenuOpen && draft.supplierId && productSearch.trim().length === 0 && <div role="listbox" className="border-b px-4 py-3 text-sm text-muted-foreground">Escribe al menos una letra, código o referencia para buscar.</div>}
          {productMenuOpen && draft.supplierId && productSearch.trim().length > 0 && !products.isLoading && productItems.length === 0 && <div role="listbox" className="border-b px-4 py-3 text-sm text-muted-foreground">Sin resultados para esta búsqueda.</div>}
          </div>
          <div className="overflow-hidden">
            <table className="w-full table-fixed text-sm">
              <thead className="bg-muted/50 text-left text-xs font-semibold uppercase tracking-wide text-muted-foreground">
                <tr><th className="w-[34%] px-4 py-3">Producto</th><th className="w-[17%] px-3 py-3">Cantidad recibida</th>
                  <th className="w-[14%] px-3 py-3">Costo unitario</th><th className="w-[12%] px-3 py-3">Descuento</th>
                  <th className="w-[7%] px-3 py-3 text-center">IVA</th><th className="w-[11%] px-3 py-3 text-right">Factura / inventario</th>
                  <th className="w-[5%]" /></tr>
              </thead>
              <tbody>{draft.lines.map((line) => {
                const calculatedLine = calculateGoodsReceiptLine(line);
                const lineInventoryCost = (calculatedLine.net +
                  (line.taxTreatment === "CapitalizedCost" ? calculatedLine.tax : 0)) * mainRate;
                return <tr key={line.productId} className="border-t align-top">
                  <td className="break-words px-4 py-3"><p className="font-semibold">{line.description}</p>
                    <p className="text-xs text-muted-foreground">IVA de compra {line.taxRate} % · {purchaseTaxTreatmentLabels[line.taxTreatment] ?? line.taxTreatment}</p>
                    <p className="text-xs text-muted-foreground">Costo promedio {line.averageUnitCost == null ? "—" : formatCurrency(line.averageUnitCost)} · Último costo {line.latestUnitCost == null ? "—" : formatCurrency(line.latestUnitCost)}</p>
                    <p className="text-xs text-muted-foreground">{line.presentationQuantity} {line.presentationName.toLowerCase()} × {line.unitsPerPresentation} = {line.quantity} {goodsReceiptUnitLabel(line.baseUnitCode, line.quantity)}</p></td>
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
                        const name = value === "base" ? goodsReceiptUnitLabel(line.baseUnitCode) : (line.preferredPresentationName ?? line.presentationName);
                        updateLine(line.productId, { presentationName: name, unitsPerPresentation: factor, quantity: calculateBaseQuantity(line.presentationQuantity, factor) });
                      }}>
                      <SelectTrigger className="mt-1 h-8 w-full px-2 text-xs"><SelectValue /></SelectTrigger>
                      <SelectContent>
                        <SelectItem value="base">{goodsReceiptUnitLabel(line.baseUnitCode)}</SelectItem>
                        <SelectItem value="package">{line.preferredPresentationName ?? line.presentationName} · {line.preferredUnitsPerPresentation ?? line.unitsPerPresentation} {goodsReceiptUnitLabel(line.baseUnitCode, line.preferredUnitsPerPresentation ?? line.unitsPerPresentation)}</SelectItem>
                      </SelectContent>
                    </Select>}
                    {(line.preferredUnitsPerPresentation ?? line.unitsPerPresentation) <= 1 && <span className="mt-1 flex h-8 items-center rounded-md bg-muted/40 px-2 text-xs text-muted-foreground">{goodsReceiptUnitLabel(line.baseUnitCode)}</span>}
                    {line.remainingQuantity != null && <span className="mt-1 block text-xs text-muted-foreground">Pendiente en orden: {line.remainingQuantity}</span>}
                    {line.remainingQuantity != null && line.quantity > line.remainingQuantity && <Input className="mt-2" value={line.overReceiptReason ?? ""}
                      onChange={(event) => updateLine(line.productId, { overReceiptReason: event.target.value })}
                      placeholder="Motivo obligatorio del excedente" maxLength={500} />}
                  </td>
                  <td className="px-3 py-2"><FormattedNumberInput kind="currency"
                    ariaLabel={`Costo unitario de ${line.description}`}
                    value={line.unitCost} onValueChange={(value) =>
                      updateLine(line.productId, { unitCost: value ?? 0 })} /></td>
                  <td className="px-3 py-2"><FormattedNumberInput kind="currency"
                    ariaLabel={`Descuento de ${line.description}`}
                    value={line.discountAmount} onValueChange={(value) =>
                      updateLine(line.productId, { discountAmount: value ?? 0 })} /></td>
                  <td className="px-3 py-4 text-center">{line.taxRate} %</td>
                  <td className="break-words px-3 py-4 text-right text-xs">
                    <p className="font-semibold">Factura {formatCurrency(calculatedLine.total)} {draft.currencyCode}</p>
                    <p className="mt-1 text-muted-foreground">Al inventario {formatCurrency(lineInventoryCost)} COP</p>
                  </td>
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

        <section className="space-y-4 rounded-2xl border p-4">
          <button type="button" onClick={() => setCostsExpanded((current) => !current)}
            className="flex w-full items-center justify-between gap-4 text-left"
            aria-expanded={costsExpanded}>
            <span><strong className="block">Facturas y otros costos</strong>
              <small className="text-muted-foreground">Opcional · flete, seguro, gastos directos y nacionalización</small></span>
            <span className="flex items-center gap-2">
              {draft.additionalCostDocuments.length > 0 && <Badge variant="secondary">{draft.additionalCostDocuments.length}</Badge>}
              <ChevronDown className={`h-5 w-5 transition-transform ${costsExpanded ? "rotate-180" : ""}`} />
            </span>
          </button>
          {costsExpanded && <div className="space-y-5 border-t pt-4">
            <div className="flex flex-wrap items-start justify-between gap-3">
              <p className="max-w-3xl text-sm text-muted-foreground">Cada documento conserva su proveedor, vencimiento, IVA, retención y cuenta por pagar. Solo el valor marcado como capitalizable se distribuye al inventario.</p>
              <div className="flex flex-wrap gap-2"><Button type="button" variant="outline" onClick={() => addCostDocument()}><Plus className="mr-2 h-4 w-4" />Agregar factura</Button>
                <Button type="button" variant="outline" onClick={() => addCostDocument("ImportDeclaration")}><Plus className="mr-2 h-4 w-4" />Agregar nacionalización</Button></div>
            </div>
            <CostDocumentGrid title="Facturas adicionales"
              documents={draft.additionalCostDocuments.filter((document) => document.purchaseEvidenceType !== "ImportDeclaration")}
              supplierNames={resolvedCostSupplierNames}
              onOpen={(document) => editCostDocument(structuredClone(document))}
              onRemove={(id) => change({ additionalCostDocuments: draft.additionalCostDocuments.filter((document) => document.costDocumentId !== id) })} />
            <CostDocumentGrid title="Nacionalización"
              documents={draft.additionalCostDocuments.filter((document) => document.purchaseEvidenceType === "ImportDeclaration")}
              supplierNames={resolvedCostSupplierNames}
              onOpen={(document) => editCostDocument(structuredClone(document))}
              onRemove={(id) => change({ additionalCostDocuments: draft.additionalCostDocuments.filter((document) => document.costDocumentId !== id) })} />
          </div>}
          {editingCostDocument && [editingCostDocument].map((document) => {
            const committedIndex = draft.additionalCostDocuments.findIndex((item) => item.costDocumentId === document.costDocumentId);
            const withholding = committedIndex >= 0 ? costWithholdingPreviews[committedIndex] : undefined;
            const rate = document.currencyCode === "COP" ? 1 : document.exchangeRate;
            const documentNet = document.lines.reduce((sum, line) => sum + line.amount, 0);
            const documentTax = document.lines.reduce((sum, line) => sum + line.taxAmount, 0);
            const functionalGross = (documentNet + documentTax) * rate;
            const evidenceLabel = options.data?.purchaseCostEvidenceTypes.find((item) =>
              item.code === document.purchaseEvidenceType)?.label ?? document.purchaseEvidenceType;
            return <Dialog key={document.costDocumentId} open onOpenChange={(value) => !value && closeCostDocument()}>
              <DialogContent className="flex max-h-[92dvh] max-w-6xl flex-col overflow-hidden p-0">
              <DialogHeader className="border-b px-6 py-5">
                <DialogTitle>{committedIndex >= 0 ? "Editar" : "Agregar"} {document.purchaseEvidenceType === "ImportDeclaration" ? "nacionalización" : "factura adicional"}</DialogTitle>
                <DialogDescription>{evidenceLabel}. Captura sus conceptos, impuestos, distribución y vencimiento independiente.</DialogDescription>
              </DialogHeader>
              <div className="space-y-4 overflow-y-auto px-6 py-5">
              <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
                <Field label="Proveedor"><PartyRoleSelect role="Supplier" value={document.supplierId} placeholder="Buscar proveedor"
                  onResolved={(supplier) => supplier && setCostSupplierNames((names) => ({ ...names, [supplier.supplierId ?? supplier.partyId]: supplier.displayName }))}
                  onChange={(supplierId, supplier) => {
                    if (supplier) setCostSupplierNames((names) => ({ ...names, [supplierId]: supplier.displayName }));
                    updateCostDocument({ supplierId });
                  }} /></Field>
                <Field label="Soporte"><Select value={document.purchaseEvidenceType} onValueChange={(purchaseEvidenceType: PurchaseEvidenceType) => updateCostDocument({ purchaseEvidenceType })}><SelectTrigger disabled={options.isLoading || !(options.data?.purchaseCostEvidenceTypes.length)}><SelectValue placeholder="Cargando soportes…" /></SelectTrigger><SelectContent>{(options.data?.purchaseCostEvidenceTypes ?? []).map((item) => <SelectItem key={item.code} value={item.code}>{item.label}</SelectItem>)}</SelectContent></Select></Field>
                <Field label="Número"><Input value={document.documentNumber} maxLength={80} onChange={(event) => updateCostDocument({ documentNumber: event.target.value })} /></Field>
                <Field label="Fecha de emisión"><DatePicker value={document.issuedAt.slice(0, 10)} onChange={(issuedAt) => updateCostDocument({ issuedAt })} /></Field>
                <Field label="Moneda"><Select value={document.currencyCode} onValueChange={(currencyCode) => updateCostDocument({ currencyCode, exchangeRate: currencyCode === "COP" ? 1 : document.exchangeRate, exchangeRateSource: currencyCode === "COP" ? "FunctionalCurrency" : (document.exchangeRateSource === "FunctionalCurrency" ? options.data?.exchangeRateSources[0]?.code ?? "" : document.exchangeRateSource) })}><SelectTrigger disabled={options.isLoading || !(options.data?.purchaseCurrencies.length)}><SelectValue placeholder="Cargando monedas…" /></SelectTrigger><SelectContent>{(options.data?.purchaseCurrencies ?? []).map((item) => <SelectItem key={item.code} value={item.code}>{item.label}</SelectItem>)}</SelectContent></Select></Field>
                {document.currencyCode !== "COP" && <><Field label="Tasa pactada/registrada a COP"><FormattedNumberInput kind="currency" value={document.exchangeRate} onValueChange={(value) => updateCostDocument({ exchangeRate: value ?? 0 })} /></Field><Field label="Fecha de la tasa"><DatePicker value={document.exchangeRateDate?.slice(0, 10) ?? document.issuedAt.slice(0, 10)} onChange={(exchangeRateDate) => updateCostDocument({ exchangeRateDate })} /></Field><Field label="Fuente de la tasa"><Select value={document.exchangeRateSource} onValueChange={(exchangeRateSource) => updateCostDocument({ exchangeRateSource })}><SelectTrigger disabled={options.isLoading || !(options.data?.exchangeRateSources.length)}><SelectValue placeholder="Selecciona la fuente" /></SelectTrigger><SelectContent>{(options.data?.exchangeRateSources ?? []).map((item) => <SelectItem key={item.code} value={item.code}>{item.label}</SelectItem>)}</SelectContent></Select></Field></>}
                <Field label="Condición"><Input readOnly value="Crédito · cuenta por pagar independiente" /></Field>
                {document.createsPayable && <Field label="Vencimiento"><DatePicker min={document.issuedAt.slice(0, 10)} value={document.dueDate?.slice(0, 10) ?? ""} onChange={(dueDate) => updateCostDocument({ dueDate })} /></Field>}
                <Field label="Concepto para retención"><Select value={document.withholdingConceptCode || "__none"} onValueChange={(value) => updateCostDocument({ withholdingConceptCode: value === "__none" ? null : value })}><SelectTrigger><SelectValue /></SelectTrigger><SelectContent><SelectItem value="__none">Sin concepto</SelectItem>{(options.data?.withholdingConcepts ?? []).map((item) => <SelectItem key={item.code} value={item.code}>{item.label}</SelectItem>)}</SelectContent></Select></Field>
              </div>
              <div className="space-y-3">
                {document.lines.map((line) => {
                  const allocationLabel = options.data?.purchaseCostAllocationMethods.find((item) => item.code === line.allocationMethod)?.label ?? line.allocationMethod;
                  const capitalizedTax = line.taxTreatment === "CapitalizedCost" ? line.taxAmount : 0;
                  const manualTarget = (line.amount + capitalizedTax) * rate;
                  const manualAssigned = line.manualAllocations?.reduce((sum, allocation) => sum + allocation.functionalAmount, 0) ?? 0;
                  return <div key={line.lineNumber} className="space-y-3 rounded-xl border bg-background p-3">
                    <div className="flex justify-end">
                      {document.lines.length > 1 && <Button type="button" size="icon" variant="ghost" aria-label={`Eliminar concepto ${line.lineNumber}`} onClick={() => removeCostLine(document, line.lineNumber)}><Trash2 className="h-4 w-4" /></Button>}</div>
                    <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
                      <Field label="Concepto"><Select value={line.costKind} onValueChange={(costKind: PurchaseCostKind) => updateCostLine(document, line.lineNumber, { costKind, description: options.data?.purchaseCostKinds.find((item) => item.code === costKind)?.label ?? costKind, ...(costKind === "ImportVat" ? { amount: 0, taxCode: "01", taxRate: 19, taxTreatment: "DeductibleInputVat" as const, costTreatment: "Expense" as const, allocationMethod: "None" as const } : {}) })}><SelectTrigger disabled={options.isLoading || !(options.data?.purchaseCostKinds.length)}><SelectValue placeholder="Cargando conceptos…" /></SelectTrigger><SelectContent>{(options.data?.purchaseCostKinds ?? []).map((item) => <SelectItem key={item.code} value={item.code}>{item.label}</SelectItem>)}</SelectContent></Select></Field>
                      <Field label="Valor antes de IVA"><FormattedNumberInput kind="currency" value={line.amount} onValueChange={(value) => updateCostLine(document, line.lineNumber, { amount: value ?? 0, taxableBaseAmount: document.purchaseEvidenceType === "ImportDeclaration" ? line.taxableBaseAmount : value ?? 0, taxAmount: Math.round((document.purchaseEvidenceType === "ImportDeclaration" ? line.taxableBaseAmount : value ?? 0) * line.taxRate) / 100 })} /></Field>
                      <Field label="IVA del documento"><Select value={String(line.taxRate)} onValueChange={(value) => { const taxRate = Number(value); updateCostLine(document, line.lineNumber, { taxRate, taxAmount: Math.round(line.taxableBaseAmount * taxRate) / 100, taxTreatment: taxRate ? (line.taxTreatment === "CapitalizedCost" ? "CapitalizedCost" : "DeductibleInputVat") : "NotApplicable" }); }}><SelectTrigger disabled={options.isLoading || !(options.data?.purchaseTaxRates.length)}><SelectValue placeholder="Cargando tarifas…" /></SelectTrigger><SelectContent>{(options.data?.purchaseTaxRates ?? []).map((item) => <SelectItem key={item.code} value={item.code}>{item.label}</SelectItem>)}</SelectContent></Select></Field>
                      {document.purchaseEvidenceType === "ImportDeclaration" && <Field label="Base aduanera del IVA"><FormattedNumberInput kind="currency" value={line.taxableBaseAmount} onValueChange={(value) => updateCostLine(document, line.lineNumber, { taxableBaseAmount: value ?? 0, taxAmount: Math.round((value ?? 0) * line.taxRate) / 100 })} /></Field>}
                      {line.taxRate > 0 && <Field label="Tratamiento del IVA"><Select value={line.taxTreatment} onValueChange={(taxTreatment: "DeductibleInputVat" | "CapitalizedCost") => updateCostLine(document, line.lineNumber, { taxTreatment })}><SelectTrigger disabled={options.isLoading || !(options.data?.purchaseTaxTreatments.length)}><SelectValue placeholder="Cargando tratamientos…" /></SelectTrigger><SelectContent>{(options.data?.purchaseTaxTreatments ?? []).filter((item) => item.code !== "NotApplicable").map((item) => <SelectItem key={item.code} value={item.code}>{item.label}</SelectItem>)}</SelectContent></Select></Field>}
                      <Field label="Tratamiento del costo"><Select value={line.costTreatment} onValueChange={(costTreatment: "Capitalize" | "Expense") => updateCostLine(document, line.lineNumber, { costTreatment, allocationMethod: costTreatment === "Expense" ? "None" : (line.allocationMethod === "None" ? "Value" : line.allocationMethod), eligibleReceiptLineNumbers: null, manualAllocations: null })}><SelectTrigger disabled={options.isLoading || !(options.data?.purchaseCostTreatments.length)}><SelectValue placeholder="Cargando tratamientos…" /></SelectTrigger><SelectContent>{(options.data?.purchaseCostTreatments ?? []).map((item) => <SelectItem key={item.code} value={item.code}>{item.label}</SelectItem>)}</SelectContent></Select></Field>
                      {line.costTreatment === "Capitalize" && <Field label="Cómo prorratear"><Select value={line.allocationMethod} onValueChange={(allocationMethod: PurchaseCostAllocationMethod) => updateCostLine(document, line.lineNumber, { allocationMethod, eligibleReceiptLineNumbers: allocationMethod === "Manual" ? draft.lines.map((item) => item.lineNumber) : null, manualAllocations: allocationMethod === "Manual" ? draft.lines.map((item) => ({ receiptLineNumber: item.lineNumber, functionalAmount: 0 })) : null })}><SelectTrigger disabled={options.isLoading || !(options.data?.purchaseCostAllocationMethods.length)}><SelectValue placeholder="Cargando métodos…" /></SelectTrigger><SelectContent>{(options.data?.purchaseCostAllocationMethods ?? []).map((item) => <SelectItem key={item.code} value={item.code}>{item.label}</SelectItem>)}</SelectContent></Select></Field>}
                    </div>
                    {(line.allocationMethod === "Weight" || line.allocationMethod === "Volume") && <div className="grid gap-2 rounded-xl bg-muted/25 p-3 md:grid-cols-2"><p className="text-sm text-muted-foreground md:col-span-2">Indica el {line.allocationMethod === "Weight" ? "peso bruto total" : "volumen total"} de cada producto; el motor usará esa proporción.</p>{draft.lines.map((product) => <Field key={product.productId} label={product.description}><Input aria-label={`${line.allocationMethod === "Weight" ? "Peso" : "Volumen"} de ${product.description}`} type="number" min="0.000001" step="0.001" value={(line.allocationMethod === "Weight" ? product.totalGrossWeightKg : product.totalVolumeM3) ?? ""} onChange={(event) => updateLine(product.productId, line.allocationMethod === "Weight" ? { totalGrossWeightKg: Number(event.target.value) } : { totalVolumeM3: Number(event.target.value) })} /></Field>)}</div>}
                    {line.allocationMethod === "Manual" && <div className="grid gap-2 rounded-xl bg-muted/25 p-3 md:grid-cols-2"><p className="text-sm text-muted-foreground md:col-span-2">Distribuye exactamente {formatCurrency(manualTarget)} COP. La diferencia debe quedar en cero.</p>{draft.lines.map((product) => <Field key={product.productId} label={product.description}><FormattedNumberInput kind="currency" value={line.manualAllocations?.find((allocation) => allocation.receiptLineNumber === product.lineNumber)?.functionalAmount ?? 0} onValueChange={(value) => updateManualAllocation(document, line, product.lineNumber, value ?? 0)} /></Field>)}<p className={`text-sm font-medium md:col-span-2 ${Math.abs(manualTarget - manualAssigned) < 0.0001 ? "text-emerald-700" : "text-amber-700"}`}>Diferencia pendiente: {formatCurrency(manualTarget - manualAssigned)} COP</p></div>}
                    <div className="grid gap-3 text-sm md:grid-cols-3"><DocumentTotal label="Base" value={line.amount} suffix={document.currencyCode} /><DocumentTotal label="IVA" value={line.taxAmount} suffix={document.currencyCode} /><DocumentTotal label={line.costTreatment === "Capitalize" ? `Al inventario · ${allocationLabel}` : "Al gasto"} value={(line.amount + capitalizedTax) * rate} suffix="COP" /></div>
                  </div>;
                })}
                <Button type="button" variant="outline" size="sm" onClick={() => addCostLine(document)}><Plus className="mr-2 h-4 w-4" />Agregar concepto</Button>
              </div>
              <div className="grid gap-3 rounded-xl border bg-background p-3 text-sm md:grid-cols-2 xl:grid-cols-5">
                <DocumentTotal label="Subtotal" value={documentNet} suffix={document.currencyCode} />
                <DocumentTotal label="IVA del documento" value={documentTax} suffix={document.currencyCode} />
                <DocumentTotal label="Total bruto" value={documentNet + documentTax} suffix={document.currencyCode} />
                {document.currencyCode !== "COP" && <DocumentTotal label="Reconocido en COP" value={functionalGross} suffix="COP" />}
                <DocumentTotal label="Retenciones" value={-(withholding?.data?.withholdingTotal ?? 0)} suffix="COP" />
                <DocumentTotal label="Neto por pagar" value={withholding?.data?.netAmount ?? functionalGross} suffix="COP" strong />
                <p className="text-xs text-muted-foreground md:col-span-2 xl:col-span-5">El IVA descontable se reconoce separado; únicamente las bases y los impuestos marcados como mayor valor se llevan al costo o gasto.</p>
                {withholding?.isFetching && <p className="text-xs text-muted-foreground md:col-span-2 xl:col-span-5">Calculando retenciones con el motor tributario…</p>}
                {withholding?.isError && <p className="text-xs text-amber-700 md:col-span-2 xl:col-span-5">La vista previa no está disponible; la confirmación volverá a validarla.</p>}
              </div>
              </div>
              <DialogFooter className="border-t px-6 py-4">
                <Button type="button" variant="outline" onClick={closeCostDocument}>Cancelar</Button>
                <Button type="button" onClick={saveCostDocument}>{committedIndex >= 0 ? "Guardar cambios" : "Agregar documento"}</Button>
              </DialogFooter>
              </DialogContent>
            </Dialog>;
          })}
        </section>

        <section className="grid gap-3 md:grid-cols-[minmax(0,1fr)_23rem]">
          <Textarea value={draft.notes} onChange={(event) => change({ notes: event.target.value })}
            className="h-full min-h-[8.5rem] resize-none"
            placeholder="Observaciones de recepción" maxLength={1000} />
          <dl className="space-y-2 rounded-2xl bg-slate-950 p-5 text-white">
            <p className="pb-1 text-xs font-semibold uppercase tracking-wide text-slate-300">{hasExtendedSummary ? "Resumen consolidado · COP" : "Resumen de la compra"}</p>
            {!hasExtendedSummary ? <>
              <Amount label="Subtotal antes de IVA" value={totals.net} />
              <Amount label="IVA de compra" value={totals.tax} />
              <Amount label="Total factura" value={totals.total} />
              {(withholdingPreview.data?.lines ?? []).map(line => <Amount key={`${line.ruleId}-${line.ruleVersion}`} label={`${line.name} (${line.rate}%)`} value={-line.amount} />)}
              <Amount label="Total retenciones" value={-(withholdingPreview.data?.withholdingTotal ?? 0)} />
              <Amount label={draft.createsPayable ? "Neto por pagar" : "Total pagado"}
                value={draft.createsPayable ? (withholdingPreview.data?.netAmount ?? totals.total) : totals.total} strong />
            </> : <>
              <Amount label="Mercancía antes de IVA" value={totals.net * mainRate} />
              <Amount label="Costos capitalizados" value={additionalSummary.capitalized} />
              <Amount label="Valor que entra al inventario" value={mainInventoryCost + additionalSummary.capitalized} />
              {additionalSummary.expensed > 0 && <Amount label="Costos llevados al gasto" value={additionalSummary.expensed} />}
              <Amount label="IVA descontable separado" value={mainDeductibleVat + additionalSummary.deductibleVat} />
              <Amount label="Total bruto de documentos" value={totals.total * mainRate + additionalSummary.gross} />
              {(withholdingPreview.data?.lines ?? []).map(line => <Amount key={`${line.ruleId}-${line.ruleVersion}`} label={`${line.name} mercancía (${line.rate}%)`} value={-line.amount} />)}
              {additionalSummary.withheld > 0 && <Amount label="Retenciones otras facturas" value={-additionalSummary.withheld} />}
              <Amount label="Total retenciones" value={-totalWithheld} />
              <Amount label="Cuentas por pagar netas" value={totalPayable} strong />
            </>}
            <p className="pt-2 text-xs text-slate-300">Las retenciones reducen lo adeudado; no reducen el costo ni el IVA reconocido.</p>
            {withholdingPreview.isFetching && <p className="text-right text-xs text-slate-300">Calculando retenciones…</p>}
            {withholdingPreview.isError && <p className="text-right text-xs text-amber-300">No fue posible obtener la vista previa. La confirmación volverá a validar con el motor.</p>}
          </dl>
        </section>

        <section className="grid gap-3 rounded-2xl border p-4 md:grid-cols-2">
          <Field label="Concepto fiscal de la compra">
            <Select value={draft.withholdingConceptCode || "__none"} onValueChange={(value) => change({ withholdingConceptCode: value === "__none" ? "" : value })}>
              <SelectTrigger><SelectValue placeholder="Sin concepto" /></SelectTrigger>
              <SelectContent><SelectItem value="__none">Sin concepto</SelectItem>{(options.data?.withholdingConcepts ?? []).map((item) => <SelectItem key={item.code} value={item.code}>{item.label}</SelectItem>)}</SelectContent>
            </Select>
          </Field>
          <Field label="Jurisdicción para reteICA">
            <Input readOnly value={withholdingPreview.data?.lines.find(line => line.kind === "IndustryCommerce")?.jurisdictionCode ?? "Automática desde el perfil del proveedor"}/>
          </Field>
          <p className="text-xs text-muted-foreground md:col-span-2">El concepto identifica la naturaleza de la compra. La jurisdicción y las responsabilidades vienen del perfil del proveedor; el motor tributario calcula la vista previa y vuelve a validarla al confirmar.</p>
        </section>
      </div>

      <SupplierChangeConfirmationDialog
        open={!!pendingSupplierChange}
        supplierName={pendingSupplierChange?.supplier.displayName}
        productCount={draft.lines.length}
        documentName="recepción"
        onCancel={() => setPendingSupplierChange(undefined)}
        onConfirm={() => pendingSupplierChange && applySupplierChange(pendingSupplierChange.supplier)}
      />

      {pendingAssociation && <section className="mx-6 mb-4 space-y-4 rounded-2xl border border-teal-200 bg-teal-50/30 p-5">
          <div>
            <h3 className="font-semibold">Asociar producto al proveedor</h3>
            <p className="mt-1 text-sm text-muted-foreground">
              {pendingAssociation?.name} no pertenece todavía al catálogo de este proveedor.
              Confirma la relación para poder recibirlo ahora y encontrarlo directamente en futuras entradas.
            </p>
          </div>
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
          <div className="flex justify-end gap-2">
            <Button type="button" variant="outline"
              onClick={() => { setPendingAssociation(undefined); setSupplierProductCode(""); setPurchasePresentationName("Unidad"); setUnitsPerPresentation(1); }}>
              Cancelar
            </Button>
            <Button type="button" disabled={associateProduct.isPending} onClick={confirmAssociation}>
              Asociar y agregar
            </Button>
          </div>
      </section>}
      <DialogFooter className="border-t bg-background px-6 py-4 sm:justify-between">
        {draft.concurrencyToken
          ? <Button type="button" variant="ghost" className="text-destructive" disabled={remove.isPending} onClick={deleteDraft}>
              <Trash2 className="mr-2 h-4 w-4" /> Eliminar borrador
            </Button>
          : <Button type="button" variant="ghost" className="text-destructive" onClick={onClear}>
              Descartar captura
            </Button>}
        <div className="grid w-full gap-2 sm:flex sm:w-auto sm:justify-end">
          <Button type="button" variant="outline" onClick={onClose}>
            <X className="mr-2 h-4 w-4" /> Cerrar
          </Button>
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
    supplierInvoiceNumber: "", supplierInvoiceDate: todayInput(), purchaseEvidenceType: "",
    receivedAt: localDateTime(), createsPayable: true, dueDate: plusDaysFrom(todayInput(),30),
    notes: "", lines: [], concurrencyToken: null,
    withholdingConceptCode: "", withholdingJurisdictionCode: "", purchaseOrderId: "",
    currencyCode: "COP", exchangeRate: 1, exchangeRateDate: todayInput(),
    exchangeRateSource: "FunctionalCurrency", additionalCostDocuments: [],
  };
}

function fromDraft(draft: GoodsReceiptDraft): EditorDraft {
  return {
    draftId: draft.draftId, warehouseId: draft.warehouseId ?? "",
    supplierId: draft.supplierId ?? "", supplierInvoiceNumber: draft.supplierInvoiceNumber ?? "",
    supplierInvoiceDate: draft.supplierInvoiceDate?.slice(0, 10) ?? "",
    purchaseEvidenceType: draft.purchaseEvidenceType ?? "",
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
      totalGrossWeightKg: line.totalGrossWeightKg, totalVolumeM3: line.totalVolumeM3,
    })),
    withholdingConceptCode: "", withholdingJurisdictionCode: "",
    purchaseOrderId: draft.purchaseOrderId ?? "",
    concurrencyToken: draft.concurrencyToken,
    currencyCode: draft.currencyCode, exchangeRate: draft.exchangeRate ?? 1,
    exchangeRateDate: draft.exchangeRateDate ?? draft.supplierInvoiceDate?.slice(0, 10) ?? todayInput(),
    exchangeRateSource: draft.exchangeRateSource ?? "FunctionalCurrency",
    additionalCostDocuments: draft.additionalCostDocuments ?? [],
  };
}


function localDateTime(value = new Date().toISOString()) {
  const date = new Date(value);
  const offset = date.getTimezoneOffset() * 60_000;
  return new Date(date.getTime() - offset).toISOString().slice(0, 16);
}

function plusDaysFrom(value: string, days: number) {
  if (!value) return "";
  const [year, month, day] = value.split("-").map(Number);
  const date = new Date(year, month - 1, day);
  date.setDate(date.getDate() + days);
  const offset = date.getTimezoneOffset() * 60_000;
  return new Date(date.getTime() - offset).toISOString().slice(0, 10);
}

function todayInput() {
  const date = new Date();
  const offset = date.getTimezoneOffset() * 60_000;
  return new Date(date.getTime() - offset).toISOString().slice(0, 10);
}

function toIsoOrNull(value: string) { return value ? new Date(value).toISOString() : null; }
function serializeCostDocuments(documents: GoodsReceiptCostDocument[]) {
  return documents.map((document) => ({ ...document,
    issuedAt: toIsoOrNull(document.issuedAt)!,
    dueDate: document.createsPayable && document.dueDate ? toIsoOrNull(document.dueDate) : null,
  }));
}

function newCostLine(
  lineNumber: number,
  costKind: PurchaseCostKind,
  description: string,
  costTreatment: "Capitalize" | "Expense",
  allocationMethod: PurchaseCostAllocationMethod,
): GoodsReceiptCostLine {
  return {
    lineNumber, costKind, description, amount: 0, taxableBaseAmount: 0,
    taxCode: "00", taxRate: 0, taxAmount: 0, taxTreatment: "NotApplicable",
    costTreatment, allocationMethod,
  };
}

function allowedPurchaseEvidenceTypes(policy: string | null) {
  if (policy === "SupplierElectronicInvoice") return ["SupplierElectronicInvoice", "InternalReceiptVoucher", "ForeignCommercialInvoice"];
  if (policy === "BuyerElectronicSupportDocument") return ["BuyerElectronicSupportDocument", "InternalReceiptVoucher", "ForeignCommercialInvoice"];
  if (policy === "InternalReceiptVoucher") return ["InternalReceiptVoucher", "ForeignCommercialInvoice"];
  return ["SupplierElectronicInvoice", "BuyerElectronicSupportDocument", "InternalReceiptVoucher", "ForeignCommercialInvoice"];
}

function CostDocumentGrid({ title, documents, supplierNames, onOpen, onRemove }: {
  title: string; documents: GoodsReceiptCostDocument[]; supplierNames: Record<string, string>;
  onOpen: (document: GoodsReceiptCostDocument) => void; onRemove: (id: string) => void;
}) {
  return <section className="overflow-hidden rounded-xl border">
    <div className="flex items-center justify-between bg-muted/40 px-4 py-3">
      <h4 className="text-sm font-semibold">{title}</h4>
      <span className="text-xs text-muted-foreground">{documents.length} documento{documents.length === 1 ? "" : "s"}</span>
    </div>
    {documents.length === 0
      ? <p className="px-4 py-5 text-sm text-muted-foreground">No hay documentos agregados.</p>
      : <div className="overflow-x-auto"><table className="w-full min-w-[760px] text-sm">
          <thead className="border-t bg-muted/20 text-left text-xs text-muted-foreground">
            <tr><th className="px-4 py-2">Proveedor</th><th>Número</th><th>Vencimiento</th>
              <th className="text-right">Antes de IVA</th><th className="text-right">IVA</th>
              <th className="text-right">Total</th><th className="w-28" /></tr>
          </thead>
          <tbody>{documents.map((document) => {
            const subtotal = document.lines.reduce((sum, line) => sum + line.amount, 0);
            const tax = document.lines.reduce((sum, line) => sum + line.taxAmount, 0);
            return <tr key={document.costDocumentId} className="border-t">
              <td className="px-4 py-3 font-medium">{supplierNames[document.supplierId] ?? "Proveedor seleccionado"}</td>
              <td>{document.documentNumber || "Sin número"}</td><td>{document.dueDate?.slice(0, 10) || "Contado"}</td>
              <td className="text-right">{formatCurrency(subtotal)} {document.currencyCode}</td>
              <td className="text-right">{formatCurrency(tax)} {document.currencyCode}</td>
              <td className="text-right font-semibold">{formatCurrency(subtotal + tax)} {document.currencyCode}</td>
              <td className="px-2 text-right"><Button type="button" size="sm" variant="ghost" onClick={() => onOpen(document)}>Ver</Button>
                <Button type="button" size="icon" variant="ghost" aria-label={`Eliminar ${document.documentNumber || "documento"}`} onClick={() => onRemove(document.costDocumentId)}><Trash2 className="h-4 w-4" /></Button></td>
            </tr>;
          })}</tbody>
        </table></div>}
  </section>;
}

function Field({ label, children }: { label: React.ReactNode; children: React.ReactNode }) {
  return <div className="space-y-2"><Label>{label}</Label>{children}</div>;
}
function Amount({ label, value, strong = false }: { label: string; value: number; strong?: boolean }) {
  return <div className={`flex justify-between ${strong ? "border-t border-white/20 pt-3 text-lg" : "text-sm"}`}>
    <dt className="text-slate-300">{label}</dt><dd className="font-semibold">{formatCurrency(value)}</dd>
  </div>;
}
function DocumentTotal({ label, value, suffix, strong = false }: {
  label: string; value: number; suffix: string; strong?: boolean;
}) {
  return <div><p className="text-xs text-muted-foreground">{label}</p>
    <p className={strong ? "font-bold" : "font-semibold"}>{formatCurrency(value)} {suffix}</p></div>;
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
