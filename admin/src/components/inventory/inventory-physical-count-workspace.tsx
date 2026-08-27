"use client";

import {
  useDeferredValue,
  useEffect,
  useMemo,
  useRef,
  useState,
  type KeyboardEvent,
} from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  AlertTriangle,
  CheckCircle2,
  ClipboardCheck,
  FileText,
  Loader2,
  RefreshCw,
  Save,
  Search,
  Trash2,
} from "lucide-react";
import { toast } from "sonner";

import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { Textarea } from "@/components/ui/textarea";
import { ProductPicker } from "@/components/products/product-picker";
import { DataTablePagination } from "@/components/tables/data-table-pagination";
import {
  inventoryApi,
  type InventoryPhysicalCountDetail,
  type InventoryPhysicalCountDraft,
  type InventoryPhysicalCountDraftSummary,
  type InventoryProductItem,
  type InventoryReconciliationDetail,
  type InventoryReconciliationProduct,
} from "@/services/api/inventory";
import { formatCurrency, formatDateTime } from "@/lib/utils";

type Warehouse = { id: string; name: string };
type ReconciliationSection = "Counted" | "Uncounted";
type CountCaptureStage = "Count" | "Recount";
type CountCaptureLine = {
  productId: string;
  productCode: string;
  productName: string;
  unitCode: string;
  systemQuantity: number;
  count: string;
  recount: string;
};

const draftStatusLabels: Record<string, string> = {
  InProgress: "En progreso",
  Ready: "Listo para conciliar",
};
const fmt = (value: number) => new Intl.NumberFormat("es-CO", { maximumFractionDigits: 6 }).format(value);
const pickerId = "physical-count-product-search";
export type PhysicalCountDraftSelection = { count: InventoryPhysicalCountDetail; draftId: string };

export function InventoryPhysicalCountWorkspace({
  businessId,
  warehouseId,
  search,
  permissions,
  onEditDraft,
}: {
  businessId: string;
  warehouses: Warehouse[];
  warehouseId?: string;
  search?: string;
  permissions: Set<string>;
  onEditDraft: (value: PhysicalCountDraftSelection) => void;
}) {
  const client = useQueryClient();
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(20);
  const query = useQuery({
    queryKey: ["inventory-physical-count-drafts", businessId, warehouseId, search, page, pageSize],
    queryFn: () => inventoryApi.physicalCountDrafts({ warehouseId, search, page, pageSize }),
  });

  async function resume(countId: string, draftId: string) {
    try {
      const count = await client.fetchQuery({
        queryKey: ["inventory-physical-count", businessId, countId],
        queryFn: () => inventoryApi.physicalCount(countId),
      });
      onEditDraft({ count, draftId });
    } catch {
      toast.error("No fue posible recuperar el borrador.");
    }
  }

  return <div className="space-y-4">
    <div className="flex flex-wrap items-start justify-between gap-3">
      <div>
        <h2 className="text-xl font-semibold">Borradores de inventario</h2>
        <p className="text-sm text-muted-foreground">Edita un conteo guardado en la misma ventana de operación o selecciónalo para conciliar.</p>
      </div>
      <Button variant="outline" size="sm" onClick={() => void query.refetch()}>
        <RefreshCw className="mr-2 h-4 w-4" />Actualizar
      </Button>
    </div>
    <Card>
      <CardContent className="overflow-x-auto p-0">
        <table className="w-full min-w-[900px] text-sm">
          <thead className="border-b bg-muted/50 text-xs uppercase text-muted-foreground">
            <tr>{["Borrador", "Bodega", "Avance", "Propietario", "Actualizado", "Estado", ""].map(label => <th key={label} className="px-4 py-3 text-left">{label}</th>)}</tr>
          </thead>
          <tbody>
            {query.isLoading
              ? <EmptyRow columns={7} text="Cargando borradores…" />
              : (query.data?.items ?? []).length === 0
                ? <EmptyRow columns={7} text="No hay borradores para estos filtros." />
                : query.data?.items.map(draft => <tr key={draft.draftId} className="border-b last:border-0">
                  <td className="px-4 py-3 font-medium">{draft.name}</td>
                  <td className="px-4 py-3">{draft.warehouseName}</td>
                  <td className="px-4 py-3">{draft.countedProductCount}/{draft.productCount}</td>
                  <td className="px-4 py-3 font-mono text-xs">{draft.ownerUserId.slice(0, 8)}</td>
                  <td className="px-4 py-3">{formatDateTime(draft.updatedAt)}</td>
                  <td className="px-4 py-3"><StatusBadge status={draft.status} /></td>
                  <td className="px-4 py-3 text-right">
                    <Button size="sm" variant="outline" disabled={!permissions.has("inventory.physical-counts.capture")} onClick={() => void resume(draft.inventoryPhysicalCountId, draft.draftId)}>Editar</Button>
                  </td>
                </tr>)}
          </tbody>
        </table>
      </CardContent>
    </Card>
    <DataTablePagination
      pageIndex={Math.max(0, page - 1)}
      pageSize={pageSize}
      pageCount={query.data?.totalPages ?? 0}
      totalItems={query.data?.totalCount ?? 0}
      onPageChange={index => setPage(index + 1)}
      onPageSizeChange={size => { setPageSize(size); setPage(1); }}
    />
  </div>;
}

