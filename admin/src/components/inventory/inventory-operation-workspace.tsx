"use client";

import {
  useEffect,
  useMemo,
  useRef,
  useState,
  type KeyboardEvent,
  type UIEvent,
} from "react";
import {
  useInfiniteQuery,
  useMutation,
  useQuery,
  useQueryClient,
} from "@tanstack/react-query";
import {
  ArrowLeftRight,
  Barcode,
  Check,
  ClipboardCheck,
  Loader2,
  PackageX,
  Plus,
  RefreshCw,
  Scale,
  Trash2,
} from "lucide-react";
import { toast } from "sonner";

import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import {
  AdjustmentCaptureGrid,
  adjustmentUnitValue,
} from "@/components/inventory/adjustment-capture-grid";
import { Textarea } from "@/components/ui/textarea";
import {
  inventoryApi,
  type InventoryAcceptance,
  type InventoryProductItem,
} from "@/services/api/inventory";
import { productsApi } from "@/services/api/products";
import {
  inventoryDraftKey,
  loadInventoryOperationDraft,
  removeInventoryOperationDraft,
  saveInventoryOperationDraft,
} from "@/lib/operation-draft-store";

export type WarehouseOption = { id: string; name: string };
type OperationKind = "count" | "adjustment" | "transfer" | "conversion" | "damage";
type Direction = "INPUT" | "OUTPUT";
export type InventoryOperationLine = {
  productId: string;
  productCode: string;
  productName: string;
  unitCode: string;
  stock: number;
  quantity: string;
  preCount?: string;
  cost: string;
  direction: Direction;
  salePrice: string;
  systemQuantity: number | null;
  familyRootProductId?: string;
  conversionFactor?: number;
  maximumLossPercent?: number;
};
type Line = InventoryOperationLine;

const PRODUCT_PAGE_SIZE = 50;
const operationDocumentTypes: Record<OperationKind, string> = {
  count: "StockCount",
  adjustment: "InventoryAdjustment",
  transfer: "WarehouseTransfer",
  conversion: "ProductConversion",
  damage: "Damage",
};
const operationOptions: Array<{
  id: OperationKind;
  label: string;
  description: string;
  icon: typeof Scale;
  permission: string;
}> = [
  {
    id: "count",
    label: "Conteo físico",
    description: "Compara el conteo real con el saldo del sistema.",
    icon: ClipboardCheck,
    permission: "inventory.counts.confirm",
  },
  {
    id: "adjustment",
    label: "Ajuste",
    description: "Registra una entrada o salida justificada.",
    icon: Scale,
    permission: "inventory.adjustments.confirm",
  },
  {
    id: "transfer",
    label: "Traslado",
    description: "Mueve existencias entre bodegas.",
    icon: ArrowLeftRight,
    permission: "inventory.transfers.confirm",
  },
  {
    id: "conversion",
    label: "Conversión",
    description: "Transforma presentaciones conservando su valor.",
    icon: RefreshCw,
    permission: "inventory.conversions.confirm",
  },
  {
    id: "damage",
    label: "Avería",
    description: "Retira unidades dañadas o no vendibles.",
    icon: PackageX,
    permission: "inventory.damages.confirm",
  },
];