export function PhysicalCountCreationForm({
  businessId,
  warehouses,
  permissions,
  onCancel,
  onCompleted,
}: {
  businessId: string;
  warehouses: Warehouse[];
  permissions: Set<string>;
  onCancel: () => void;
  onCompleted: (destination: "documents" | "drafts") => void;
}) {
  const [warehouse, setWarehouse] = useState(warehouses.length === 1 ? warehouses[0].id : "");
  const [reason, setReason] = useState("");
  const [notes, setNotes] = useState("");
  const [lines, setLines] = useState<CountCaptureLine[]>([]);
  const [draftDialogOpen, setDraftDialogOpen] = useState(false);
  const [draftName, setDraftName] = useState("");
  const [captureStage, setCaptureStage] = useState<CountCaptureStage>("Count");
  const documentId = useRef(crypto.randomUUID());
  const countRefs = useRef(new Map<string, HTMLInputElement>());
  const recountRefs = useRef(new Map<string, HTMLInputElement>());
  const reasons = useQuery({
    queryKey: ["inventory-reasons", businessId, "StockCount"],
    queryFn: () => inventoryApi.reasons({ operationType: "StockCount" }),
  });
  const selectedIds = useMemo(() => new Set(lines.map(line => line.productId)), [lines]);
  const countsComplete = lines.length > 0 && lines.every(line => validNumber(line.count));
  const recountsComplete = lines.length > 0 && lines.every(line => validNumber(line.recount));
  const valid = Boolean(warehouse && reason && countsComplete);
  const applyValid = valid && (captureStage === "Count" || recountsComplete);
  const canSave = permissions.has("inventory.physical-counts.capture") && permissions.has("inventory.physical-counts.manage");
  const canApply = permissions.has("inventory.counts.confirm") && permissions.has("inventory.physical-counts.manage");

  function addProduct(product: InventoryProductItem) {
    setLines(current => current.some(line => line.productId === product.productId)
      ? current
      : [...current, fromProduct(product)]);
    window.requestAnimationFrame(() => {
      const input = countRefs.current.get(product.productId);
      input?.focus();
      input?.select();
    });
  }

  const saveDraft = useMutation({
    mutationFn: async () => {
      const created = await inventoryApi.createPhysicalCount({
        inventoryPhysicalCountId: crypto.randomUUID(),
        businessId,
        warehouseId: warehouse,
        scopeType: "General",
        reasonCode: reason,
        notes: notes.trim() || null,
        initialDraftName: draftName.trim(),
        productIds: [],
      });
      const draft = created.drafts[0];
      if (!draft) throw new Error("El inventario no creó el borrador inicial.");
      return inventoryApi.savePhysicalCountDraft(created.inventoryPhysicalCountId, draft.draftId, {
        businessId,
        version: draft.version,
        name: draftName.trim(),
        readyForReconciliation: captureStage === "Count" || recountsComplete,
        captureStage,
        lines: mergeDraftLines(draft, lines),
      });
    },
    onSuccess: () => {
      toast.success(captureStage === "Recount" && !recountsComplete ? "Borrador guardado para continuar el reconteo." : "Borrador guardado y listo para conciliar.");
      setDraftDialogOpen(false);
      onCompleted("drafts");
    },
    onError: (error: Error) => toast.error(error.message || "No fue posible guardar el borrador."),
  });

  const apply = useMutation({
    mutationFn: () => inventoryApi.applyCount({
      documentId: documentId.current,
      businessId,
      warehouseId: warehouse,
      occurredAt: new Date().toISOString(),
      reasonCode: reason,
      notes: notes.trim() || null,
      lines: lines.map(line => ({
        productId: line.productId,
        initialQuantity: numeric(line.count),
        countedQuantity: numeric(captureStage === "Recount" ? line.recount : line.count),
      })),
    }),
    onSuccess: value => {
      toast.success(`${value.documentNumber} fue enviado para aplicar.`);
      onCompleted("documents");
    },
    onError: (error: Error) => toast.error(error.message || "No fue posible aplicar el conteo."),
  });

  return <div className="space-y-5">
    <div className="rounded-2xl border border-emerald-200 bg-gradient-to-r from-emerald-50 to-white p-5">
      <h3 className="text-lg font-semibold text-emerald-950">Conteo físico</h3>
      <p className="mt-1 text-sm text-emerald-900">Busca cada producto, registra su conteo y aplica el inventario o guárdalo como borrador.</p>
    </div>
    <div className="grid gap-4 md:grid-cols-2">
      <Field label="Bodega">
        <Select value={warehouse} onValueChange={value => { setWarehouse(value); setLines([]); setCaptureStage("Count"); }}>
          <SelectTrigger><SelectValue placeholder="Selecciona una bodega" /></SelectTrigger>
          <SelectContent>{warehouses.map(item => <SelectItem key={item.id} value={item.id}>{item.name}</SelectItem>)}</SelectContent>
        </Select>
      </Field>
      <Field label="Motivo">
        <Select value={reason} onValueChange={setReason}>
          <SelectTrigger><SelectValue placeholder="Selecciona un motivo" /></SelectTrigger>
          <SelectContent>{reasons.data?.map(item => <SelectItem key={item.inventoryReasonId} value={item.code}>{item.name}</SelectItem>)}</SelectContent>
        </Select>
      </Field>
    </div>
    <Card className="overflow-visible">
      <CardHeader className="border-b bg-muted/20">
        <div className="flex flex-wrap items-center justify-between gap-2"><CardTitle className="text-base">Productos contados</CardTitle><Badge variant="outline">{captureStage === "Count" ? "Etapa: contar" : "Etapa: recontar"}</Badge></div>
        <p className="text-sm text-muted-foreground">{captureStage === "Count" ? "Solo Contar está activo. Al agregar un producto, el foco pasa a su cantidad." : "Solo Recontar está activo. Completa la segunda lectura antes de aplicar."}</p>
      </CardHeader>
      <CardContent className="space-y-4 pt-5">
        <ProductPicker
          businessId={businessId}
          warehouseId={warehouse}
          selectedProductIds={selectedIds}
          disabled={!warehouse || captureStage === "Recount" || apply.isPending || saveDraft.isPending}
          onSelect={addProduct}
          label="Buscar producto"
          inputId={pickerId}
        />
        <CountCaptureTable
          lines={lines}
          disabled={apply.isPending || saveDraft.isPending}
          captureStage={captureStage}
          countRefs={countRefs}
          recountRefs={recountRefs}
          onChange={setLines}
        />
      </CardContent>
    </Card>
    <Field label="Observaciones">
      <Textarea value={notes} onChange={event => setNotes(event.target.value)} maxLength={1000} placeholder="Opcional" />
    </Field>
    <div className="flex flex-wrap items-center justify-between gap-3 border-t pt-4">
      <p className="text-sm text-muted-foreground">{lines.length} {lines.length === 1 ? "producto" : "productos"} en el conteo</p>
      <div className="flex flex-wrap gap-2">
        <Button variant="outline" onClick={onCancel}>Cerrar</Button>
        <Button variant="outline" disabled={!valid || !canSave || saveDraft.isPending || apply.isPending} onClick={() => setDraftDialogOpen(true)}>
          <Save className="mr-2 h-4 w-4" />Guardar borrador
        </Button>
        {captureStage === "Count" && <Button variant="outline" disabled={!countsComplete || apply.isPending || saveDraft.isPending} onClick={() => { setCaptureStage("Recount"); window.requestAnimationFrame(() => window.requestAnimationFrame(() => { const input = recountRefs.current.get(lines.find(line => !validNumber(line.recount))?.productId ?? lines[0]?.productId); input?.focus(); input?.select(); })); }}><RefreshCw className="mr-2 h-4 w-4" />Recontar</Button>}
        <Button disabled={!applyValid || !canApply || apply.isPending || saveDraft.isPending} onClick={() => apply.mutate()}>
          {apply.isPending ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : <CheckCircle2 className="mr-2 h-4 w-4" />}
          Aplicar inventario
        </Button>
      </div>
    </div>
    <SaveDraftNameDialog
      open={draftDialogOpen}
      name={draftName}
      pending={saveDraft.isPending}
      onNameChange={setDraftName}
      onClose={() => setDraftDialogOpen(false)}
      onSave={() => saveDraft.mutate()}
    />
  </div>;
}

export function InventoryReconciliationDialog({
  open,
  businessId,
  warehouseId,
  onClose,
}: {
  open: boolean;
  businessId: string;
  warehouseId?: string;
  onClose: () => void;
}) {
  const client = useQueryClient();
  const [search, setSearch] = useState("");
  const deferredSearch = useDeferredValue(search.trim());
  const [fromDate, setFromDate] = useState("");
  const [toDate, setToDate] = useState("");
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(20);
  const [selectedDrafts, setSelectedDrafts] = useState<Map<string, InventoryPhysicalCountDraftSummary>>(new Map());
  const [result, setResult] = useState<InventoryReconciliationDetail>();
  const [section, setSection] = useState<ReconciliationSection>("Counted");
  const [valuation, setValuation] = useState<"cost" | "average">("cost");
  const [draftDialogOpen, setDraftDialogOpen] = useState(false);
  const [draftName, setDraftName] = useState("");
  const [confirmZero, setConfirmZero] = useState(false);
  const countId = selectedDrafts.values().next().value?.inventoryPhysicalCountId ?? "";
  const drafts = useQuery({
    queryKey: ["inventory-physical-count-drafts", businessId, warehouseId, deferredSearch, fromDate, toDate, page, pageSize, "reconcile"],
    queryFn: () => inventoryApi.physicalCountDrafts({
      warehouseId,
      search: deferredSearch || undefined,
      from: dayBoundary(fromDate),
      to: dayBoundary(toDate, true),
      page,
      pageSize,
    }),
    enabled: open,
  });
  const candidates = drafts.data?.items ?? [];

  useEffect(() => {
    if (!open) {
      setSearch("");
      setFromDate("");
      setToDate("");
      setPage(1);
      setSelectedDrafts(new Map());
      setResult(undefined);
      setDraftName("");
      setConfirmZero(false);
    }
  }, [open]);

  const prepare = useMutation({
    mutationFn: () => inventoryApi.prepareReconciliation(countId, {
      businessId,
      drafts: [...selectedDrafts.values()].map(draft => ({ draftId: draft.draftId, version: draft.version })),
    }),
    onSuccess: value => {
      setResult(value);
      setSection("Counted");
      toast.success("Borradores conciliados por producto.");
    },
    onError: (error: Error) => toast.error(error.message),
  });
  const save = useMutation({
    mutationFn: () => inventoryApi.saveReconciliationDraft(countId, result!.reconciliationId, {
      businessId,
      section,
      draftId: crypto.randomUUID(),
      name: draftName.trim(),
    }),
    onSuccess: async () => {
      toast.success(section === "Counted" ? "Consolidado guardado como borrador." : "No contados guardados para continuar.");
      setDraftDialogOpen(false);
      setDraftName("");
      await Promise.all([
        client.invalidateQueries({ queryKey: ["inventory-physical-count"] }),
        client.invalidateQueries({ queryKey: ["inventory-physical-count-drafts"] }),
      ]);
    },
    onError: (error: Error) => toast.error(error.message),
  });
  const apply = useMutation({
    mutationFn: () => inventoryApi.applyReconciliation(countId, result!.reconciliationId, { businessId, section }),
    onSuccess: async () => {
      toast.success(section === "Counted" ? "Conteo conciliado enviado para aplicar." : "Productos no contados enviados para aplicar en cero.");
      setResult(await inventoryApi.reconciliation(countId));
      await Promise.all([
        client.invalidateQueries({ queryKey: ["inventory-operations"] }),
        client.invalidateQueries({ queryKey: ["inventory-physical-counts"] }),
        client.invalidateQueries({ queryKey: ["inventory-physical-count-drafts"] }),
      ]);
    },
    onError: (error: Error) => toast.error(error.message),
  });
  const products = result?.products.filter(product => product.status === section) ?? [];
  const sectionApplied = section === "Counted" ? result?.countedApplicationStatus : result?.uncountedApplicationStatus;

  function selectDraft(draft: InventoryPhysicalCountDraftSummary, checked: boolean) {
    setSelectedDrafts(current => {
      const next = new Map(current);
      if (checked) {
        const selectedCountId = next.values().next().value?.inventoryPhysicalCountId;
        if (selectedCountId && selectedCountId !== draft.inventoryPhysicalCountId) return current;
        next.set(draft.draftId, draft);
      } else {
        next.delete(draft.draftId);
      }
      return next;
    });
  }

  return <Dialog open={open} onOpenChange={value => !value && onClose()}>
      <DialogContent className="flex max-h-[94dvh] max-w-7xl flex-col overflow-hidden p-0">
        <DialogHeader className="border-b bg-gradient-to-r from-slate-950 to-emerald-950 px-6 py-5 text-white">
          <DialogTitle className="text-white">Conciliación de inventario</DialogTitle>
          <DialogDescription className="text-slate-300">
            {result ? "Revisa los productos agrupados, sus borradores de origen y la acción para cada sección." : "Filtra y selecciona los borradores que deseas conciliar."}
          </DialogDescription>
        </DialogHeader>
        <div className="space-y-5 overflow-y-auto px-6 py-5">
          {!result ? <>
            <Card>
              <CardHeader className="border-b bg-muted/20">
                <CardTitle className="text-base">Borradores disponibles</CardTitle>
                <p className="text-sm text-muted-foreground">El primer borrador elegido fija el mismo inventario y bodega para toda la selección.</p>
              </CardHeader>
              <CardContent className="space-y-4 pt-5">
                <div className="grid gap-3 md:grid-cols-3">
                  <Field label="Nombre del borrador">
                    <div className="relative"><Search className="absolute left-3 top-3 h-4 w-4 text-muted-foreground" /><Input className="pl-9" value={search} onChange={event => { setSearch(event.target.value); setPage(1); }} placeholder="Buscar por nombre o producto" /></div>
                  </Field>
                  <Field label="Actualizado desde"><Input type="date" value={fromDate} max={toDate || undefined} onChange={event => { setFromDate(event.target.value); setPage(1); }} /></Field>
                  <Field label="Actualizado hasta"><Input type="date" value={toDate} min={fromDate || undefined} onChange={event => { setToDate(event.target.value); setPage(1); }} /></Field>
                </div>
                <div className="overflow-x-auto rounded-2xl border">
                  <table className="w-full min-w-[920px] text-sm">
                    <thead className="bg-muted/60 text-xs uppercase tracking-wide text-muted-foreground">
                      <tr><th className="w-14 px-4 py-3"><span className="sr-only">Seleccionar</span></th><th className="px-4 py-3 text-left">Borrador</th><th className="px-4 py-3 text-left">Bodega</th><th className="px-4 py-3 text-left">Avance</th><th className="px-4 py-3 text-left">Actualizado</th><th className="px-4 py-3 text-left">Estado</th></tr>
                    </thead>
                    <tbody>
                      {drafts.isLoading
                        ? <EmptyRow columns={6} text="Cargando borradores…" />
                        : candidates.length === 0
                          ? <EmptyRow columns={6} text="No hay borradores para los filtros seleccionados." />
                          : candidates.map(draft => {
                            const unavailable = draft.status !== "Ready" || Boolean(countId && countId !== draft.inventoryPhysicalCountId);
                            return <tr key={draft.draftId} className={`border-t transition-colors ${unavailable ? "opacity-55" : "hover:bg-emerald-50/40"}`}>
                              <td className="px-4 py-3 text-center"><input aria-label={`Seleccionar ${draft.name}`} type="checkbox" disabled={unavailable} checked={selectedDrafts.has(draft.draftId)} onChange={event => selectDraft(draft, event.target.checked)} /></td>
                              <td className="px-4 py-3"><strong>{draft.name}</strong><span className="block text-xs text-muted-foreground">Propietario {draft.ownerUserId.slice(0, 8)}</span></td>
                              <td className="px-4 py-3">{draft.warehouseName}</td>
                              <td className="px-4 py-3"><span className="font-medium">{draft.countedProductCount}/{draft.productCount}</span><span className="block text-xs text-muted-foreground">productos contados</span></td>
                              <td className="px-4 py-3">{formatDateTime(draft.updatedAt)}</td>
                              <td className="px-4 py-3"><StatusBadge status={draft.status} /></td>
                            </tr>;
                          })}
                    </tbody>
                  </table>
                </div>
                <DataTablePagination
                  pageIndex={Math.max(0, page - 1)}
                  pageSize={pageSize}
                  pageCount={drafts.data?.totalPages ?? 0}
                  totalItems={drafts.data?.totalCount ?? 0}
                  onPageChange={index => setPage(index + 1)}
                  onPageSizeChange={size => { setPageSize(size); setPage(1); }}
                />
              </CardContent>
            </Card>
            <div className="flex flex-wrap items-center justify-between gap-3">
              <p className="text-sm text-muted-foreground">{selectedDrafts.size} {selectedDrafts.size === 1 ? "borrador seleccionado" : "borradores seleccionados"}</p>
              <Button disabled={!countId || selectedDrafts.size === 0 || prepare.isPending} onClick={() => prepare.mutate()}>
                {prepare.isPending ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : <ClipboardCheck className="mr-2 h-4 w-4" />}
                Conciliar seleccionados
              </Button>
            </div>
          </> : <>
            <div className="flex flex-wrap items-center justify-between gap-3">
              <div>
                <p className="font-medium">{result.drafts.length} {result.drafts.length === 1 ? "borrador agrupado" : "borradores agrupados"}</p>
                <p className="text-sm text-muted-foreground">Cada producto conserva visibles todos los borradores que aportaron al total.</p>
              </div>
              <div className="flex flex-wrap gap-2">
                <Select value={valuation} onValueChange={value => setValuation(value as "cost" | "average")}>
                  <SelectTrigger className="w-48"><SelectValue /></SelectTrigger>
                  <SelectContent><SelectItem value="cost">Precio costo</SelectItem><SelectItem value="average">Costo promedio</SelectItem></SelectContent>
                </Select>
                <Button variant="outline" onClick={() => setResult(undefined)}>Cambiar selección</Button>
              </div>
            </div>
            <Tabs value={section} onValueChange={value => { setSection(value as ReconciliationSection); setConfirmZero(false); }}>
              <TabsList className="grid h-auto w-full grid-cols-2 rounded-2xl p-1">
                <TabsTrigger value="Counted" className="rounded-xl py-2.5">Contados · {result.products.filter(product => product.status === "Counted").length}</TabsTrigger>
                <TabsTrigger value="Uncounted" className="rounded-xl py-2.5">No contados · {result.products.filter(product => product.status === "Uncounted").length}</TabsTrigger>
              </TabsList>
              <TabsContent value="Counted"><ReconciliationTable products={products} valuation={valuation} /></TabsContent>
              <TabsContent value="Uncounted"><ReconciliationTable products={products} valuation={valuation} /></TabsContent>
            </Tabs>
            <Card className="border-emerald-200">
              <CardContent className="space-y-4 pt-6">
                <div className="flex flex-wrap items-center justify-between gap-3">
                  <div>
                    <p className="font-medium">{section === "Counted" ? "Productos contados" : "Productos no contados"}</p>
                    <p className="text-sm text-muted-foreground">{section === "Counted" ? "Guarda el consolidado o aplícalo al inventario." : "Guárdalos para contarlos después o aplícalos todos en cero."}</p>
                  </div>
                  {sectionApplied && <Badge variant="secondary">Aplicación: {sectionApplied}</Badge>}
                </div>
                {section === "Uncounted" && <label className="flex items-start gap-3 rounded-2xl border border-amber-300 bg-amber-50 p-4 text-sm text-amber-950">
                  <input className="mt-1" type="checkbox" checked={confirmZero} onChange={event => setConfirmZero(event.target.checked)} />
                  <span><strong>Confirmo que deseo ajustar a cero todos los productos no contados.</strong><span className="mt-1 block">La acción genera un documento normal de inventario y conserva su trazabilidad.</span></span>
                </label>}
                {draftDialogOpen && <div className="grid gap-3 rounded-2xl border border-emerald-200 bg-emerald-50/50 p-4 md:grid-cols-[1fr_auto] md:items-end">
                  <Field label={section === "Counted" ? "Nombre del consolidado" : "Nombre del borrador de no contados"}>
                    <Input autoFocus value={draftName} maxLength={120} onChange={event => setDraftName(event.target.value)} onKeyDown={event => { if (event.key === "Enter" && draftName.trim() && !save.isPending) { event.preventDefault(); save.mutate(); } }} placeholder="Escribe un nombre claro" />
                  </Field>
                  <div className="flex gap-2"><Button variant="outline" onClick={() => { setDraftDialogOpen(false); setDraftName(""); }}>Cancelar</Button><Button disabled={!draftName.trim() || save.isPending} onClick={() => save.mutate()}>{save.isPending && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}Guardar</Button></div>
                </div>}
                <div className="flex flex-wrap justify-end gap-2 border-t pt-4">
                  <Button variant="outline" disabled={products.length === 0 || save.isPending || Boolean(sectionApplied) || draftDialogOpen} onClick={() => setDraftDialogOpen(true)}>
                    <Save className="mr-2 h-4 w-4" />Guardar como borrador
                  </Button>
                  <Button variant={section === "Uncounted" ? "destructive" : "default"} disabled={products.length === 0 || apply.isPending || Boolean(sectionApplied) || (section === "Uncounted" && !confirmZero)} onClick={() => apply.mutate()}>
                    {apply.isPending ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : section === "Uncounted" ? <AlertTriangle className="mr-2 h-4 w-4" /> : <CheckCircle2 className="mr-2 h-4 w-4" />}
                    {section === "Uncounted" ? "Aplicar todos en cero" : "Aplicar contados"}
                  </Button>
                </div>
              </CardContent>
            </Card>
          </>}
        </div>
        <DialogFooter className="border-t px-6 py-4"><Button variant="outline" onClick={onClose}>Cerrar</Button></DialogFooter>
      </DialogContent>
    </Dialog>;
}