export function InventoryOperationWorkspace({
  businessId,
  warehouses,
  permissions,
}: {
  businessId: string;
  warehouses: WarehouseOption[];
  permissions: Set<string>;
}) {
  const queryClient = useQueryClient();
  const [kind, setKind] = useState<OperationKind>("count");
  const [warehouseId, setWarehouseId] = useState("");
  const [destinationId, setDestinationId] = useState("");
  const [reason, setReason] = useState("");
  const [notes, setNotes] = useState("");
  const [lines, setLines] = useState<Line[]>([]);
  const [valuationBasis, setValuationBasis] = useState<"Cost" | "SalePrice">("Cost");
  const [countDocumentId, setCountDocumentId] = useState<string | null>(null);
  const [conversionType, setConversionType] = useState<"SPLIT" | "MERGE">("SPLIT");
  const [documentId, setDocumentId] = useState(() => crypto.randomUUID());
  const [hydratedKey, setHydratedKey] = useState<string | null>(null);

  const selected = operationOptions.find((option) => option.id === kind)!;
  const allowed = permissions.has(selected.permission);
  const reasonsQuery = useQuery({
    queryKey: ["inventory-reasons", businessId, operationDocumentTypes[kind]],
    queryFn: () => inventoryApi.reasons({ operationType: operationDocumentTypes[kind] }),
    enabled: Boolean(businessId),
  });
  const reasons = useMemo(() => reasonsQuery.data ?? [], [reasonsQuery.data]);
  const selectedProductIds = useMemo(
    () => new Set(lines.map((line) => line.productId)),
    [lines],
  );
  const initialWarehouseId = warehouses.length === 1 ? warehouses[0].id : "";
  const warehouseIdsKey = warehouses.map((warehouse) => warehouse.id).join("|");
  const draftKey = inventoryDraftKey(businessId, kind);

  useEffect(() => {
    let active = true;
    setHydratedKey(null);
    void loadInventoryOperationDraft(draftKey)
      .then((draft) => {
        if (!active) return;
        if (draft) {
          setDocumentId(draft.documentId);
          setWarehouseId(draft.warehouseId);
          setDestinationId(draft.destinationId);
          setReason(draft.reason);
          setNotes(draft.notes);
          setCountDocumentId(draft.countDocumentId);
          setConversionType(draft.conversionType);
          setValuationBasis(draft.valuationBasis ?? "Cost");
          setLines(draft.lines.map((line) => ({ ...line, salePrice: line.salePrice ?? "" })));
        } else {
          setDocumentId(crypto.randomUUID());
          setWarehouseId(initialWarehouseId);
          setDestinationId("");
          setReason("");
          setNotes("");
          setCountDocumentId(null);
          setValuationBasis("Cost");
          setConversionType("SPLIT");
          setLines([]);
        }
        setHydratedKey(draftKey);
      })
      .catch(() => {
        if (!active) return;
        setHydratedKey(draftKey);
        toast.error("No fue posible recuperar el borrador local de inventario.");
      });
    return () => { active = false; };
  }, [draftKey, initialWarehouseId, warehouseIdsKey]);

  useEffect(() => {
    if (hydratedKey !== draftKey) return;
    const timer = window.setTimeout(() => {
      if (lines.length === 0 && notes.trim() === "" && !countDocumentId) {
        void removeInventoryOperationDraft(draftKey);
        return;
      }
      void saveInventoryOperationDraft({
        key: draftKey,
        businessId,
        kind,
        documentId,
        warehouseId,
        destinationId,
        reason,
        notes,
        countDocumentId,
        conversionType,
        valuationBasis,
        lines,
        updatedAt: new Date().toISOString(),
      }).catch(() => toast.error("No fue posible guardar el avance local."));
    }, 120);
    return () => window.clearTimeout(timer);
  }, [
    businessId,
    conversionType,
    countDocumentId,
    destinationId,
    valuationBasis,
    documentId,
    draftKey,
    hydratedKey,
    kind,
    lines,
    notes,
    reason,
    warehouseId,
  ]);

  useEffect(() => {
    if (!reasons.length) return;
    setReason((current) => reasons.some((item) => item.code === current) ? current : reasons[0].code);
  }, [reasons]);

  function focusQuantity(index: number) {
    window.requestAnimationFrame(() => {
      const input = document.querySelector<HTMLInputElement>(
        `[data-inventory-row="${index}"]`,
      );
      input?.focus();
      input?.select();
      input?.scrollIntoView({ block: "nearest" });
    });
  }

  function focusSearch() {
    window.requestAnimationFrame(() =>
      document.querySelector<HTMLInputElement>("#inventory-product-search")?.focus(),
    );
  }

  function addProduct(product: InventoryProductItem) {
    if (countDocumentId) return;
    const existing = lines.findIndex((line) => line.productId === product.productId);
    if (existing >= 0) {
      focusQuantity(existing);
      return;
    }

    const nextIndex = lines.length;
    setLines((current) => [
      ...current,
      {
        productId: product.productId,
        productCode: product.productCode,
        productName: product.productName,
        unitCode: product.unitCode,
        stock: product.quantityOnHand,
        quantity: "",
        preCount: "",
        cost: product.averageUnitCost?.toString() ?? "",
        salePrice: product.saleUnitPrice?.toString() ?? "",
        direction: kind === "conversion" && current.length > 0 ? "OUTPUT" : "INPUT",
        systemQuantity: null,
        familyRootProductId: product.familyRootProductId,
        conversionFactor: product.conversionFactor,
        maximumLossPercent: product.maximumLossPercent,
      },
    ]);
    window.requestAnimationFrame(() =>
      window.requestAnimationFrame(() => focusQuantity(nextIndex)),
    );
  }

  function gridKey(event: KeyboardEvent<HTMLInputElement>, index: number) {
    if (event.key === "ArrowDown") {
      event.preventDefault();
      focusQuantity(Math.min(index + 1, lines.length - 1));
    } else if (event.key === "ArrowUp") {
      event.preventDefault();
      focusQuantity(Math.max(index - 1, 0));
    } else if (event.key === "Enter") {
      event.preventDefault();
      if (index < lines.length - 1) focusQuantity(index + 1);
      else focusSearch();
    } else if (event.key === "Escape") {
      event.preventDefault();
      focusSearch();
    }
  }

  function update(index: number, patch: Partial<Line>) {
    setLines((current) =>
      current.map((line, currentIndex) =>
        currentIndex === index ? { ...line, ...patch } : line,
      ),
    );
  }

  function clear() {
    void removeInventoryOperationDraft(draftKey);
    setDocumentId(crypto.randomUUID());
    setLines([]);
    setCountDocumentId(null);
    setNotes("");
    focusSearch();
  }

  const mutation = useMutation({
    mutationFn: async () => {
      const now = new Date().toISOString();
      if (kind === "count" && !countDocumentId) {
        const draft = await inventoryApi.startCount({
          documentId,
          businessId,
          warehouseId,
          occurredAt: now,
          reasonCode: reason.trim(),
          notes: notes.trim() || null,
          lines: lines.map((line) => ({
            productId: line.productId,
            preCountQuantity: Number(line.quantity),
          })),
        });
        setCountDocumentId(documentId);
        setLines((current) =>
          current.map((line) => ({
            ...line,
            preCount: String(
              draft.lines.find((candidate) => candidate.productId === line.productId)
                ?.preCountQuantity ?? Number(line.quantity),
            ),
            quantity: "",
            systemQuantity:
              draft.lines.find((candidate) => candidate.productId === line.productId)
                ?.systemQuantityAtBase ?? 0,
          })),
        );
        return null;
      }

      let result: InventoryAcceptance;
      if (kind === "count") {
        result = await inventoryApi.confirmCount(countDocumentId!, {
          businessId,
          lines: lines.map((line, index) => ({
            lineNumber: index + 1,
            productId: line.productId,
            countedQuantity: Number(line.quantity),
          })),
        });
      } else if (kind === "adjustment") {
        result = await inventoryApi.confirmAdjustment({
          documentId,
          businessId,
          warehouseId,
          occurredAt: now,
          reasonCode: reason.trim(),
          costCenterId: null,
          notes: notes.trim() || null,
          lines: lines.map((line, index) => ({
            lineNumber: index + 1,
            productId: line.productId,
            quantityChange: Number(line.quantity),
            explicitUnitCost: Number(line.quantity) > 0 ? adjustmentUnitValue(line, valuationBasis) : null,
          })),
        });
      } else if (kind === "transfer") {
        result = await inventoryApi.confirmTransfer({
          documentId,
          businessId,
          sourceWarehouseId: warehouseId,
          destinationWarehouseId: destinationId,
          occurredAt: now,
          reasonCode: reason.trim(),
          notes: notes.trim() || null,
          lines: lines.map((line, index) => ({
            lineNumber: index + 1,
            productId: line.productId,
            quantity: Number(line.quantity),
          })),
        });
      } else if (kind === "conversion") {
        result = await inventoryApi.confirmConversion({
          documentId,
          businessId,
          warehouseId,
          occurredAt: now,
          conversionType,
          reasonCode: reason.trim(),
          costCenterId: null,
          notes: notes.trim() || null,
          lines: lines.map((line, index) => ({
            lineNumber: index + 1,
            direction: line.direction,
            productId: line.productId,
            quantity: Number(line.quantity),
            allocationWeight: null,
          })),
        });
      } else {
        result = await inventoryApi.confirmDamage({
          documentId,
          businessId,
          warehouseId,
          occurredAt: now,
          reasonCode: reason.trim(),
          costCenterId: null,
          notes: notes.trim() || null,
          lines: lines.map((line, index) => ({
            lineNumber: index + 1,
            productId: line.productId,
            quantity: Number(line.quantity),
          })),
        });
      }
      return result;
    },
    onSuccess: async (result) => {
      if (!result) {
        toast.success("Preconteo guardado. Ingresa ahora la cantidad contada.");
        focusQuantity(0);
        return;
      }
      toast.success(`${result.documentNumber} fue enviado al motor`);
      clear();
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ["inventory-balances"] }),
        queryClient.invalidateQueries({ queryKey: ["inventory-movements"] }),
        queryClient.invalidateQueries({ queryKey: ["inventory-operations"] }),
        queryClient.invalidateQueries({ queryKey: ["inventory-conversions"] }),
        queryClient.invalidateQueries({ queryKey: ["inventory-operation-products"] }),
        queryClient.invalidateQueries({ queryKey: ["inventory-product-picker"] }),
      ]);
    },
    onError: (error: { message?: string }) =>
      toast.error(error.message ?? "No fue posible confirmar la operación"),
  });

  const quantitiesValid =
    lines.length > 0 &&
    lines.every((line) => {
      const value = Number(line.quantity);
      return (
        line.quantity !== "" &&
        Number.isFinite(value) &&
        (kind === "adjustment" ? value !== 0 : value >= 0) &&
        (kind === "count" || value > 0)
      );
    });
  const conversionValid =
    kind !== "conversion" ||
    (lines.some((line) => line.direction === "INPUT") &&
      lines.some((line) => line.direction === "OUTPUT") &&
      lines.every((line) => line.familyRootProductId === lines[0]?.familyRootProductId && (line.conversionFactor ?? 0) > 0) &&
      (conversionType === "SPLIT"
        ? lines.filter((line) => line.direction === "INPUT").length === 1
        : lines.filter((line) => line.direction === "OUTPUT").length === 1));
  const conversionInputEquivalent = conversionQuantity(lines.filter((line) => line.direction === "INPUT").reduce((total, line) => total + conversionQuantity(Number(line.quantity || 0) * (line.conversionFactor ?? 0)), 0));
  const conversionOutputEquivalent = conversionQuantity(lines.filter((line) => line.direction === "OUTPUT").reduce((total, line) => total + conversionQuantity(Number(line.quantity || 0) * (line.conversionFactor ?? 0)), 0));
  const conversionLoss = conversionQuantity(Math.max(0, conversionInputEquivalent - conversionOutputEquivalent));
  const conversionLossPercent = conversionInputEquivalent > 0 ? conversionQuantity(conversionLoss / conversionInputEquivalent * 100) : 0;
  const conversionMaximumLossPercent = lines[0]?.maximumLossPercent ?? 0;
  const conversionInventoryValid = kind !== "conversion" || (
    conversionOutputEquivalent <= conversionInputEquivalent &&
    conversionLossPercent <= conversionMaximumLossPercent &&
    lines.filter((line) => line.direction === "INPUT").every((line) => Number(line.quantity || 0) <= line.stock)
  );
  const adjustmentValuationValid =
    kind !== "adjustment" ||
    lines.every((line) =>
      Number(line.quantity) <= 0 || adjustmentUnitValue(line, valuationBasis) > 0,
    );
  const ready =
    allowed &&
    Boolean(warehouseId) &&
    Boolean(reason.trim()) &&
    lines.length > 0 &&
    quantitiesValid && conversionValid && conversionInventoryValid && adjustmentValuationValid &&
    (kind !== "transfer" ||
      (Boolean(destinationId) && destinationId !== warehouseId));

  return (
    <div className="space-y-4">
      <div className="grid gap-3 md:grid-cols-5">
        {operationOptions.map((option) => {
          const Icon = option.icon;
          const enabled = permissions.has(option.permission);
          return (
            <button
              key={option.id}
              type="button"
              disabled={!enabled || mutation.isPending}
              onClick={() => setKind(option.id)}
              className={`rounded-xl border p-4 text-left transition ${
                kind === option.id
                  ? "border-emerald-500 bg-emerald-50 ring-1 ring-emerald-400"
                  : "bg-card hover:border-emerald-300"
              } disabled:cursor-not-allowed disabled:opacity-45`}
            >
              <Icon className="mb-3 h-5 w-5 text-emerald-700" />
              <strong className="block text-sm">{option.label}</strong>
              <span className="mt-1 block text-xs text-muted-foreground">
                {option.description}
              </span>
            </button>
          );
        })}
      </div>

      <Card>
        <CardHeader><CardTitle>{selected.label}</CardTitle></CardHeader>
        <CardContent className="space-y-4">
          <div className="grid gap-4 md:grid-cols-3">
            <Field label={kind === "transfer" ? "Bodega de origen" : "Bodega"}>
              <Select
                value={warehouseId}
                disabled={mutation.isPending || Boolean(countDocumentId)}
                onValueChange={(value) => {
                  setWarehouseId(value);
                  setLines([]);
                  setCountDocumentId(null);
                }}
              >
                <SelectTrigger><SelectValue placeholder="Selecciona una bodega" /></SelectTrigger>
                <SelectContent>
                  {warehouses.map((warehouse) => (
                    <SelectItem key={warehouse.id} value={warehouse.id}>{warehouse.name}</SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </Field>
            {kind === "transfer" && (
              <Field label="Bodega de destino">
                <Select value={destinationId} onValueChange={setDestinationId}>
                  <SelectTrigger><SelectValue placeholder="Selecciona el destino" /></SelectTrigger>
                  <SelectContent>
                    {warehouses.filter((warehouse) => warehouse.id !== warehouseId).map((warehouse) => (
                      <SelectItem key={warehouse.id} value={warehouse.id}>{warehouse.name}</SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </Field>
            )}
            {kind === "conversion" && (
              <Field label="Tipo de conversión">
                <Select value={conversionType} onValueChange={(value) => setConversionType(value as "SPLIT" | "MERGE")}>
                  <SelectTrigger><SelectValue /></SelectTrigger>
                  <SelectContent>
                    <SelectItem value="SPLIT">Un producto en varias salidas</SelectItem>

                    <SelectItem value="MERGE">Varios productos en una salida</SelectItem>
                  </SelectContent>
                </Select>
              </Field>
            )}
            {kind === "adjustment" && (
              <Field label="Valorar entradas con">
                <Select value={valuationBasis} onValueChange={(value) => setValuationBasis(value as "Cost" | "SalePrice")}>
                  <SelectTrigger><SelectValue /></SelectTrigger>
                  <SelectContent>
                    <SelectItem value="Cost">Precio costo</SelectItem>
                    <SelectItem value="SalePrice">Precio de venta</SelectItem>
                  </SelectContent>
                </Select>
                <p className="text-xs text-muted-foreground">El valor unitario se calcula desde el producto. Las salidas conservan el costo promedio.</p>
              </Field>
            )}
            <Field label="Motivo">
              <Select value={reason} disabled={mutation.isPending || Boolean(countDocumentId) || reasonsQuery.isLoading || reasons.length === 0} onValueChange={setReason}>
                <SelectTrigger data-testid="inventory-reason-select"><SelectValue placeholder={reasonsQuery.isLoading ? "Cargando motivos..." : "Selecciona un motivo"} /></SelectTrigger>
                <SelectContent>
                  {reasons.map((item) => <SelectItem key={item.inventoryReasonId} value={item.code}>{item.name}</SelectItem>)}
                </SelectContent>
              </Select>
              {reasonsQuery.isError && <p className="text-xs text-red-700">No fue posible cargar los motivos.</p>}
            </Field>
          </div>

          {!countDocumentId && (
            <InventoryProductPicker
              businessId={businessId}
              warehouseId={warehouseId}
              selectedProductIds={selectedProductIds}
              disabled={!warehouseId || mutation.isPending}
              conversionOnly={kind === "conversion"}
              conversionFamilyRootProductId={kind === "conversion" ? lines[0]?.familyRootProductId : undefined}
              onSelect={addProduct}
            />
          )}

          <div className="[&_strong]:font-normal">
          {kind === "adjustment" ? <AdjustmentCaptureGrid lines={lines} valuationBasis={valuationBasis} update={update} remove={(index) => setLines((current) => current.filter((_, currentIndex) => currentIndex !== index))} onKey={gridKey} /> : (
          <CaptureGrid
            kind={kind}
            prepared={Boolean(countDocumentId)}
            lines={lines}
            update={update}
            remove={(index) => {
              setLines((current) => current.filter((_, currentIndex) => currentIndex !== index));
              window.requestAnimationFrame(() => focusQuantity(Math.min(index, lines.length - 2)));
            }}
            onKey={gridKey}
          />
          )}
          </div>

          {kind === "conversion" && lines.length > 0 && <div className={`grid gap-3 rounded-xl border p-4 sm:grid-cols-4 ${conversionInventoryValid ? "border-emerald-200 bg-emerald-50/50" : "border-red-200 bg-red-50/60"}`}>
            <ConversionMetric label="Entrada equivalente" value={conversionInputEquivalent} />
            <ConversionMetric label="Salida equivalente" value={conversionOutputEquivalent} />
            <ConversionMetric label="Merma" value={conversionLoss} detail={`${conversionLossPercent.toFixed(3)} %`} />
            <ConversionMetric label="Máximo permitido" value={conversionMaximumLossPercent} detail="%" />
            {!conversionInventoryValid && <p className="text-sm text-red-700 sm:col-span-4">La salida no puede superar la entrada, la merma debe quedar dentro del máximo configurado y cada consumo debe tener existencia suficiente.</p>}
          </div>}

          <Field label="Observaciones">
            <Textarea value={notes} onChange={(event) => setNotes(event.target.value)} maxLength={1000} />
          </Field>
          <div className="flex flex-wrap items-center gap-3">
            <Button disabled={!ready || mutation.isPending} onClick={() => mutation.mutate()}>
              {mutation.isPending
                ? "Procesando…"
                : kind === "count" && !countDocumentId
                  ? "Preparar conteo"
                  : `Confirmar ${selected.label.toLowerCase()}`}
            </Button>
            <Button type="button" variant="outline" disabled={mutation.isPending || lines.length === 0} onClick={clear}>Limpiar</Button>
            {!allowed && <span className="text-sm text-amber-700">No tienes permiso para confirmar esta operación.</span>}
            {countDocumentId && <span className="text-sm text-muted-foreground">El preconteo quedó congelado; confirma las cantidades contadas para aplicar las diferencias.</span>}
          </div>
        </CardContent>
      </Card>
    </div>
  );
}

export function InventoryProductPicker({
  businessId,
  warehouseId,
  selectedProductIds,
  disabled,
  onSelect,
  label = "Agregar productos",
  conversionOnly = false,
  conversionFamilyRootProductId,
}: {
  businessId: string;
  warehouseId?: string;
  selectedProductIds: ReadonlySet<string>;
  disabled: boolean;
  onSelect: (product: InventoryProductItem) => void;
  label?: string;
  conversionOnly?: boolean;
  conversionFamilyRootProductId?: string;
}) {
  const [search, setSearch] = useState("");
  const [open, setOpen] = useState(false);
  const [activeIndex, setActiveIndex] = useState(0);
  const listRef = useRef<HTMLDivElement>(null);
  const pickerRef = useRef<HTMLDivElement>(null);

  const query = useInfiniteQuery({
    queryKey: ["inventory-product-picker", businessId, warehouseId ?? "catalog", conversionOnly, conversionFamilyRootProductId ?? "all-families", search.trim()],
    queryFn: async ({ pageParam }) => {
      if (warehouseId && conversionOnly) return inventoryApi.conversionProducts({ warehouseId, familyRootProductId: conversionFamilyRootProductId, search: search.trim() || undefined, page: pageParam, pageSize: PRODUCT_PAGE_SIZE });
      if (warehouseId) return inventoryApi.products({ warehouseId, search: search.trim() || undefined, page: pageParam, pageSize: PRODUCT_PAGE_SIZE });
      const page = await productsApi.list(businessId, { page: pageParam, pageSize: PRODUCT_PAGE_SIZE, search: search.trim() || undefined, includeInactive: false });
      return { ...page, items: page.items.map((product) => ({ productId: product.productId, productCode: product.productCode ?? product.sku ?? "", reference: product.reference ?? null, productName: product.name, unitCode: "EA", quantityOnHand: product.stockQuantity ?? 0, averageUnitCost: null, saleUnitPrice: product.unitPrice })) };
    },
    initialPageParam: 1,
    getNextPageParam: (lastPage) => lastPage.page < lastPage.totalPages ? lastPage.page + 1 : undefined,
    enabled: Boolean(businessId) && open && !disabled,
  });
  const products = useMemo(() => query.data?.pages.flatMap((page) => page.items) ?? [], [query.data]);
  const totalCount = query.data?.pages[0]?.totalCount ?? 0;

  useEffect(() => { setActiveIndex(0); listRef.current?.scrollTo({ top: 0 }); }, [search, warehouseId, conversionOnly, conversionFamilyRootProductId]);
  useEffect(() => {
    if (!open) return;
    const closeOnOutsidePointer = (event: PointerEvent) => {
      if (!pickerRef.current?.contains(event.target as Node)) setOpen(false);
    };
    document.addEventListener("pointerdown", closeOnOutsidePointer, true);
    return () => document.removeEventListener("pointerdown", closeOnOutsidePointer, true);
  }, [open]);

  useEffect(() => { if (activeIndex >= products.length) setActiveIndex(Math.max(0, products.length - 1)); }, [activeIndex, products.length]);

  function choose(product: InventoryProductItem) { onSelect(product); setSearch(""); setOpen(false); }
  async function chooseActive() {
    let product: InventoryProductItem | undefined = products[activeIndex];
    if (!product && !query.isFetching) {
      const refreshed = await query.refetch();
      product = refreshed.data?.pages.flatMap((page) => page.items)[0];
    }
    if (product) choose(product);
  }
  async function moveDown() {
    if (activeIndex < products.length - 1) { setActiveIndex((current) => current + 1); return; }
    if (!query.hasNextPage || query.isFetchingNextPage) return;
    const previousLength = products.length;
    const next = await query.fetchNextPage();
    const nextLength = next.data?.pages.flatMap((page) => page.items).length ?? previousLength;
    if (nextLength > previousLength) setActiveIndex(previousLength);
  }
  function keyDown(event: KeyboardEvent<HTMLInputElement>) {
    if (event.key === "ArrowDown") { event.preventDefault(); setOpen(true); void moveDown(); }
    else if (event.key === "ArrowUp") { event.preventDefault(); setActiveIndex((current) => Math.max(0, current - 1)); }
    else if (event.key === "Enter") { event.preventDefault(); void chooseActive(); }
    else if (event.key === "Escape") { event.preventDefault(); setOpen(false); }
  }
  function scroll(event: UIEvent<HTMLDivElement>) {
    const target = event.currentTarget;
    if (target.scrollHeight - target.scrollTop - target.clientHeight < 80 && query.hasNextPage && !query.isFetchingNextPage) void query.fetchNextPage();
  }

  return (
    <div ref={pickerRef} onBlur={(event) => { if (!event.currentTarget.contains(event.relatedTarget as Node | null)) setOpen(false); }} className="relative [&_strong]:font-normal">
      <Label htmlFor="inventory-product-search">{label}</Label>
      <div className="mt-2 flex gap-2">
        <div className="relative min-w-0 flex-1">
          <Barcode className="pointer-events-none absolute left-3 top-3 h-4 w-4 text-primary" />
          <Input id="inventory-product-search" data-testid="inventory-product-search" className="pl-9" disabled={disabled} value={search} onFocus={() => setOpen(true)} onClick={() => setOpen(true)} onChange={(event) => { setSearch(event.target.value); setOpen(true); }} onKeyDown={keyDown} autoComplete="off" aria-autocomplete="list" aria-expanded={open} aria-controls="inventory-product-results" placeholder="Código interno, código de barras, referencia o nombre" />
        </div>
        <Button type="button" disabled={disabled || query.isFetching || products.length === 0} onMouseDown={(event) => event.preventDefault()} onClick={() => void chooseActive()}>
          {query.isFetching && !query.isFetchingNextPage ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : <Plus className="mr-2 h-4 w-4" />} Agregar
        </Button>
      </div>
      {open && (
        <div id="inventory-product-results" ref={listRef} role="listbox" onScroll={scroll} className="absolute z-30 mt-1 max-h-72 w-full overflow-auto rounded-xl border bg-popover p-1 shadow-xl">
          {query.isLoading ? <p className="flex items-center gap-2 p-4 text-sm text-muted-foreground"><Loader2 className="h-4 w-4 animate-spin" />Buscando productos…</p>
          : query.isError ? <div className="p-4 text-sm text-red-700"><p>No fue posible cargar los productos.</p><Button className="mt-3" size="sm" variant="outline" onClick={() => void query.refetch()}>Reintentar</Button></div>
          : products.length === 0 ? <p className="p-4 text-sm text-muted-foreground">No hay productos activos que coincidan con la búsqueda.</p>
          : <><div className="px-3 py-2 text-xs text-muted-foreground">{products.length.toLocaleString("es-CO")} de {totalCount.toLocaleString("es-CO")} productos</div>
            {products.map((product, index) => <button key={product.productId} type="button" role="option" aria-selected={activeIndex === index} onMouseDown={(event) => event.preventDefault()} onMouseEnter={() => setActiveIndex(index)} onClick={() => choose(product)} className={`flex w-full items-center justify-between gap-4 rounded-lg px-3 py-2.5 text-left text-sm ${activeIndex === index ? "bg-emerald-50 text-emerald-950" : "hover:bg-muted"}`}><span className="min-w-0"><strong className="block truncate">{product.productName}</strong><small className="block truncate text-muted-foreground">{product.productCode}{product.reference ? ` · ${product.reference}` : ""}{conversionOnly && product.conversionFactor ? ` · factor ${product.conversionFactor}` : ""}</small></span><span className="flex shrink-0 items-center gap-3 text-xs text-muted-foreground">Saldo {product.quantityOnHand}{selectedProductIds.has(product.productId) && <Check className="h-4 w-4 text-emerald-700" aria-label="Agregado" />}</span></button>)}
            {query.hasNextPage && <Button type="button" variant="ghost" className="mt-1 w-full" disabled={query.isFetchingNextPage} onMouseDown={(event) => event.preventDefault()} onClick={() => void query.fetchNextPage()}>{query.isFetchingNextPage && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}Cargar 50 más</Button>}</>}
        </div>
      )}
    </div>
  );
}

function CountCaptureGrid({ prepared, lines, update, remove, onKey }: { prepared: boolean; lines: Line[]; update: (index: number, patch: Partial<Line>) => void; remove: (index: number) => void; onKey: (event: KeyboardEvent<HTMLInputElement>, index: number) => void }) {
  return <div className="overflow-x-auto rounded-xl border"><table className="w-full min-w-[760px] text-sm"><thead className="bg-muted/60 text-xs uppercase tracking-wide text-muted-foreground"><tr><th className="px-3 py-3 text-left">Producto</th><th className="px-3 py-3 text-right">Existencia sistema</th><th className="px-3 py-3 text-right">Preconteo</th>{prepared && <th className="px-3 py-3 text-left">Cantidad contada</th>}{prepared && <th className="px-3 py-3 text-right">Diferencia</th>}<th className="w-14" /></tr></thead><tbody>{lines.length === 0 ? <tr><td colSpan={prepared ? 6 : 4} className="p-10 text-center text-muted-foreground">Selecciona una bodega y agrega los productos del conteo.</td></tr> : lines.map((line, index) => { const preCount = Number(prepared ? line.preCount ?? 0 : line.quantity || 0); const difference = Number(line.quantity || 0) - preCount; return <tr key={line.productId} className="border-t focus-within:bg-emerald-50/60"><td className="px-3 py-2"><strong>{line.productName}</strong><span className="block text-xs text-muted-foreground">{line.productCode} · {line.unitCode}</span></td><td className="px-3 py-2 text-right tabular-nums">{prepared ? line.systemQuantity ?? line.stock : line.stock}</td><td className="px-3 py-2 text-right tabular-nums">{prepared ? preCount : <Input data-inventory-row={index} data-testid={`inventory-quantity-${index}`} className="ml-auto w-36 text-right tabular-nums" inputMode="decimal" value={line.quantity} onChange={(event) => update(index, { quantity: event.target.value })} onKeyDown={(event) => onKey(event, index)} aria-label={`Preconteo de ${line.productName}`} />}</td>{prepared && <td className="px-3 py-2"><Input data-inventory-row={index} data-testid={`inventory-quantity-${index}`} className="w-36 text-right tabular-nums" inputMode="decimal" value={line.quantity} onChange={(event) => update(index, { quantity: event.target.value })} onKeyDown={(event) => onKey(event, index)} aria-label={`Cantidad contada de ${line.productName}`} /></td>}{prepared && <td className={`px-3 py-2 text-right font-semibold tabular-nums ${difference > 0 ? "text-emerald-700" : difference < 0 ? "text-red-700" : "text-muted-foreground"}`}>{line.quantity === "" ? "—" : <>{difference > 0 ? "+" : ""}{difference}</>}</td>}<td className="px-2 py-2"><Button type="button" size="icon" variant="ghost" disabled={prepared} onClick={() => remove(index)} aria-label={`Eliminar ${line.productName}`}><Trash2 className="h-4 w-4" /></Button></td></tr>; })}</tbody></table></div>;
}

function CaptureGrid({ kind, prepared, lines, update, remove, onKey }: { kind: OperationKind; prepared: boolean; lines: Line[]; update: (index: number, patch: Partial<Line>) => void; remove: (index: number) => void; onKey: (event: KeyboardEvent<HTMLInputElement>, index: number) => void }) {
  if (kind === "count") return <CountCaptureGrid prepared={prepared} lines={lines} update={update} remove={remove} onKey={onKey} />;
  return <div className="overflow-x-auto rounded-xl border"><table className="w-full min-w-[760px] text-sm"><thead className="bg-muted/60"><tr><th className="px-3 py-3 text-left">Producto</th>{kind === "conversion" && <th className="px-3 py-3 text-left">Movimiento</th>}<th className="px-3 py-3 text-right">Saldo</th>{prepared && <th className="px-3 py-3 text-right">Saldo base</th>}<th className="px-3 py-3 text-left">{kind === "adjustment" ? "Cantidad (+ / −)" : "Cantidad"}</th>{kind === "adjustment" && <th className="px-3 py-3 text-left">Costo entrada</th>}<th className="w-14" /></tr></thead><tbody>{lines.length === 0 ? <tr><td colSpan={7} className="p-10 text-center text-muted-foreground">Selecciona una bodega y agrega los productos de la operación.</td></tr> : lines.map((line, index) => <tr key={line.productId} className="border-t focus-within:bg-emerald-50/60"><td className="px-3 py-2"><strong>{line.productName}</strong><span className="block text-xs text-muted-foreground">{line.productCode} · {line.unitCode}</span></td>{kind === "conversion" && <td className="px-3 py-2"><Select value={line.direction} onValueChange={(value) => update(index, { direction: value as Direction })}><SelectTrigger className="w-32"><SelectValue /></SelectTrigger><SelectContent><SelectItem value="INPUT">Consume</SelectItem><SelectItem value="OUTPUT">Produce</SelectItem></SelectContent></Select></td>}<td className="px-3 py-2 text-right tabular-nums">{line.stock}</td>{prepared && <td className="px-3 py-2 text-right tabular-nums">{line.systemQuantity ?? 0}</td>}<td className="px-3 py-2"><Input data-inventory-row={index} data-testid={`inventory-quantity-${index}`} className="w-36 text-right tabular-nums" inputMode="decimal" value={line.quantity} onChange={(event) => update(index, { quantity: event.target.value })} onKeyDown={(event) => onKey(event, index)} aria-label={`Cantidad de ${line.productName}`} /></td>{kind === "adjustment" && <td className="px-3 py-2"><Input className="w-36 text-right tabular-nums" inputMode="decimal" disabled={Number(line.quantity) <= 0} value={line.cost} onChange={(event) => update(index, { cost: event.target.value })} aria-label={`Costo de ${line.productName}`} /></td>}<td className="px-2 py-2"><Button type="button" size="icon" variant="ghost" disabled={prepared} onClick={() => remove(index)} aria-label={`Eliminar ${line.productName}`}><Trash2 className="h-4 w-4" /></Button></td></tr>)}</tbody></table></div>;
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return <div className="space-y-2"><Label>{label}</Label>{children}</div>;
}

function ConversionMetric({ label, value, detail }: { label: string; value: number; detail?: string }) {
  return <div className="rounded-lg bg-background/80 p-3">
    <p className="text-xs text-muted-foreground">{label}</p>
    <p className="mt-1 text-lg font-semibold tabular-nums">{value.toLocaleString("es-CO", { maximumFractionDigits: 6 })} {detail && <span className="text-xs font-normal text-muted-foreground">{detail}</span>}</p>
  </div>;
}

function conversionQuantity(value: number) {
  return Math.round((value + Number.EPSILON) * 1_000_000) / 1_000_000;
}