export function PhysicalCountDraftEditForm({
  value,
  businessId,
  permissions,
  onCancel,
  onCompleted,
}: {
  value: PhysicalCountDraftSelection;
  businessId: string;
  permissions: Set<string>;
  onCancel: () => void;
  onCompleted: (destination: "documents" | "drafts") => void;
}) {
  const draft = value?.count.drafts.find(item => item.draftId === value.draftId);
  const [lines, setLines] = useState<CountCaptureLine[]>([]);
  const [draftDialogOpen, setDraftDialogOpen] = useState(false);
  const [name, setName] = useState("");
  const [captureStage, setCaptureStage] = useState<CountCaptureStage>("Count");
  const countRefs = useRef(new Map<string, HTMLInputElement>());
  const recountRefs = useRef(new Map<string, HTMLInputElement>());
  const selectedIds = useMemo(() => new Set(lines.map(line => line.productId)), [lines]);
  const countsComplete = lines.length > 0 && lines.every(line => validNumber(line.count));
  const recountsComplete = lines.length > 0 && lines.every(line => validNumber(line.recount));
  const valid = countsComplete;
  const applyValid = valid && (captureStage === "Count" || recountsComplete);
  const canCapture = permissions.has("inventory.physical-counts.capture");
  const canApply = permissions.has("inventory.counts.confirm") && permissions.has("inventory.physical-counts.manage");

  useEffect(() => {
    if (!draft) return;
    const nextLines = draft.lines.filter(line => line.initialQuantity !== null).map(fromDraftLine);
    const nextStage: CountCaptureStage = draft.captureStage === "Recount" || nextLines.some(line => line.recount.trim()) ? "Recount" : "Count";
    setName(draft.name);
    setCaptureStage(nextStage);
    setLines(nextLines);
    window.requestAnimationFrame(() => window.requestAnimationFrame(() => {
      const pending = nextLines.find(line => !validNumber(nextStage === "Count" ? line.count : line.recount));
      const input = pending ? (nextStage === "Count" ? countRefs : recountRefs).current.get(pending.productId) : null;
      if (input) { input.focus(); input.select(); }
      else if (nextStage === "Count") document.getElementById(pickerId)?.focus();
    }));
  }, [draft]);

  function addProduct(product: InventoryProductItem) {
    if (!draft?.lines.some(line => line.productId === product.productId)) {
      toast.error("Este producto no pertenece al alcance original del borrador.");
      return;
    }
    setLines(current => current.some(line => line.productId === product.productId) ? current : [...current, fromProduct(product)]);
    window.requestAnimationFrame(() => {
      const input = countRefs.current.get(product.productId);
      input?.focus();
      input?.select();
    });
  }

  const save = useMutation({
    mutationFn: () => inventoryApi.savePhysicalCountDraft(value!.count.inventoryPhysicalCountId, draft!.draftId, {
      businessId,
      version: draft!.version,
      name: name.trim(),
      readyForReconciliation: captureStage === "Count" || recountsComplete,
      captureStage,
      lines: mergeDraftLines(draft!, lines),
    }),
    onSuccess: () => {
      toast.success(captureStage === "Recount" && !recountsComplete ? "Borrador guardado para continuar el reconteo." : "Borrador actualizado y listo para conciliar.");
      setDraftDialogOpen(false);
      onCompleted("drafts");
    },
    onError: (error: Error) => toast.error(error.message),
  });
  const apply = useMutation({
    mutationFn: async () => {
      const saved = await inventoryApi.savePhysicalCountDraft(value!.count.inventoryPhysicalCountId, draft!.draftId, {
        businessId,
        version: draft!.version,
        name: name.trim(),
        readyForReconciliation: captureStage === "Count" || recountsComplete,
        captureStage,
        lines: mergeDraftLines(draft!, lines),
      });
      const updated = saved.drafts.find(item => item.draftId === draft!.draftId);
      if (!updated) throw new Error("No fue posible recuperar la versión guardada del borrador.");
      const reconciliation = await inventoryApi.prepareReconciliation(saved.inventoryPhysicalCountId, {
        businessId,
        drafts: [{ draftId: updated.draftId, version: updated.version }],
      });
      return inventoryApi.applyReconciliation(saved.inventoryPhysicalCountId, reconciliation.reconciliationId, { businessId, section: "Counted" });
    },
    onSuccess: () => {
      toast.success("Conteo enviado para aplicar al inventario.");
      onCompleted("documents");
    },
    onError: (error: Error) => toast.error(error.message || "No fue posible aplicar el conteo."),
  });

  if (!value || !draft) return null;
  return <div className="space-y-5">
    <div className="rounded-2xl border border-emerald-200 bg-gradient-to-r from-emerald-50 to-white p-5">
      <h3 className="text-lg font-semibold text-emerald-950">Conteo físico · {value.count.warehouseName}</h3>
      <p className="mt-1 text-sm text-emerald-900">Editando <strong>{draft.name}</strong>, guardado {formatDateTime(draft.updatedAt)}. Los conteos y reconteos existentes permanecen cargados.</p>
    </div>
    <Card className="overflow-visible">
      <CardHeader className="border-b bg-muted/20">
        <CardTitle className="text-base">Productos contados</CardTitle>
        <p className="text-sm text-muted-foreground">Agrega productos del alcance o ajusta los valores existentes. Enter vuelve al buscador.</p>
      </CardHeader>
      <CardContent className="space-y-4 pt-5">
        <ProductPicker businessId={businessId} warehouseId={value.count.warehouseId} selectedProductIds={selectedIds} disabled={!canCapture || captureStage === "Recount" || save.isPending || apply.isPending} onSelect={addProduct} label="Buscar producto" inputId={pickerId} />
        <CountCaptureTable lines={lines} disabled={!canCapture || save.isPending || apply.isPending} captureStage={captureStage} countRefs={countRefs} recountRefs={recountRefs} onChange={setLines} />
      </CardContent>
    </Card>
    <div className="flex flex-wrap items-center justify-between gap-3 border-t pt-4">
      <p className="text-sm text-muted-foreground">{lines.length} {lines.length === 1 ? "producto" : "productos"} cargados del borrador</p>
      <div className="flex flex-wrap gap-2">
        <Button variant="outline" onClick={onCancel}>Cerrar</Button>
        {canCapture && <Button variant="outline" disabled={!valid || save.isPending || apply.isPending} onClick={() => setDraftDialogOpen(true)}><Save className="mr-2 h-4 w-4" />Guardar borrador</Button>}
        {captureStage === "Count" && <Button variant="outline" disabled={!countsComplete || save.isPending || apply.isPending} onClick={() => { setCaptureStage("Recount"); window.requestAnimationFrame(() => window.requestAnimationFrame(() => { const input = recountRefs.current.get(lines.find(line => !validNumber(line.recount))?.productId ?? lines[0]?.productId); input?.focus(); input?.select(); })); }}><RefreshCw className="mr-2 h-4 w-4" />Recontar</Button>}
        {canApply && <Button disabled={!applyValid || save.isPending || apply.isPending} onClick={() => apply.mutate()}>{apply.isPending ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : <CheckCircle2 className="mr-2 h-4 w-4" />}Aplicar inventario</Button>}
      </div>
    </div>
    <SaveDraftNameDialog open={draftDialogOpen} name={name} pending={save.isPending} onNameChange={setName} onClose={() => setDraftDialogOpen(false)} onSave={() => save.mutate()} />
  </div>;
}

function CountCaptureTable({
  lines,
  disabled,
  captureStage,
  countRefs,
  recountRefs,
  onChange,
}: {
  lines: CountCaptureLine[];
  disabled: boolean;
  captureStage: CountCaptureStage;
  countRefs: React.MutableRefObject<Map<string, HTMLInputElement>>;
  recountRefs: React.MutableRefObject<Map<string, HTMLInputElement>>;
  onChange: (lines: CountCaptureLine[]) => void;
}) {
  function update(productId: string, values: Partial<CountCaptureLine>) {
    onChange(lines.map(line => line.productId === productId ? { ...line, ...values } : line));
  }
  function advance(event: KeyboardEvent<HTMLInputElement>, productId: string) {
    if (event.key !== "Enter") return;
    event.preventDefault();
    if (captureStage === "Recount") {
      const currentIndex = lines.findIndex(line => line.productId === productId);
      const pending = lines.slice(currentIndex + 1).find(line => !validNumber(line.recount));
      const input = pending ? recountRefs.current.get(pending.productId) : undefined;
      input?.focus();
      input?.select();
      return;
    }
    const input = document.getElementById(pickerId) as HTMLInputElement | null;
    input?.focus();
    input?.select();
  }
  return <div className="overflow-x-auto rounded-2xl border">
    <table className="w-full min-w-[820px] text-sm">
      <thead className="bg-muted/60 text-xs uppercase tracking-wide text-muted-foreground">
        <tr><th className="px-4 py-3 text-left">Producto</th><th className="px-4 py-3 text-right">Existencia sistema</th><th className="px-4 py-3 text-right">Contar</th><th className="px-4 py-3 text-right">Recontar</th><th className="w-14" /></tr>
      </thead>
      <tbody>
        {lines.length === 0 ? <EmptyRow columns={5} text="Busca el primer producto para comenzar el conteo." /> : lines.map(line => <tr key={line.productId} className="border-t">
          <td className="px-4 py-3"><strong>{line.productName}</strong><span className="block text-xs text-muted-foreground">{line.productCode || "Sin código"} · {line.unitCode}</span></td>
          <td className="px-4 py-3 text-right tabular-nums text-muted-foreground">{fmt(line.systemQuantity)}</td>
          <td className="px-4 py-3"><Input ref={element => { if (element) countRefs.current.set(line.productId, element); else countRefs.current.delete(line.productId); }} aria-label={`Contar ${line.productName}`} className="ml-auto w-36 text-right tabular-nums" inputMode="decimal" min="0" disabled={disabled || captureStage !== "Count"} value={line.count} onFocus={event => event.currentTarget.select()} onKeyDown={event => advance(event, line.productId)} onChange={event => update(line.productId, { count: event.target.value })} /></td>
          <td className="px-4 py-3"><Input ref={element => { if (element) recountRefs.current.set(line.productId, element); else recountRefs.current.delete(line.productId); }} aria-label={`Recontar ${line.productName}`} className="ml-auto w-36 text-right tabular-nums" inputMode="decimal" min="0" disabled={disabled || captureStage !== "Recount"} value={line.recount} onFocus={event => event.currentTarget.select()} onKeyDown={event => advance(event, line.productId)} onChange={event => update(line.productId, { recount: event.target.value })} placeholder={captureStage === "Recount" ? "Pendiente" : "Opcional"} /></td>
          <td className="px-3 py-3"><Button type="button" size="icon" variant="ghost" disabled={disabled || captureStage === "Recount"} aria-label={`Quitar ${line.productName}`} onClick={() => onChange(lines.filter(item => item.productId !== line.productId))}><Trash2 className="h-4 w-4 text-destructive" /></Button></td>
        </tr>)}
      </tbody>
    </table>
  </div>;
}

function ReconciliationTable({ products, valuation }: { products: InventoryReconciliationProduct[]; valuation: "cost" | "average" }) {
  return <div className="mt-4 overflow-x-auto rounded-2xl border">
    <table className="w-full min-w-[1050px] text-sm">
      <thead className="bg-muted/60 text-xs uppercase tracking-wide text-muted-foreground">
        <tr><th className="px-4 py-3 text-left">Producto</th><th className="px-4 py-3 text-left">Borradores de origen</th><th className="px-4 py-3 text-right">Cantidad conciliada</th><th className="px-4 py-3 text-right">Existencia sistema</th><th className="px-4 py-3 text-right">Diferencia</th><th className="px-4 py-3 text-right">{valuation === "cost" ? "Precio costo" : "Costo promedio"}</th><th className="px-4 py-3 text-right">Valorización</th></tr>
      </thead>
      <tbody>
        {products.length === 0 ? <EmptyRow columns={7} text="No hay productos en esta sección." /> : products.map(product => {
          const cost = valuation === "cost" ? product.unitCost : product.averageUnitCost;
          const quantity = product.proposedQuantity ?? 0;
          return <tr key={product.productId} className="border-t align-top">
            <td className="px-4 py-4"><strong>{product.productName}</strong><span className="block text-xs text-muted-foreground">{product.productCode}</span></td>
            <td className="px-4 py-4">
              {product.sources.length === 0
                ? <span className="text-sm text-muted-foreground">No aparece en los borradores seleccionados.</span>
                : <div className="space-y-2"><span className="text-xs font-medium text-muted-foreground">{product.sources.length} {product.sources.length === 1 ? "borrador" : "borradores"}</span><div className="flex max-w-xl flex-wrap gap-2">{product.sources.map(source => <span key={source.draftId} className="inline-flex items-center gap-2 rounded-xl border border-emerald-200 bg-emerald-50 px-3 py-2 text-emerald-950"><FileText className="h-4 w-4 text-emerald-700" /><span><strong className="block text-xs">{source.draftName}</strong><small>{source.verificationQuantity === null ? "Contado" : "Recontado"}: {fmt(source.finalQuantity)}</small></span></span>)}</div></div>}
            </td>
            <td className="px-4 py-4 text-right font-semibold tabular-nums">{product.proposedQuantity === null ? "No contado" : fmt(product.proposedQuantity)}</td>
            <td className="px-4 py-4 text-right tabular-nums">{fmt(product.systemQuantity)}</td>
            <td className="px-4 py-4 text-right tabular-nums">{product.proposedQuantity === null ? "—" : fmt(product.proposedQuantity - product.systemQuantity)}</td>
            <td className="px-4 py-4 text-right tabular-nums">{cost === null ? "Restringido" : formatCurrency(cost)}</td>
            <td className="px-4 py-4 text-right font-medium tabular-nums">{cost === null ? "Restringido" : formatCurrency(quantity * cost)}</td>
          </tr>;
        })}
      </tbody>
    </table>
  </div>;
}

function SaveDraftNameDialog({
  open,
  name,
  pending,
  title = "Guardar borrador",
  onNameChange,
  onClose,
  onSave,
}: {
  open: boolean;
  name: string;
  pending: boolean;
  title?: string;
  onNameChange: (value: string) => void;
  onClose: () => void;
  onSave: () => void;
}) {
  return <Dialog open={open} onOpenChange={value => !value && onClose()}>
    <DialogContent className="max-w-md">
      <DialogHeader><DialogTitle>{title}</DialogTitle><DialogDescription>Asigna un nombre claro para encontrar este conteo después.</DialogDescription></DialogHeader>
      <Field label="Nombre del borrador"><Input autoFocus value={name} maxLength={120} onChange={event => onNameChange(event.target.value)} onKeyDown={event => { if (event.key === "Enter" && name.trim() && !pending) { event.preventDefault(); onSave(); } }} placeholder="Ej. Conteo pasillo principal" /></Field>
      <DialogFooter><Button variant="outline" onClick={onClose}>Cancelar</Button><Button disabled={!name.trim() || pending} onClick={onSave}>{pending && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}Guardar</Button></DialogFooter>
    </DialogContent>
  </Dialog>;
}

function mergeDraftLines(draft: InventoryPhysicalCountDraft, captures: CountCaptureLine[]) {
  const values = new Map(captures.map(line => [line.productId, line]));
  return draft.lines.map(line => {
    const captured = values.get(line.productId);
    return {
      productId: line.productId,
      initialQuantity: captured ? numeric(captured.count) : null,
      verificationQuantity: captured?.recount.trim() ? numeric(captured.recount) : null,
      pendingReason: null,
    };
  });
}

function fromProduct(product: InventoryProductItem): CountCaptureLine {
  return { productId: product.productId, productCode: product.productCode, productName: product.productName, unitCode: product.unitCode, systemQuantity: product.quantityOnHand, count: "", recount: "" };
}

function fromDraftLine(line: InventoryPhysicalCountDraft["lines"][number]): CountCaptureLine {
  return { productId: line.productId, productCode: line.productCode, productName: line.productName, unitCode: "", systemQuantity: line.systemQuantity, count: line.initialQuantity === null ? "" : String(line.initialQuantity), recount: line.verificationQuantity === null ? "" : String(line.verificationQuantity) };
}

function validNumber(value: string) {
  return value.trim() !== "" && Number.isFinite(Number(value)) && Number(value) >= 0;
}

function numeric(value: string) {
  return Number(value);
}

function dayBoundary(value: string, next = false) {
  if (!value) return undefined;
  const date = new Date(`${value}T00:00:00`);
  if (next) date.setDate(date.getDate() + 1);
  return date.toISOString();
}

function StatusBadge({ status }: { status: string }) {
  return <Badge variant={status === "Ready" ? "default" : "secondary"}>{draftStatusLabels[status] ?? status}</Badge>;
}

function EmptyRow({ columns, text }: { columns: number; text: string }) {
  return <tr><td colSpan={columns} className="p-10 text-center text-muted-foreground">{text}</td></tr>;
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return <label className="space-y-1.5 text-sm"><span className="font-medium">{label}</span>{children}</label>;
}
