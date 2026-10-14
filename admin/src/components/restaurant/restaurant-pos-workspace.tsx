"use client";

import Link from "next/link";
import { useEffect, useLayoutEffect, useMemo, useRef, useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowLeft, ArrowRight, CakeSlice, ChefHat, CircleCheck, Clock3,
  Coffee, DoorOpen,
  Fish, GlassWater, List, Map as MapIcon, MessageSquareText, FileText, LayoutGrid,
  Loader2, Minus, Plus, Printer, Salad, Sandwich, Search, Settings2, Soup, Trash2, UtensilsCrossed,
  type LucideIcon } from "lucide-react";
import { toast } from "sonner";
import { RestaurantPaymentDialog } from "@/components/restaurant/restaurant-payment-dialog";
import { RestaurantSessionClosure } from "@/components/restaurant/restaurant-session-closure";
import { PosPrinterDialog } from "@/app/(pos)/pos/pos-printer-dialog";
import { Button } from "@/components/ui/button";
import { PartyRoleSelect } from "@/components/parties/party-role-select";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { ThemeToggle } from "@/components/layout/theme-toggle";
import { useAuthStore } from "@/stores/auth-store";
import { useBodyScrollLock } from "@/hooks/use-body-scroll-lock";
import { closePrintPreview, OnlinePosClient, openHalfLetterPrintPreview,
  type SalesWorkspaceOption } from "@/services/pos/online-pos-client";
import type { PosPaymentInput, PosSaleDocumentType,
  PosPrintTemplateFormat } from "@/services/pos/pos-edge-client";
import { restaurantMasterApi, type RestaurantTable } from "@/services/api/restaurant-master";
import { floorPlanShapeRadius } from "@/lib/restaurant-floorplan-appearance";
import { FloorPlanObjectAppearance, FloorPlanTableSeats } from "@/components/restaurant/floor-plan-object-appearance";
import { restaurantAccountApi, type RestaurantAccountDraft,
  type RestaurantAttention, type RestaurantCancellation,
  type RestaurantCollectionAllocation, type RestaurantCollectionSummary,
  type RestaurantPrebill,
  type RestaurantProduct, type RestaurantTableOccupancy } from
  "@/services/api/restaurant-account";
import { inventoryApi } from "@/services/api/inventory";

const money = new Intl.NumberFormat("es-CO", {
  style: "currency", currency: "COP", maximumFractionDigits: 0,
});

const escapePrebillText = (value: string) => value.replace(/[&<>"']/g, (character) => ({
  "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;",
}[character] ?? character));

type ProductGroup = {
  productId: string;
  name: string;
  quantity: number;
  sentQuantity: number;
  pendingQuantity: number;
  amount: number;
  stationId: string | null;
  stationName: string | null;
  pending: RestaurantAccountDraft["additions"];
  items: RestaurantAccountDraft["additions"];
  contributors: string[];
};

export function RestaurantPosWorkspace({ option }: {
  option: SalesWorkspaceOption & { workSessionId?: string };
}) {
  const permissions = useAuthStore((state) => state.user?.permissions ?? []);
  const connectedUser = useAuthStore((state) => state.user);
  const canAttend = permissions.includes("restaurant.attend");
  const canOpenWithoutWaiter = permissions.includes(
    "restaurant.tables.open_without_waiter");
  const canSelectPriceChannel = permissions.includes("restaurant.price_channels.select");
  const queryClient = useQueryClient();
  const scope = ["restaurant-pos", option.businessId] as const;
  const [roomId, setRoomId] = useState("all");
  const [floorView, setFloorView] = useState<"plan" | "list">("plan");
  const initializedRoom = useRef(false);
  const waiterSecretInput = useRef<HTMLInputElement>(null);
  const reworkKeys = useRef(new Map<string, string>());
  const [tableSearch, setTableSearch] = useState("");
  const [tablePage, setTablePage] = useState(1);
  const [draft, setDraft] = useState<RestaurantAccountDraft | null>(null);
  const [customerOpen, setCustomerOpen] = useState(false);
  const [channelOpen, setChannelOpen] = useState(false);
  const [selectedTable, setSelectedTable] = useState<RestaurantTable | null>(null);
  const [pendingTable, setPendingTable] = useState<RestaurantTable | null>(null);
  const [waiterUserId, setWaiterUserId] = useState("");
  const [activeWaiterName, setActiveWaiterName] = useState("");
  const [waiterSearch, setWaiterSearch] = useState("");
  const [waiterSearchTerm, setWaiterSearchTerm] = useState("");
  const [waiterSecret, setWaiterSecret] = useState("");
  const [attention, setAttention] = useState<RestaurantAttention | null>(null);
  const [pendingSend, setPendingSend] = useState<{
    operationId: string; expectedVersion: number; additionId?: string;
  } | null>(null);
  const [categoryId, setCategoryId] = useState<string | null>(null);
  const [productSearch, setProductSearch] = useState("");
  const [productPage, setProductPage] = useState(0);
  const [mobileSection, setMobileSection] = useState<"products" | "account">("products");
  const openAccountId = draft?.account.accountId;
  useLayoutEffect(() => {
    if (openAccountId) window.scrollTo(0, 0);
  }, [openAccountId, mobileSection]);
  const [busy, setBusy] = useState(false);
  const [addingProductId, setAddingProductId] = useState<string | null>(null);
  const [printerOpen, setPrinterOpen] = useState(false);
  const [noteAdditionId, setNoteAdditionId] = useState<string | null>(null);
  const [noteText, setNoteText] = useState("");
  const [cancelAdditionId, setCancelAdditionId] = useState<string | null>(null);
  const [cancelQuantity, setCancelQuantity] = useState(1);
  const [cancelReasonCode, setCancelReasonCode] = useState("");
  const [cancelExplanation, setCancelExplanation] = useState("");
  const [cancelRequestId, setCancelRequestId] = useState<string | null>(null);
  const [checkoutPreview, setCheckoutPreview] = useState<{
    grossAmount: number; withholdingTotal: number; netAmount: number;
  } | null>(null);
  const [checkoutKey, setCheckoutKey] = useState<string | null>(null);
  const [collectionSummary, setCollectionSummary] =
    useState<RestaurantCollectionSummary | null>(null);
  const [prebillPreview, setPrebillPreview] = useState<RestaurantPrebill | null>(null);
  const captureAttempt = useRef<{ signature: string; id: string } | null>(null);
  const [checkoutDocumentType, setCheckoutDocumentType] =
    useState<PosSaleDocumentType>(option.fiscalReadyForOnlineSales === true &&
      option.hasDianDocumentQuota === true ? "SalesInvoice" : "SalesReceipt");
  const fiscalIssues = [
    option.fiscalReadyForOnlineSales === true ? null :
      "La factura electrónica no está habilitada en esta sede.",
    option.hasDianDocumentQuota === true ? null :
      "No hay cupo DIAN disponible para emitirla.",
  ].filter((issue): issue is string => issue !== null);
  const documentTypeBlockReason = checkoutDocumentType === "SalesReceipt" ||
    fiscalIssues.length === 0 ? null :
    `${fiscalIssues.join(" ")} Revisa la configuración fiscal o elige Comprobante de venta en Documento final.`;
  const canAdd = (canAttend || canOpenWithoutWaiter || !!attention) &&
    draft?.account.status === "Open";
  const paymentClient = useMemo(() => {
    if (!option.workSessionId || !connectedUser) return null;
    return new OnlinePosClient({
      ...option,
      fiscalReadyForOnlineSales: option.fiscalReadyForOnlineSales === true,
      fiscalReadyForEnrollment: option.fiscalReadyForEnrollment === true,
      hasDianDocumentQuota: option.hasDianDocumentQuota === true,
      fiscalWarningMessages: option.fiscalWarningMessages ?? [],
      workSessionId: option.workSessionId,
    }, connectedUser.userId,
    [connectedUser.firstName, connectedUser.lastName].filter(Boolean).join(" ") ||
      connectedUser.username || "Cajero");
  }, [connectedUser, option]);

  const rooms = useQuery({ queryKey: [...scope, "rooms"],
    queryFn: () => restaurantMasterApi.rooms(1) });
  useEffect(() => {
    if (initializedRoom.current || !rooms.data) return;
    initializedRoom.current = true;
    const first = rooms.data.items.find((room) => room.isActive);
    if (first) setRoomId(first.roomId);
  }, [rooms.data]);
  const tables = useQuery({ queryKey: [...scope, "tables", tablePage],
    queryFn: () => restaurantMasterApi.tables(tablePage) });
  const floorPlan = useQuery({ queryKey: [...scope, "floorplan", roomId],
    queryFn: () => restaurantMasterApi.floorPlan(roomId), enabled: roomId !== "all" });
  const occupancy = useQuery({ queryKey: [...scope, "occupancy"],
    queryFn: restaurantAccountApi.occupancy });
  const waiters = useQuery({ queryKey: [...scope, "waiters", waiterSearchTerm],
    queryFn: () => restaurantAccountApi.waiters(waiterSearchTerm || undefined),
    enabled: !!pendingTable });
  useEffect(() => {
    if (pendingTable && waiterUserId && waiters.data?.requireCredential)
      waiterSecretInput.current?.focus();
  }, [pendingTable, waiterUserId, waiters.data?.requireCredential]);

  const categories = useQuery({ queryKey: [...scope, "categories", draft?.account.accountId],
    queryFn: () => restaurantAccountApi.categories(draft!.account.accountId, attention?.token),
    enabled: !!draft && canAdd });
  const priceChannels = useQuery({ queryKey: [...scope, "price-channels"],
    queryFn: () => restaurantAccountApi.priceChannels(),
    enabled: canSelectPriceChannel && !!draft });
  async function changeTableCustomer(customerId: string | null) {
    if (!draft) return;
    setBusy(true);
    try {
      const updated = await restaurantAccountApi.changeCustomer(draft.account.accountId,
        customerId, null,
        draft.account.version, attention?.token);
      setDraft(updated);
      setCustomerOpen(false);
      setProductPage(0);
      await queryClient.invalidateQueries({ queryKey: [...scope, "products", draft.account.accountId] });
    } catch (error) {
      toast.error(error instanceof Error ? error.message : "No se pudo cambiar el cliente.");
    } finally { setBusy(false); }
  }
  async function changeTableChannel(priceChannelId: string | null) {
    if (!draft) return;
    setBusy(true);
    try {
      const updated = await restaurantAccountApi.changePriceChannel(
        draft.account.accountId, priceChannelId, draft.account.version);
      setDraft(updated);
      setChannelOpen(false);
      setProductPage(0);
      await queryClient.invalidateQueries({ queryKey: [...scope, "products", draft.account.accountId] });
    } catch (error) {
      toast.error(error instanceof Error ? error.message : "No se pudo cambiar el canal.");
    } finally { setBusy(false); }
  }
  const products = useQuery({ queryKey: [...scope, "products", draft?.account.accountId,
    categoryId, productSearch, productPage],
    queryFn: () => restaurantAccountApi.products(draft!.account.accountId, {
      search: productSearch.trim() || undefined,
      categoryId: productSearch.trim() ? undefined :
        categoryId && categoryId !== "all" ? categoryId : undefined,
      skip: productPage * 40, take: 40,
    }, attention?.token),
    enabled: !!draft && canAdd });
  const cancellations = useQuery({
    queryKey: [...scope, "cancellations", draft?.account.accountId],
    queryFn: () => restaurantAccountApi.cancellations(
      draft!.account.accountId, attention?.token),
    enabled: !!draft,
  });
  const cancellationReasons = useQuery({
    queryKey: [...scope, "cancellation-reasons"],
    queryFn: () => inventoryApi.businessReasons("RestaurantCancellation"),
    enabled: !!cancelAdditionId,
  });

  useEffect(() => {
    if (draft && canAdd && categoryId === null && categories.data)
      setCategoryId(categories.data[0]?.categoryId ?? "all");
  }, [draft, canAdd, categoryId, categories.data]);

  const roomNames = useMemo(() => new Map((rooms.data?.items ?? []).map((room) =>
    [room.roomId, room.name])), [rooms.data]);
  const tableStates = useMemo(() => new Map((occupancy.data ?? []).map((state) =>
    [state.tableId, state])), [occupancy.data]);
  const visibleTables = useMemo(() => (tables.data?.items ?? []).filter((table) =>
    table.isActive && (roomId === "all" || table.roomId === roomId) &&
    `${table.code} ${table.name ?? ""}`.toLocaleLowerCase("es")
      .includes(tableSearch.trim().toLocaleLowerCase("es"))),
  [tables.data, roomId, tableSearch]);
  const selectTable = (table: RestaurantTable, chooseWaiter = false) => {
    if (canOpenWithoutWaiter && !chooseWaiter) {
      void openTable(table);
      return;
    }
    setPendingTable(table); setWaiterUserId(""); setWaiterSecret("");
    setWaiterSearch(""); setWaiterSearchTerm("");
  };
  const activeFloorPlan = roomId !== "all" && floorPlan.data?.hasCustomLayout &&
    !tableSearch.trim() && floorView !== "list" ? floorPlan.data : null;
  const seatingByTable = useMemo(() => {
    const counts = new Map<string, { chairs: number; furniture: number }>();
    for (const item of floorPlan.data?.objects ?? []) {
      if (!item.tableId || (item.kind !== "Chair" && item.kind !== "Bench")) continue;
      const current = counts.get(item.tableId) ?? { chairs: 0, furniture: 0 };
      if (item.kind === "Chair") current.chairs++;
      else current.furniture++;
      counts.set(item.tableId, current);
    }
    return counts;
  }, [floorPlan.data]);
  const groups = useMemo<ProductGroup[]>(() => {
    const grouped = new Map<string, ProductGroup>();
    for (const item of draft?.additions ?? []) {
      if (item.status === "Cancelled") continue;
      const key = `${item.productId}|${item.publicUnitPrice}|${item.promotionDiscountPerUnit}|${item.stationId ?? "unrouted"}`;
      const current = grouped.get(key) ?? {
        productId: item.productId, name: item.description,
        quantity: 0, sentQuantity: 0, pendingQuantity: 0,
        amount: 0, stationId: item.stationId, stationName: item.stationName,
        pending: [], items: [], contributors: [],
      };
      current.items.push(item);
      current.quantity += item.quantity;
      current.amount += item.quantity * (item.publicUnitPrice - item.promotionDiscountPerUnit);
      if (item.status === "Pending") {
        current.pending.push(item);
        current.pendingQuantity += item.quantity;
      } else if (item.status === "Sent") current.sentQuantity += item.quantity;
      if (!current.contributors.includes(item.addedByName))
        current.contributors.push(item.addedByName);
      grouped.set(key, current);
    }
    return [...grouped.values()];
  }, [draft]);
  const estimatedTotal = groups.reduce((sum, group) => sum + group.amount, 0);
  const pendingCount = draft?.additions.filter((item) => item.status === "Pending").length ?? 0;
  const activeCancellations = cancellations.data?.filter((item) =>
    item.status === "Requested" || item.status === "Confirmed") ?? [];
  const cancellationsByAddition = new Map(activeCancellations.map((item) =>
    [item.additionId, item]));
  const availableReworks = (cancellations.data ?? []).filter((item) =>
    item.status === "Approved" &&
    (item.preparationState === "Prepared" || item.preparationState === "Partial") &&
    !draft?.additions.some((addition) =>
      addition.replacementOfCancellationId === item.cancellationId));
  const cancellingAddition = draft?.additions.find((item) =>
    item.additionId === cancelAdditionId && item.status === "Sent");
  const editingAddition = draft?.additions.find((item) =>
    item.additionId === noteAdditionId && item.status === "Pending" && !!item.stationId);
  useBodyScrollLock(Boolean(cancellingAddition || editingAddition || pendingTable));

  async function saveNote() {
    if (!draft || !editingAddition || busy || pendingSend) return;
    const note = noteText.trim() || null;
    setBusy(true);
    try {
      const updated = await restaurantAccountApi.changeNote(
        draft.account.accountId, editingAddition.additionId,
        { preparationNote: note, expectedVersion: draft.account.version },
        attention?.token);
      setDraft(updated);
      setNoteAdditionId(null);
    } catch (error) {
      try {
        const current = await restaurantAccountApi.draft(
          draft.account.accountId, attention?.token);
        setDraft(current);
        if (current.additions.find((item) =>
          item.additionId === editingAddition.additionId)?.preparationNote === note) {
          setNoteAdditionId(null);
          return;
        }
      } catch { /* Keep the editor open until the account can be checked. */ }
      toast.error(error instanceof Error ? error.message :
        "No se pudo guardar la observación. Revisa la cuenta e inténtalo de nuevo.");
    } finally { setBusy(false); }
  }

  async function requestCancellation() {
    if (!draft || !cancellingAddition || busy || !cancelReasonCode ||
        !cancelExplanation.trim() || cancelQuantity <= 0 ||
        cancelQuantity > cancellingAddition.quantity) return;
    const cancellationId = cancelRequestId ?? crypto.randomUUID();
    setCancelRequestId(cancellationId);
    setBusy(true);
    try {
      const created = await restaurantAccountApi.requestCancellation(
        draft.account.accountId, cancellingAddition.additionId,
        { cancellationId, quantity: cancelQuantity,
          reasonCode: cancelReasonCode,
          explanation: cancelExplanation.trim(),
          expectedAccountVersion: draft.account.version }, attention?.token);
      queryClient.setQueryData<RestaurantCancellation[]>(
        [...scope, "cancellations", draft.account.accountId],
        (current) => [...(current ?? []).filter((item) =>
          item.cancellationId !== created.cancellationId), created]);
      setDraft((current) => current ? { ...current,
        account: { ...current.account, version: Math.max(current.account.version,
          created.expectedAccountVersion + 1) } } : current);
      setCancelAdditionId(null);
      setCancelRequestId(null);
      toast.success("Cancelación enviada a la estación para confirmar la preparación.");
    } catch (error) {
      toast.error(error instanceof Error ? error.message :
        "No se pudo confirmar la solicitud. Reintenta sin cambiarla.");
    } finally { setBusy(false); }
  }

  async function refreshCancellations() {
    if (!draft || busy) return;
    setBusy(true);
    try {
      const [account, updated] = await Promise.all([
        restaurantAccountApi.draft(draft.account.accountId, attention?.token),
        restaurantAccountApi.cancellations(draft.account.accountId, attention?.token),
      ]);
      setDraft(account);
      queryClient.setQueryData([...scope, "cancellations", draft.account.accountId], updated);
    } catch { toast.error("No se pudo actualizar el estado de las cancelaciones."); }
    finally { setBusy(false); }
  }

  function rememberOccupancy(accountDraft: RestaurantAccountDraft) {
    queryClient.setQueryData<RestaurantTableOccupancy[]>(
      [...scope, "occupancy"], (current) => current?.map((item) =>
        item.tableId === accountDraft.account.tableId ? {
          ...item,
          accountId: accountDraft.account.accountId,
          state: accountDraft.account.status === "Open" &&
            !accountDraft.additions.some((addition) => addition.status !== "Cancelled")
            ? "Free" : accountDraft.account.status,
          responsibleUserId: accountDraft.account.responsibleUserId,
          openedAt: accountDraft.account.openedAt,
          accountVersion: accountDraft.account.version,
        } : item));
  }

  async function openTable(table: RestaurantTable, token?: string): Promise<boolean> {
    if (busy) return false;
    setBusy(true);
    try {
      const state = tableStates.get(table.tableId);
      const accountDraft = state?.accountId
        ? await restaurantAccountApi.draft(state.accountId, token)
        : { account: await restaurantAccountApi.open({
              accountId: crypto.randomUUID(), tableId: table.tableId,
              warehouseId: option.warehouseId,
            }, token), additions: [] };
      rememberOccupancy(accountDraft);
      setSelectedTable(table);
      setDraft(accountDraft);
      setCategoryId(null);
      setProductSearch("");
      setProductPage(0);
      setMobileSection("products");
      setPendingSend(null);
      return true;
    } catch {
      toast.error("No fue posible abrir la mesa. Revisa la conexión y vuelve a intentarlo.");
      return false;
    } finally {
      setBusy(false);
    }
  }

  async function beginAttention() {
    if (!pendingTable || busy) return;
    if (!waiterUserId && canOpenWithoutWaiter) {
      if (await openTable(pendingTable)) {
        if (attention) void restaurantAccountApi.endAttention(attention.token)
          .catch(() => undefined);
        setAttention(null);
        setActiveWaiterName("");
        setPendingTable(null);
      }
      return;
    }
    if (!waiterUserId) return;
    setBusy(true);
    let grant: RestaurantAttention | null = null;
    try {
      grant = await restaurantAccountApi.startAttention({
        tableId: pendingTable.tableId, waiterUserId,
        secret: waiters.data?.requireCredential ? waiterSecret : undefined,
      });
      const previousAttention = attention;
      setAttention(grant);
      setActiveWaiterName(waiters.data?.items.find((item) =>
        item.userId === waiterUserId)?.name ?? "Mesero identificado");
      setPendingTable(null);
      setWaiterSecret("");
      if (previousAttention) void restaurantAccountApi.endAttention(
        previousAttention.token).catch(() => undefined);
    } catch (error) {
      toast.error(error instanceof Error ? error.message : "No se pudo identificar al mesero.");
      return;
    } finally {
      setBusy(false);
    }
    if (!await openTable(pendingTable, grant.token)) {
      void restaurantAccountApi.endAttention(grant.token);
      setAttention(null);
    }
  }

  async function mutateProduct(productId: string, direction: "plus" | "remove",
    targetAdditionId?: string) {
    if (!draft || busy || pendingSend || !canAdd) return;
    const accountId = draft.account.accountId;
    const operationId = crypto.randomUUID();
    const target = direction === "remove"
      ? draft.additions.find((item) => item.additionId === targetAdditionId &&
          item.productId === productId && (item.status === "Pending" || item.status === "Direct")) : null;
    if (direction === "remove" && !target) return;
    const nextQuantity = target?.status === "Direct"
      ? Math.max(0, target.quantity - 1) : 0;
    if (direction === "plus") setAddingProductId(productId);
    setBusy(true);
    try {
      const updated = direction === "plus"
        ? await restaurantAccountApi.add(accountId, {
            additionId: operationId, productId, quantity: 1,
            expectedVersion: draft.account.version,
          }, attention?.token)
        : await restaurantAccountApi.changeQuantity(accountId,
            target!.additionId, {
              changeId: operationId,
              quantity: nextQuantity,
              expectedVersion: draft.account.version,
            }, attention?.token);
      setDraft(updated);
    } catch (error) {
      if (attention && typeof error === "object" && error !== null &&
          "statusCode" in error && error.statusCode === 403) {
        backToTables(true);
        toast.error("La atención terminó. Identifica al mesero de nuevo.");
        return;
      }
      try {
        if (direction === "remove" && target) {
          const replay = await restaurantAccountApi.changeQuantity(accountId,
            target.additionId, {
              changeId: operationId,
              quantity: nextQuantity,
              expectedVersion: draft.account.version,
            }, attention?.token);
          setDraft(replay);
          return;
        }
        const authoritative = await restaurantAccountApi.draft(accountId, attention?.token);
        setDraft(authoritative);
        const committed = direction === "plus"
          ? authoritative.additions.some((item) => item.additionId === operationId)
          : false;
        if (!committed) toast.error("La cuenta cambió. Revísala antes de volver a tocar el producto.");
      } catch {
        toast.error("No se pudo confirmar el cambio. Reabre la mesa para conciliarlo.");
      }
    } finally {
      setAddingProductId(null);
      setBusy(false);
    }
  }

  async function reworkProduct(cancellation: RestaurantCancellation) {
    if (!draft || busy || pendingSend || !canAdd) return;
    const original = draft.additions.find((item) =>
      item.additionId === cancellation.additionId);
    if (!original) return;
    const accountId = draft.account.accountId;
    const additionId = reworkKeys.current.get(cancellation.cancellationId) ?? crypto.randomUUID();
    reworkKeys.current.set(cancellation.cancellationId, additionId);
    setBusy(true);
    try {
      const updated = await restaurantAccountApi.add(accountId, {
        additionId, productId: original.productId,
        quantity: cancellation.quantity,
        expectedVersion: draft.account.version,
        replacementOfCancellationId: cancellation.cancellationId,
      }, attention?.token);
      setDraft(updated);
      reworkKeys.current.delete(cancellation.cancellationId);
      toast.success("Reemplazo agregado a la cuenta. Envíalo a la comanda cuando esté listo.");
    } catch (error) {
      if (attention && typeof error === "object" && error !== null &&
          "statusCode" in error && error.statusCode === 403) {
        backToTables(true);
        toast.error("La atención terminó. Identifica al mesero de nuevo.");
        return;
      }
      try {
        const authoritative = await restaurantAccountApi.draft(accountId, attention?.token);
        setDraft(authoritative);
        if (authoritative.additions.some((item) => item.additionId === additionId)) {
          reworkKeys.current.delete(cancellation.cancellationId);
          toast.success("El reemplazo ya quedó agregado a la cuenta.");
        } else toast.error("No se pudo agregar el reemplazo. Revisa la cuenta e inténtalo de nuevo.");
      } catch { toast.error("No se pudo confirmar el reemplazo. Reabre la mesa para conciliarlo."); }
    } finally { setBusy(false); }
  }

  async function sendPending(additionId?: string) {
    if (!draft || busy || !canAdd || pendingSend &&
        pendingSend.additionId !== additionId) return;
    const request = pendingSend ?? {
      operationId: crypto.randomUUID(),
      expectedVersion: draft.account.version, additionId,
    };
    setPendingSend(request);
    setBusy(true);
    const accountId = draft.account.accountId;
    const confirmSend = async () => restaurantAccountApi.sendPending(
      accountId, request, attention?.token);
    const acceptSend = (result: Awaited<ReturnType<typeof confirmSend>>) => {
      setDraft(result.draft);
      setPendingSend(null);
      toast.success(`Comanda enviada: ${result.cards.map((card) =>
        `${card.stationName} · ${card.lineCount}`).join(" / ")}`);
    };
    try {
      acceptSend(await confirmSend());
    } catch (error) {
      if (attention && typeof error === "object" && error !== null &&
          "statusCode" in error && error.statusCode === 403) {
        backToTables(true);
        toast.error("La atención terminó. Identifica al mesero de nuevo.");
        return;
      }
      // A response can be lost after the transaction commits. Replay with the
      // same operation id before deciding whether the account actually changed.
      try {
        acceptSend(await confirmSend());
        return;
      } catch (replayError) {
        if (attention && typeof replayError === "object" && replayError !== null &&
            "statusCode" in replayError && replayError.statusCode === 403) {
          backToTables(true);
          toast.error("La atención terminó. Identifica al mesero de nuevo.");
          return;
        }
        try {
          setDraft(await restaurantAccountApi.draft(accountId, attention?.token));
          setPendingSend(null);
          toast.error(replayError instanceof Error ? replayError.message :
            "La cuenta cambió. Revisa sus pendientes antes de enviar.");
        } catch {
          toast.error("No se pudo verificar el envío. Reintenta la comanda o vuelve a mesas y abre la cuenta de nuevo.");
        }
      }
    } finally { setBusy(false); }
  }

  async function openCheckout() {
    if (!draft || !option.workSessionId || busy || pendingSend ||
        pendingCount > 0 || activeCancellations.length > 0 ||
        cancellations.isLoading || cancellations.isError ||
        draft.account.status !== "Open") return;
    setBusy(true);
    try {
      const [preview, collections] = await Promise.all([
        restaurantAccountApi.settlementPreview(
          draft.account.accountId, option.workSessionId),
        restaurantAccountApi.collections(draft.account.accountId),
      ]);
      setCheckoutKey(crypto.randomUUID());
      setCheckoutPreview(preview);
      setCollectionSummary(collections);
    } catch (error) {
      toast.error(error instanceof Error ? error.message :
        "No fue posible preparar el cobro de la mesa.");
    } finally { setBusy(false); }
  }

  async function preparePrebill() {
    if (!draft || busy || pendingSend || pendingCount > 0 ||
        activeCancellations.length > 0 ||
        cancellations.isLoading || cancellations.isError ||
        draft.account.status !== "Open") return;
    setBusy(true);
    try {
      const bill = await restaurantAccountApi.prebill(
        draft.account.accountId, draft.account.version, attention?.token);
      setPrebillPreview(bill);
    } catch (error) {
      toast.error(error instanceof Error ? error.message :
        "No se pudo preparar la precuenta.");
    } finally { setBusy(false); }
  }

  function printPrebill(bill: RestaurantPrebill, includeTip: boolean) {
    const printWindow = window.open("", "_blank", "width=440,height=720");
    if (!printWindow) {
      toast.error("Habilita la ventana de impresión para esta página.");
      return;
    }
    try {
      const rows = bill.lines.map((line) => `<tr><td>${escapePrebillText(line.description)}
        <small>${escapePrebillText(line.addedByName)}</small></td>
        <td>${line.quantity}</td><td>${money.format(line.total)}</td></tr>`).join("");
      const tip = includeTip && bill.suggestedTipAmount > 0;
      printWindow.document.open();
      printWindow.document.write(`<!doctype html><html lang="es"><head>
        <meta charset="utf-8"><title>Precuenta · Mesa ${escapePrebillText(bill.tableCode)}</title>
        <style>body{font:14px system-ui,sans-serif;color:#182628;max-width:80mm;margin:0 auto;padding:20px}
        h1{font-size:24px;margin:5px 0}h2{font-size:18px;margin:0 0 16px}
        .tag{font-weight:800;letter-spacing:.16em;color:#087e75;font-size:11px}
        .notice{border:2px solid #087e75;border-radius:8px;padding:10px;margin:14px 0;font-weight:700}
        table{width:100%;border-collapse:collapse}td{padding:8px 2px;border-bottom:1px dashed #aaa;vertical-align:top}
        td:nth-child(2),td:nth-child(3){text-align:right;white-space:nowrap}
        small{display:block;color:#667;font-size:10px}.total{display:flex;justify-content:space-between;
        font-size:20px;font-weight:800;margin-top:18px}.meta{color:#667;font-size:11px}
        button{width:100%;padding:12px;margin-top:24px;border:0;border-radius:9px;background:#087e75;color:white;font-weight:700}
        @media print{body{padding:0}button{display:none}}</style></head><body>
        <div class="tag">AURALY · RESTAURANTE</div><h1>Mesa ${escapePrebillText(bill.tableCode)}</h1>
        <h2>Precuenta</h2><div class="meta">${new Date(bill.printedAt).toLocaleString("es-CO")}
        · Revisión ${bill.accountVersion}</div>
        <div class="notice">Precuenta · No es factura</div>
        <table><tbody>${rows}</tbody></table>
        <div class="total"><span>Consumo</span><span>${money.format(bill.total)}</span></div>
        ${tip ? `<div class="total"><span>Propina voluntaria sugerida (${bill.suggestedTipPercent} %)</span><span>${money.format(bill.suggestedTipAmount)}</span></div>
        <div class="total"><span>Total sugerido</span><span>${money.format(bill.total + bill.suggestedTipAmount)}</span></div>
        <p class="meta">La propina es voluntaria. Puedes aceptarla, cambiarla o no pagarla.</p>` : ""}
        <p class="meta">Confirma el pago en caja. La factura se emite al cobrar.</p>
        <button onclick="window.print()">Imprimir precuenta</button></body></html>`);
      printWindow.document.close();
      printWindow.focus();
      setPrebillPreview(null);
    } catch (error) {
      printWindow.close();
      toast.error(error instanceof Error ? error.message :
        "No se pudo preparar la precuenta.");
    }
  }

  async function captureCollection(
    allocations: RestaurantCollectionAllocation[],
    payments: PosPaymentInput[],
    tipPayment: PosPaymentInput | null,
  ): Promise<RestaurantCollectionSummary> {
    if (!draft || !option.workSessionId || !collectionSummary)
      throw new Error("Vuelve a abrir el cobro de la mesa.");
    const signature = JSON.stringify({ accountId: draft.account.accountId,
      version: collectionSummary.accountVersion, allocations, payments, tipPayment });
    if (captureAttempt.current?.signature !== signature)
      captureAttempt.current = { signature, id: crypto.randomUUID() };
    const collectionId = captureAttempt.current.id;
    const updated = await restaurantAccountApi.captureCollection(
      draft.account.accountId, {
        collectionId, workSessionId: option.workSessionId,
        expectedAccountVersion: collectionSummary.accountVersion,
        allocations, payments, tipPayment,
      }, collectionId);
    captureAttempt.current = null;
    setCollectionSummary(updated);
    return updated;
  }

  async function reverseCollection(collectionId: string): Promise<void> {
    if (!draft || !collectionSummary)
      throw new Error("Vuelve a abrir el cobro de la mesa.");
    const updated = await restaurantAccountApi.reverseCollection(
      draft.account.accountId, collectionId, collectionSummary.accountVersion);
    setCollectionSummary(updated);
  }

  async function completeCheckout(
    payments: PosPaymentInput[],
    version: number,
    printChoice: PosPrintTemplateFormat | "none",
  ) {
    if (!draft || !option.workSessionId || !checkoutKey || !paymentClient || busy)
      throw new Error("Vuelve a abrir el cobro para emitir el documento guardado.");
    const preview = printChoice === "none"
      ? null : openHalfLetterPrintPreview();
    setBusy(true);
    try {
      const result = await restaurantAccountApi.complete(
        draft.account.accountId, option.workSessionId,
        { expectedVersion: version, payments,
          documentType: checkoutDocumentType }, checkoutKey);
      setCheckoutPreview(null);
      setCheckoutKey(null);
      toast.success(`Mesa facturada · ${result.receipt.documentNumber}`);
      try {
        if (printChoice !== "none" && !result.isReplay)
          await paymentClient.printRestaurantReceipt(
            result.receipt, printChoice, preview);
        else closePrintPreview(preview);
      } catch {
        closePrintPreview(preview);
        toast.error("La factura quedó guardada, pero no se pudo imprimir. Puedes reimprimirla desde facturas.");
      }
      backToTables(true);
    } catch (error) {
      closePrintPreview(preview);
      throw error;
    } finally { setBusy(false); }
  }

  function backToTables(force = false) {
    if (busy && !force) {
      return;
    }
    if (draft?.account.status === "Open") rememberOccupancy(draft);
    setDraft(null);
    setSelectedTable(null);
    if (attention) void restaurantAccountApi.endAttention(attention.token)
      .catch(() => undefined);
    setAttention(null);
    setActiveWaiterName("");
    setPendingSend(null);
    setCancelAdditionId(null);
    setCancelRequestId(null);
    setCheckoutPreview(null);
    setCheckoutKey(null);
    setCollectionSummary(null);
    captureAttempt.current = null;
    void queryClient.removeQueries({ queryKey: [...scope, "categories"] });
    void queryClient.removeQueries({ queryKey: [...scope, "products"] });
    void queryClient.invalidateQueries({ queryKey: [...scope, "occupancy"] });
  }

  function leaveWorkspace(event: React.MouseEvent<HTMLAnchorElement>) {
    if (busy) {
      event.preventDefault();
      return;
    }
    backToTables();
  }

  return <main className="min-h-screen bg-[#f5f7f6] text-foreground dark:bg-[#0e1717]">
    <header className="sticky top-0 z-20 flex min-h-20 flex-col gap-2 border-b bg-background/95 px-3 py-2 shadow-sm backdrop-blur sm:px-7 lg:flex-row lg:items-center lg:justify-between lg:gap-3 lg:py-0">
      <div className="flex w-full min-w-0 items-center gap-2 lg:w-auto lg:gap-3">{draft ? <Button variant="outline" className="h-11 w-11 shrink-0 rounded-xl p-0 sm:w-auto sm:px-4" onClick={() => backToTables()} aria-label="Volver a mesas"><ArrowLeft className="h-5 w-5 sm:mr-2"/><span className="hidden text-sm sm:inline">Volver a mesas</span></Button> : <Button asChild variant="outline" className="h-11 w-11 shrink-0 rounded-xl p-0 sm:w-auto sm:px-4" aria-label="Volver al menú"><Link href="/dashboard/restaurant" onClick={leaveWorkspace}><LayoutGrid className="h-5 w-5 sm:mr-2"/><span className="hidden sm:inline">Menú</span></Link></Button>}<div className="grid h-11 w-11 shrink-0 place-items-center rounded-xl bg-primary/10 text-primary"><UtensilsCrossed className="h-6 w-6"/></div>
        <div className="min-w-0"><p className="truncate text-[10px] font-bold uppercase tracking-[.16em] text-primary sm:text-[11px]">Auraly · Restaurante</p>
          <h1 className="truncate text-base font-bold leading-tight sm:text-xl">{option.businessName}</h1></div></div>
      <div className="flex w-full items-center justify-center gap-2 rounded-2xl border bg-card/70 p-1.5 shadow-sm sm:flex-wrap sm:justify-end sm:border-0 sm:bg-transparent sm:p-0 sm:shadow-none lg:w-auto lg:flex-nowrap">
        {paymentClient && option.workSessionId && permissions.includes("work-sessions.read") &&
          <RestaurantSessionClosure client={paymentClient} workSessionId={option.workSessionId}
            businessId={option.businessId} businessName={option.businessName}
            warehouseName={option.warehouseName}
            canClose={permissions.includes("work-sessions.close")} />}
        {permissions.includes("pos.workspace.change") && <Button asChild variant="outline" size="sm"
          className="h-11 w-11 gap-2 rounded-xl p-0 sm:w-auto sm:px-3" title="Cambiar sede, bodega o documento predeterminado">
          <Link href="/pos?workspace=change" onClick={leaveWorkspace} aria-label="Configurar punto de venta">
            <Settings2 className="h-4 w-4"/><span className="hidden md:inline">Configuración</span>
          </Link></Button>}
        <Button variant="outline" size="sm" className="h-11 w-11 gap-2 rounded-xl p-0 sm:w-auto sm:px-3"
          onClick={() => setPrinterOpen(true)} aria-label="Periféricos">
          <Printer className="h-4 w-4"/><span className="hidden md:inline">Periféricos</span>
        </Button>
        {permissions.includes("restaurant.commands.read") && <Button asChild variant="outline" size="sm" className="h-11 w-11 rounded-xl p-0 sm:w-auto sm:px-3"><Link href="/restaurant/commands?from=pos" onClick={leaveWorkspace} aria-label="Comandas" title="Comandas"><ChefHat className="h-4 w-4"/><span className="hidden sm:inline">Comandas</span></Link></Button>}
        <ThemeToggle className="h-11 w-11 rounded-xl"/></div>
      {draft && <nav aria-label="Vista de la mesa" className="grid w-full grid-cols-2 gap-2 lg:hidden"><Button variant={mobileSection === "products" ? "default" : "outline"} className="min-h-12" onClick={() => setMobileSection("products")}>Productos</Button><Button variant={mobileSection === "account" ? "default" : "outline"} className="min-h-12" onClick={() => setMobileSection("account")}>Cuenta · {groups.reduce((sum, item) => sum + item.quantity, 0)}</Button></nav>}
    </header>
    {!draft ? <section className="mx-auto max-w-[1500px] px-4 pb-12 pt-7 sm:px-7">
      <div className="flex flex-wrap items-end justify-between gap-4"><div><p className="text-sm font-medium text-primary">Vista de mesas</p>
        <h2 className="mt-1 text-3xl font-bold tracking-tight">¿A qué mesa atendemos?</h2>
        <p className="mt-2 text-sm text-muted-foreground">Selecciona una mesa para ver su cuenta o empezar un pedido.</p></div>
        <div className="rounded-full border bg-card px-4 py-2 text-sm font-medium shadow-sm">{visibleTables.length} mesas en esta vista</div></div>
      <div className="mt-6 flex flex-wrap items-center gap-3"><nav aria-label="Salones" className="flex max-w-full gap-3 overflow-x-auto pb-2"><button type="button" aria-pressed={roomId === "all"} onClick={() => setRoomId("all")}
        className={`flex min-h-16 shrink-0 items-center gap-3 rounded-2xl border px-5 text-left shadow-sm transition-all active:scale-[.98] ${roomId === "all" ? "border-primary bg-primary/10 text-primary shadow-primary/10" : "bg-card hover:border-primary/50 hover:shadow-md"}`}><span className="grid h-10 w-10 place-items-center rounded-xl bg-primary/10"><LayoutGrid className="h-5 w-5"/></span><span className="font-bold">Todas</span></button>
        {(rooms.data?.items ?? []).filter((room) => room.isActive).map((room) =>
          <button key={room.roomId} type="button" aria-pressed={roomId === room.roomId} onClick={() => setRoomId(room.roomId)} className={`flex min-h-16 shrink-0 items-center gap-3 rounded-2xl border px-5 text-left shadow-sm transition-all active:scale-[.98] ${roomId === room.roomId ? "border-primary bg-primary/10 text-primary shadow-primary/10" : "bg-card hover:border-primary/50 hover:shadow-md"}`}><span className="grid h-10 w-10 place-items-center rounded-xl bg-primary/10"><DoorOpen className="h-5 w-5"/></span><span className="font-bold">{room.name}</span></button>)}</nav>
        <div className="relative ml-auto w-full sm:w-72"><Search className="absolute left-4 top-3.5 h-5 w-5 text-muted-foreground"/>
          <Input className="h-12 rounded-2xl bg-card pl-11" value={tableSearch} onChange={(event) => setTableSearch(event.target.value)} placeholder="Buscar mesa" aria-label="Buscar mesa"/></div></div>
      {roomId !== "all" && floorPlan.data?.hasCustomLayout && <div className="mt-4 flex flex-wrap gap-3" role="group" aria-label="Vista del salón">
        {([ ["plan", "Plano", MapIcon], ["list", "Lista de mesas", List] ] as const).map(([mode, label, Icon]) =>
          <button key={mode} type="button" aria-pressed={floorView === mode} className={`flex min-h-14 items-center gap-3 rounded-2xl border px-4 text-sm font-bold shadow-sm transition-all active:scale-[.98] ${floorView === mode ? "border-primary bg-primary/10 text-primary shadow-primary/10" : "bg-card hover:border-primary/50 hover:shadow-md"}`} onClick={() => setFloorView(mode)}><span className="grid h-9 w-9 place-items-center rounded-xl bg-primary/10"><Icon className="h-5 w-5"/></span>{label}</button>)}
      </div>}
      {(tables.isLoading || occupancy.isLoading) && <p className="mt-10 text-muted-foreground">Cargando mesas…</p>}
      {(tables.isError || occupancy.isError) && <div role="alert" className="mt-8 rounded-2xl border border-destructive/30 bg-card p-5">No pudimos cargar las mesas. <Button variant="link" onClick={() => { void tables.refetch(); void occupancy.refetch(); }}>Reintentar</Button></div>}
      {!tables.isLoading && !tables.isError && !visibleTables.length && <div className="mt-8 grid min-h-64 place-items-center rounded-[2rem] border border-dashed bg-card p-7 text-center"><div><LayoutGrid className="mx-auto h-10 w-10 text-primary"/><h3 className="mt-3 text-xl font-semibold">Aún no hay mesas en esta vista</h3><p className="mt-1 text-sm text-muted-foreground">Crea mesas en Configuración → Restaurante o cambia el filtro.</p></div></div>}
      {activeFloorPlan && <div className="relative mt-5 aspect-[10/7] w-full overflow-hidden rounded-[2rem] border border-stone-200 bg-[#f8f5ed] shadow-[0_20px_60px_-30px_#364c45aa] dark:border-slate-700 dark:bg-[#202d29]">
        <div aria-hidden="true" className="pointer-events-none absolute inset-0 bg-[radial-gradient(circle_at_center,#b9b19d55_1px,transparent_1px)] bg-[length:24px_24px] opacity-35 dark:opacity-20"/>
        {activeFloorPlan.objects.filter((object) => object.kind !== "Table").map((object) =>
          <div key={object.objectId} data-floor-kind={object.kind} aria-hidden="true" className={`pointer-events-none absolute overflow-hidden ${object.kind === "Chair" || object.kind === "Bench" ? "z-10" : ""}`}
            style={{ left: `${object.x / 10}%`, top: `${object.y / 7}%`, width: `${object.width / 10}%`, height: `${object.height / 7}%`, rotate: `${object.rotation}deg` }}><FloorPlanObjectAppearance object={object}/></div>)}
        {activeFloorPlan.objects.filter((object) => object.kind === "Table").map((object) => {
          const table = activeFloorPlan.tables.find((entry) => entry.tableId === object.tableId);
          if (!table) return null;
          const occupied = tableStates.get(table.tableId)?.state !== "Free" &&
            !!tableStates.get(table.tableId)?.accountId;
          return <button key={object.objectId} type="button" disabled={busy} onClick={() => selectTable(table)}
            className={`absolute flex min-h-8 items-center justify-center rounded-2xl border-2 px-1 text-center text-xs font-bold shadow-[0_6px_12px_-5px_#39443b99] transition hover:z-20 hover:scale-105 focus-visible:outline-4 focus-visible:outline-primary sm:text-base ${occupied ? "border-amber-500 bg-amber-100 text-amber-950 dark:bg-amber-900 dark:text-amber-50" : "border-teal-500 bg-teal-50 text-teal-950 dark:bg-teal-900 dark:text-teal-50"}`}
            style={{ left: `${object.x / 10}%`, top: `${object.y / 7}%`, width: `${object.width / 10}%`, height: `${object.height / 7}%`, rotate: `${object.rotation}deg`,
              borderRadius: floorPlanShapeRadius(object.shape),
              borderColor: object.color ?? undefined }}><FloorPlanTableSeats chairs={object.tableId && seatingByTable.has(object.tableId) ? 0 : object.chairs ?? table.capacity}/><span className="relative z-10">Mesa {table.code}{object.tableId && seatingByTable.has(object.tableId) ? <small className="block text-[10px] font-medium leading-tight sm:text-xs">{seatingByTable.get(object.tableId)?.chairs ?? 0} sillas · {seatingByTable.get(object.tableId)?.furniture ?? 0} muebles</small> : (object.chairs ?? table.capacity) ? <small className="block text-[10px] font-medium leading-tight sm:text-xs">{object.chairs ?? table.capacity} sillas</small> : null}</span></button>;
        })}
        <div className="absolute bottom-3 left-3 rounded-full border bg-background/90 px-3 py-1 text-xs font-medium shadow-sm">Verde: libre · Ámbar: ocupada</div>
      </div>}
      {activeFloorPlan && visibleTables.some((table) => !activeFloorPlan.objects.some((object) => object.tableId === table.tableId)) && <p className="mt-6 text-sm font-semibold">Mesas aún sin ubicar en el plano</p>}
      <div className="mt-5 grid gap-4 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4">{visibleTables.filter((table) => !activeFloorPlan || !activeFloorPlan.objects.some((object) => object.tableId === table.tableId)).map((table) =>
        <TableCard key={table.tableId} table={table} roomName={table.roomId ? roomNames.get(table.roomId) : undefined}
          occupancy={tableStates.get(table.tableId)} disabled={busy} onClick={() => selectTable(table)}/>)}</div>
      {(tables.data?.totalCount ?? 0) > 100 && <div className="mt-7 flex justify-center gap-3"><Button variant="outline" disabled={tablePage === 1} onClick={() => setTablePage((page) => page - 1)}>Anterior</Button><span className="self-center text-sm">Página {tablePage}</span><Button variant="outline" disabled={tablePage * 100 >= (tables.data?.totalCount ?? 0)} onClick={() => setTablePage((page) => page + 1)}>Siguiente</Button></div>}
    </section> : <section className="mx-auto max-w-[1700px] p-3 sm:p-5">
      <div className="grid gap-4 lg:grid-cols-[minmax(300px,30%)_minmax(0,70%)]">
        <aside className={`${mobileSection !== "account" ? "hidden lg:flex" : "flex"} h-auto min-h-0 flex-col self-start overflow-visible rounded-[2rem] border bg-card shadow-sm lg:sticky lg:top-24 lg:h-[calc(100dvh-8rem)] lg:min-h-[25rem] lg:overflow-hidden`}>
          <div className="shrink-0 border-b p-5">
            <p className="text-xs font-bold uppercase tracking-widest text-primary">Cuenta abierta · {selectedTable?.roomId ? roomNames.get(selectedTable.roomId) : "Sin salón"}</p>
            <h2 className="mt-1 text-2xl font-bold">Mesa {selectedTable?.code}</h2>
            <div className="mt-3 flex items-center justify-between gap-2"><div className="min-w-0">
              <p className="truncate text-sm font-semibold">Responsable: {draft.account.responsibleUserName || (draft.account.responsibleUserId === connectedUser?.userId ? [connectedUser?.firstName, connectedUser?.lastName].filter(Boolean).join(" ") || connectedUser?.username : "Mesero asignado")}</p>
              <p className="mt-1 truncate text-xs text-muted-foreground">Atendiendo ahora: {attention ? activeWaiterName || draft.account.responsibleUserName || "Mesero identificado" : [connectedUser?.firstName, connectedUser?.lastName].filter(Boolean).join(" ") || connectedUser?.username || "Usuario conectado"}</p>
            </div>{canOpenWithoutWaiter && selectedTable && <Button variant="outline" size="sm" className="min-h-11 shrink-0 rounded-xl" disabled={busy || !!pendingSend} onClick={() => selectTable(selectedTable, true)}>Cambiar mesero</Button>}</div>
            <div className="mt-3 space-y-2 border-t pt-3 text-sm">
              <div className="flex items-center justify-between gap-2"><p className="min-w-0 truncate"><span className="text-muted-foreground">Cliente:</span> <strong>{draft.account.customerName || "Consumidor final"}</strong></p><Button type="button" variant="ghost" size="sm" className="shrink-0 text-primary" disabled={busy || !!pendingSend} onClick={() => setCustomerOpen(true)}>{draft.account.customerId ? "Cambiar" : "Buscar"}</Button></div>
              <div className="flex items-center justify-between gap-2"><p className="min-w-0 truncate"><span className="text-muted-foreground">Canal de precio:</span> <strong>{draft.account.priceChannelName ?? "Sin canal"}</strong></p>{canSelectPriceChannel && <Button type="button" variant="ghost" size="sm" className="shrink-0 text-primary" disabled={busy || !!pendingSend || draft.account.customerPriceChannelLocked || priceChannels.isLoading} onClick={() => setChannelOpen(true)}>Cambiar</Button>}</div>
              {draft.account.customerPriceChannelLocked && <p className="text-xs text-muted-foreground">Este canal lo define el cliente seleccionado.</p>}
            </div>
          </div>
          <div className="flex-1 space-y-2 p-3 sm:p-4 lg:min-h-0 lg:overflow-y-auto lg:overscroll-contain">{!groups.length && <div className="grid min-h-52 place-items-center text-center text-muted-foreground"><div><Coffee className="mx-auto h-8 w-8"/><p className="mt-3 text-sm">Toca un producto para empezar el pedido.</p></div></div>}
            {groups.map((group, index) => <div key={`${group.productId}-${index}`} className="rounded-2xl border bg-background p-4"><div className="flex items-start justify-between gap-2"><div><strong className="text-base">{group.name}</strong><p className="mt-0.5 text-xs text-muted-foreground">{group.contributors.join(", ")}</p><p className={`mt-1 text-xs font-medium ${"text-muted-foreground"}`}>{group.sentQuantity > 0 && `${group.sentQuantity} enviadas · `}{group.pendingQuantity > 0 ? `${group.pendingQuantity} pendientes · ${group.stationName ?? "Sin estación"}` : group.sentQuantity > 0 ? group.stationName ?? "Estación no disponible" : "Sin comanda · se cobra directamente"}</p></div><span className="font-semibold">{money.format(group.amount)}</span></div>
              <div className="mt-3 flex items-center justify-between"><span className="text-sm font-semibold">{group.quantity} {group.quantity === 1 ? "unidad" : "unidades"}</span>{canAdd && <div className="flex items-center gap-2">{group.stationId === null && <Button aria-label={`Quitar uno de ${group.name}`} variant="outline" size="icon" className="h-11 w-11 rounded-xl" disabled={busy || !!pendingSend} onClick={() => { const lastDirect = group.items.filter((item) => item.status === "Direct").at(-1); if (lastDirect) void mutateProduct(group.productId, "remove", lastDirect.additionId); }}><Minus className="h-5 w-5"/></Button>}<Button aria-label={`Agregar uno de ${group.name}`} size="icon" className="h-11 w-11 rounded-xl" disabled={busy || !!pendingSend} onClick={() => void mutateProduct(group.productId, "plus")}><Plus className="h-5 w-5"/></Button></div>}</div>
              {group.stationId !== null && <div className="mt-3 space-y-2 border-t pt-3">{group.items.map((item, itemIndex) => <div key={item.additionId} className="flex items-start justify-between gap-2 rounded-xl bg-muted/50 px-3 py-2 text-sm"><div className="min-w-0"><p className="font-semibold">{item.quantity} × {group.name} <span className="font-normal text-muted-foreground">· {item.status === "Sent" ? "Enviada" : "Pendiente"}</span></p>{item.preparationNote && <p className="mt-1 whitespace-pre-wrap text-primary"><MessageSquareText className="mr-1 inline h-4 w-4"/>{item.preparationNote}</p>}{cancellationsByAddition.get(item.additionId) && <p className="mt-1 text-xs font-bold text-red-700 dark:text-red-300">Cancelación en revisión</p>}</div>{item.status === "Pending" && canAdd ? <div className="flex shrink-0 flex-wrap justify-end gap-2"><Button size="sm" variant="outline" className="min-h-10 rounded-xl" disabled={busy || !!pendingSend} onClick={() => { setNoteAdditionId(item.additionId); setNoteText(item.preparationNote ?? ""); }}><MessageSquareText className="mr-1 h-4 w-4"/>{item.preparationNote ? "Editar" : "Observación"} <span className="sr-only">de la unidad {itemIndex + 1}</span></Button><Button size="icon" variant="outline" className="h-10 w-10 rounded-xl border-rose-200 text-rose-700 hover:bg-rose-50 hover:text-rose-800 dark:border-rose-900 dark:text-rose-300" aria-label={`Eliminar producto pendiente ${group.name}, incorporación ${itemIndex + 1}`} title="Eliminar producto pendiente" disabled={busy || !!pendingSend} onClick={() => void mutateProduct(group.productId, "remove", item.additionId)}><Trash2 className="h-5 w-5"/></Button></div> : item.status === "Sent" && canAdd && !cancellationsByAddition.has(item.additionId) && <Button size="sm" variant="outline" className="min-h-10 shrink-0 rounded-xl" disabled={busy || !!pendingSend} onClick={() => { setCancelAdditionId(item.additionId); setCancelQuantity(1); setCancelReasonCode(""); setCancelExplanation(""); setCancelRequestId(null); }}>Corregir</Button>}</div>)}</div>}
              {group.sentQuantity > 0 && group.pendingQuantity === 0 && canAdd && <p className="mt-2 text-xs text-muted-foreground">Toca + para agregar más y enviar una nueva comanda.</p>}{group.pending.length > 0 && canAdd && <Button className="mt-3 min-h-12 w-full rounded-xl bg-teal-600 font-bold text-white shadow-sm transition hover:bg-teal-700 hover:shadow-md dark:bg-teal-500 dark:text-slate-950 dark:hover:bg-teal-400" disabled={busy || !!pendingSend && pendingSend.additionId !== group.pending[0].additionId} onClick={() => void sendPending(group.pending[0].additionId)}><ChefHat className="mr-2 h-5 w-5"/>{group.pending.length > 1 ? `Enviar 1 de ${group.pending.length} cantidades pendientes` : "Enviar este producto"}</Button>}</div>)}</div>
          <div className="shrink-0 border-t bg-card p-5">
            <div className="flex items-center justify-between text-sm"><span className="text-muted-foreground">Consumo estimado</span><strong className="text-2xl">{money.format(estimatedTotal)}</strong></div>
            {canAdd && (pendingCount > 0 || pendingSend) && <Button className="mt-4 min-h-14 w-full rounded-2xl bg-teal-700 text-base font-bold text-white shadow-md shadow-teal-900/15 hover:bg-teal-800 dark:bg-teal-500 dark:text-slate-950 dark:hover:bg-teal-400" disabled={busy || !!pendingSend?.additionId} onClick={() => void sendPending()}><ChefHat className="mr-2 h-5 w-5"/>{pendingSend ? "Reintentar envío" : `Enviar pendientes · ${pendingCount}`}</Button>}
            {activeCancellations.length > 0 && <div className="mt-4 rounded-xl border border-amber-400/60 bg-amber-50 p-3 text-sm text-amber-950 dark:bg-amber-950/40 dark:text-amber-100"><p className="font-bold">{activeCancellations.length} cancelación(es) por resolver</p><p className="mt-1">La precuenta y la factura quedan en espera hasta confirmar el consumo.</p><Button size="sm" variant="outline" className="mt-2" disabled={busy} onClick={() => void refreshCancellations()}>Actualizar estado</Button></div>}
            {canAdd && availableReworks.map((cancellation) => {
              const original = draft.additions.find((item) => item.additionId === cancellation.additionId);
              if (!original) return null;
              return <div key={cancellation.cancellationId} className="mt-3 rounded-xl border border-teal-300 bg-teal-50 p-3 text-sm text-teal-950 dark:border-teal-800 dark:bg-teal-950/40 dark:text-teal-100"><p className="font-bold">{original.description} · {cancellation.quantity} cancelada(s)</p><p className="mt-1">El primer intento ya quedó registrado como pérdida. Puedes preparar otro sin cobrar ambos.</p><Button size="sm" variant="outline" className="mt-3 min-h-10 border-teal-500" disabled={busy || !!pendingSend} onClick={() => void reworkProduct(cancellation)}>Rehacer plato</Button></div>;
            })}
            {cancellations.isError && <p role="alert" className="mt-3 text-sm text-destructive">No se pudo verificar el estado de las cancelaciones. Vuelve a abrir la mesa.</p>}
            {(canAdd || permissions.includes("sales.create")) && draft.account.status === "Open" && <div className="mt-4 flex items-center gap-3 rounded-2xl border bg-muted/20 p-3"><span className="grid h-11 w-11 shrink-0 place-items-center rounded-xl bg-background text-primary shadow-sm"><FileText className="h-5 w-5"/></span><div className="min-w-0 flex-1"><p className="text-sm font-semibold">Precuenta</p><p className="text-xs text-muted-foreground">Para mostrar al cliente · no es factura</p></div><Button size="sm" variant="outline" className="shrink-0 rounded-xl" disabled={busy || !!pendingSend || pendingCount > 0 || activeCancellations.length > 0 || cancellations.isLoading || cancellations.isError || !groups.length} onClick={() => void preparePrebill()}>Imprimir</Button></div>}
            {permissions.includes("sales.create") && option.workSessionId && draft.account.status === "Open" && <Button className="mt-3 min-h-14 w-full justify-between rounded-2xl bg-slate-900 px-5 text-base font-bold text-white shadow-lg shadow-slate-900/15 hover:bg-slate-800 disabled:border disabled:border-dashed disabled:border-slate-300 disabled:bg-slate-100 disabled:text-slate-500 disabled:opacity-100 disabled:shadow-none dark:bg-emerald-100 dark:text-emerald-950 dark:hover:bg-white dark:disabled:border-slate-700 dark:disabled:bg-slate-800 dark:disabled:text-slate-400" disabled={busy || !!pendingSend || pendingCount > 0 || activeCancellations.length > 0 || cancellations.isLoading || cancellations.isError || !groups.length} onClick={() => void openCheckout()}><span>Facturar mesa</span><ArrowRight className="h-5 w-5"/></Button>}
            {pendingCount > 0 && permissions.includes("sales.create") && <p className="mt-2 text-xs text-amber-700 dark:text-amber-300">Envía o retira los pendientes antes de facturar.</p>}
            <p className="mt-2 text-xs text-muted-foreground">Solo los productos con estación se envían a comanda. Los demás se cobran directamente.</p>
          </div>
        </aside>
        <div className={`${mobileSection !== "products" ? "hidden lg:block" : "block"} min-h-[32rem] min-w-0 rounded-[2rem] border bg-card p-4 shadow-sm sm:p-6 lg:min-h-0`}>
          <div className="flex flex-wrap items-center justify-between gap-3"><div><p className="text-xs font-bold uppercase tracking-widest text-primary">Menú</p><h2 className="mt-1 text-2xl font-bold">Elige productos</h2></div><div className="relative w-full sm:w-64"><Search className="absolute left-4 top-3.5 h-5 w-5 text-muted-foreground"/><Input className="h-12 rounded-2xl pl-11" value={productSearch} onChange={(event) => { setProductSearch(event.target.value); setCategoryId("all"); setProductPage(0); }} placeholder="Buscar producto" aria-label="Buscar producto"/></div></div>
          {canAdd ? <><div className="mt-5 flex gap-2 overflow-x-auto pb-2" role="group" aria-label="Categorías del menú"><CategoryPill active={categoryId === "all"} label="Todos" icon={LayoutGrid} onClick={() => { setCategoryId("all"); setProductPage(0); }}/>{(categories.data ?? []).map((category) => <CategoryPill key={category.categoryId} active={categoryId === category.categoryId} label={category.name} count={category.productCount} icon={categoryIcon(category.name)} onClick={() => { setCategoryId(category.categoryId); setProductPage(0); }}/>)}</div>
            {products.isLoading && <div className="mt-6 grid gap-3 sm:grid-cols-2 xl:grid-cols-4">{Array.from({ length: 8 }, (_, index) => <div key={index} className="h-52 animate-pulse rounded-3xl bg-muted"/>)}</div>}
            {products.isError && <div role="alert" className="mt-6 rounded-2xl border p-5">No pudimos cargar los productos. <Button variant="link" onClick={() => void products.refetch()}>Reintentar</Button></div>}
            {!products.isLoading && !products.isError && !products.data?.items.length && <div className="mt-8 rounded-2xl border border-dashed p-8 text-center text-muted-foreground">No hay productos en esta categoría o búsqueda.</div>}
            <div className="mt-5 grid grid-cols-1 gap-3 min-[430px]:grid-cols-2 md:grid-cols-3 2xl:grid-cols-4">{products.data?.items.map((product) => <ProductTile key={product.productId} product={product} disabled={busy || !!pendingSend} loading={addingProductId === product.productId} onClick={() => void mutateProduct(product.productId, "plus")}/>)}</div>
            {products.data?.hasMore && <Button variant="outline" className="mt-5 min-h-12 w-full" onClick={() => setProductPage((page) => page + 1)}>Más productos</Button>}
          </> : <div className="mt-8 rounded-2xl border bg-muted/30 p-7 text-center"><ChefHat className="mx-auto h-8 w-8 text-primary"/><p className="mt-3 font-medium">Cuenta en consulta</p><p className="mt-1 text-sm text-muted-foreground">No tienes permiso para agregar productos a esta mesa.</p></div>}
        </div>
      </div>
    </section>}
    <Dialog open={customerOpen} onOpenChange={setCustomerOpen}>
      <DialogContent className="max-h-[85dvh] max-w-lg overflow-y-auto rounded-3xl">
        <DialogHeader><DialogTitle>Cliente de la mesa</DialogTitle><DialogDescription>Busca por nombre o identificación. Su canal se aplicará a toda la cuenta.</DialogDescription></DialogHeader>
        <PartyRoleSelect role="Customer" value={draft?.account.customerId ?? ""}
          selectedOption={draft?.account.customerId ? { value: draft.account.customerId, label: draft.account.customerName || "Cliente" } : null}
          placeholder="Buscar cliente" preload={customerOpen} disabled={busy}
          onChange={(value) => void changeTableCustomer(value || null)}/>
        {draft?.account.customerId && <Button variant="outline" className="min-h-12 justify-start" disabled={busy} onClick={() => void changeTableCustomer(null)}>Quitar cliente · consumidor final</Button>}
      </DialogContent>
    </Dialog>
    <Dialog open={channelOpen} onOpenChange={setChannelOpen}>
      <DialogContent className="max-w-md rounded-3xl"><DialogHeader><DialogTitle>Canal de precio</DialogTitle><DialogDescription>El cambio ajusta los productos de esta mesa antes de cobrar.</DialogDescription></DialogHeader>
        <div className="space-y-2"><Button type="button" variant={draft?.account.priceChannelId ? "outline" : "default"} className="min-h-12 w-full justify-start rounded-xl" disabled={busy} onClick={() => void changeTableChannel(null)}>Sin canal · precio de mesa</Button>
          {priceChannels.data?.map((channel) => <Button type="button" key={channel.priceChannelId} variant={draft?.account.priceChannelId === channel.priceChannelId ? "default" : "outline"} className="min-h-12 w-full justify-start rounded-xl" disabled={busy} onClick={() => void changeTableChannel(channel.priceChannelId)}>{channel.name}</Button>)}
        </div>
      </DialogContent>
    </Dialog>
    {editingAddition && <div className="fixed inset-0 z-50 grid place-items-center bg-black/50 p-4" role="dialog" aria-modal="true" aria-label="Observación del producto"><div className="w-full max-w-lg rounded-[2rem] border bg-card p-6 shadow-2xl"><p className="text-xs font-bold uppercase tracking-widest text-primary">Indicación para preparación</p><h2 className="mt-1 text-xl font-bold">{editingAddition.description}</h2><p className="mt-1 text-sm text-muted-foreground">Esta observación corresponde solo a esta incorporación. Para indicaciones distintas, agrega otra unidad.</p><textarea autoFocus maxLength={500} value={noteText} onChange={(event) => setNoteText(event.target.value)} placeholder="Ej. Sin cebolla, salsa aparte…" className="mt-5 min-h-32 w-full resize-y rounded-2xl border bg-background p-4 text-base outline-none focus-visible:ring-2 focus-visible:ring-primary"/><div className="mt-2 text-right text-xs text-muted-foreground">{noteText.length}/500</div><div className="mt-5 flex justify-end gap-2"><Button variant="outline" disabled={busy} onClick={() => setNoteAdditionId(null)}>Cancelar</Button><Button disabled={busy || !!pendingSend} onClick={() => void saveNote()}>{busy ? "Guardando…" : "Guardar observación"}</Button></div></div></div>}
        {cancellingAddition && <div className="fixed inset-0 z-50 grid place-items-center bg-black/50 p-4" role="dialog" aria-modal="true" aria-label="Solicitar cancelación"><div className="w-full max-w-lg rounded-[2rem] border bg-card p-6 shadow-2xl"><p className="text-xs font-bold uppercase tracking-widest text-red-700 dark:text-red-300">Corrección de comanda</p><h2 className="mt-1 text-xl font-bold">{cancellingAddition.description}</h2><p className="mt-1 text-sm text-muted-foreground">Cocina o barra confirmará si ya se preparó. Hasta resolverlo, no se puede cobrar esta mesa.</p><label className="mt-5 block text-sm font-semibold">Cantidad a retirar<Input className="mt-2 h-12" type="number" min="1" max={cancellingAddition.quantity} step={cancellingAddition.allowsFractionalSale ? "0.0001" : "1"} value={cancelQuantity} onChange={(event) => { setCancelQuantity(Number(event.target.value)); setCancelRequestId(null); }}/></label><div className="mt-4"><p id="restaurant-cancellation-reason-label" className="text-sm font-semibold">Motivo</p><Select value={cancelReasonCode || undefined} onValueChange={(value) => { setCancelReasonCode(value); setCancelRequestId(null); }}><SelectTrigger aria-labelledby="restaurant-cancellation-reason-label" className="mt-2 h-14 rounded-2xl bg-background px-4 text-base"><SelectValue placeholder="Selecciona un motivo"/></SelectTrigger><SelectContent className="z-[60] rounded-2xl">{cancellationReasons.data?.filter((reason) => reason.isActive).map((reason) => <SelectItem className="min-h-12 text-base" key={reason.code} value={reason.code}>{reason.name}</SelectItem>)}</SelectContent></Select></div>{cancellationReasons.isError && <p role="alert" className="mt-2 text-sm text-destructive">No se pudieron cargar los motivos.</p>}<label className="mt-4 block text-sm font-semibold">¿Qué pasó?<textarea className="mt-2 min-h-24 w-full resize-y rounded-xl border bg-background p-3" maxLength={500} value={cancelExplanation} onChange={(event) => { setCancelExplanation(event.target.value); setCancelRequestId(null); }} placeholder="Explica la corrección para la estación y el supervisor"/></label><div className="mt-5 flex justify-end gap-2"><Button variant="outline" disabled={busy} onClick={() => { setCancelAdditionId(null); setCancelRequestId(null); }}>Volver</Button><Button disabled={busy || !cancelReasonCode || !cancelExplanation.trim() || cancelQuantity <= 0 || cancelQuantity > cancellingAddition.quantity || !cancellingAddition.allowsFractionalSale && !Number.isInteger(cancelQuantity)} onClick={() => void requestCancellation()}>{busy ? "Enviando…" : "Solicitar cancelación"}</Button></div></div></div>}
    {checkoutPreview && collectionSummary && paymentClient && draft &&
      option.workSessionId && <RestaurantPaymentDialog
      draft={draft} summary={collectionSummary} client={paymentClient}
      workSessionId={option.workSessionId}
      busy={busy} documentType={checkoutDocumentType}
      documentTypeBlockReason={documentTypeBlockReason}
      onChangeDocumentType={setCheckoutDocumentType}
      onClose={() => { if (!busy) { setCheckoutPreview(null); setCheckoutKey(null); setCollectionSummary(null); } }}
      onCapture={captureCollection} onReverse={reverseCollection}
      onFinalize={completeCheckout}/>}
    {prebillPreview && <div className="fixed inset-0 z-50 grid place-items-center bg-black/60 p-4" role="dialog" aria-modal="true" aria-label="Preparar precuenta">
      <div className="w-full max-w-md rounded-3xl bg-card p-6 shadow-2xl">
        <p className="text-xs font-bold uppercase tracking-widest text-primary">Mesa {prebillPreview.tableCode}</p>
        <h2 className="mt-1 text-2xl font-bold">Precuenta</h2>
        <p className="mt-2 text-sm text-muted-foreground">El cliente decide si deja propina. La factura registra solamente el consumo.</p>
        <div className="mt-5 rounded-2xl border p-4"><div className="flex justify-between font-semibold"><span>Consumo</span><span>{money.format(prebillPreview.total)}</span></div>
          {prebillPreview.suggestedTipPercent > 0 && <><div className="mt-3 flex justify-between text-sm"><span>Propina sugerida · {prebillPreview.suggestedTipPercent} %</span><span>{money.format(prebillPreview.suggestedTipAmount)}</span></div><div className="mt-3 flex justify-between border-t pt-3 text-lg font-bold"><span>Total sugerido</span><span>{money.format(prebillPreview.total + prebillPreview.suggestedTipAmount)}</span></div></>}
        </div>
        <div className="mt-5 grid gap-2 sm:grid-cols-2"><Button variant="outline" className="min-h-12" onClick={() => printPrebill(prebillPreview, false)}>Sin propina</Button>
          {prebillPreview.suggestedTipPercent > 0 && <Button className="min-h-12" onClick={() => printPrebill(prebillPreview, true)}>Incluir {prebillPreview.suggestedTipPercent} %</Button>}</div>
        <Button variant="ghost" className="mt-2 w-full" onClick={() => setPrebillPreview(null)}>Cerrar</Button>
      </div>
    </div>}
    {printerOpen && <PosPrinterDialog client={null} browserOnly onClose={() => setPrinterOpen(false)}/>}
    {pendingTable && <div className="fixed inset-0 z-50 grid place-items-center bg-black/50 p-4" role="dialog" aria-modal="true" aria-label="Identificar mesero">
      <div className="w-full max-w-lg rounded-[2rem] border bg-card p-5 shadow-2xl sm:p-7">
        <div className="flex items-start justify-between gap-3"><div><p className="text-xs font-bold uppercase tracking-widest text-primary">Atención de mesa</p><h2 className="mt-1 text-2xl font-bold">Mesa {pendingTable.code}</h2><p className="mt-1 text-sm text-muted-foreground">{canOpenWithoutWaiter ? "Entra con tu usuario para consultar la cuenta. Si seleccionas un mesero, los productos nuevos quedarán a su nombre." : "Selecciona quién va a tomar este pedido."}</p></div><Button variant="ghost" onClick={() => setPendingTable(null)}>Cerrar</Button></div>
        <div className="mt-5 flex gap-2"><Input className="h-12" value={waiterSearch} onChange={(event) => setWaiterSearch(event.target.value)} onKeyDown={(event) => { if (event.key === "Enter") setWaiterSearchTerm(waiterSearch.trim()); }} placeholder="Buscar mesero" aria-label="Buscar mesero" autoFocus /><Button variant="outline" className="min-h-12" onClick={() => setWaiterSearchTerm(waiterSearch.trim())}>Buscar</Button></div>
        <div className="mt-3 max-h-64 space-y-2 overflow-y-auto">{waiters.isLoading && <p className="p-4 text-sm text-muted-foreground">Cargando meseros…</p>}{waiters.isError && <p role="alert" className="p-4 text-sm text-destructive">No se pudieron cargar los meseros.</p>}{waiters.data?.items.map((waiter) => <button key={waiter.userId} type="button" onClick={() => setWaiterUserId(waiter.userId)} className={`flex min-h-14 w-full items-center rounded-2xl border px-4 text-left font-semibold transition ${waiterUserId === waiter.userId ? "border-primary bg-primary/10 text-primary" : "hover:border-primary/50"}`}>{waiter.shortName || waiter.name}</button>)}</div>
        {waiterUserId && waiters.data?.requireCredential && <label className="mt-5 block text-sm font-medium">Tu clave<Input ref={waiterSecretInput} type="password" autoComplete="off" className="mt-2 h-14 text-center text-xl tracking-widest" value={waiterSecret} onChange={(event) => setWaiterSecret(event.target.value)} placeholder="Principal o secundaria" /></label>}
        <Button className="mt-6 min-h-14 w-full text-base" disabled={busy || !canOpenWithoutWaiter && (!waiterUserId || waiters.isLoading) || !!waiterUserId && !!waiters.data?.requireCredential && !waiterSecret} onClick={() => void beginAttention()}>{busy ? "Abriendo…" : "Abrir mesa"}</Button>
      </div>
    </div>}
  </main>;
}

function TableCard({ table, roomName, occupancy, disabled, onClick }: {
  table: RestaurantTable;
  roomName?: string;
  occupancy?: RestaurantTableOccupancy;
  disabled: boolean;
  onClick: () => void;
}) {
  const occupied = !!occupancy?.accountId && occupancy.state !== "Free";
  return <button disabled={disabled} onClick={onClick} className="group min-h-52 rounded-[1.75rem] border bg-card p-5 text-left shadow-sm transition hover:-translate-y-0.5 hover:border-primary/60 hover:shadow-md focus-visible:outline-2 focus-visible:outline-primary disabled:opacity-60">
    <div className="flex items-start justify-between gap-2"><div className={`grid h-14 w-14 place-items-center rounded-2xl ${occupied ? "bg-amber-500/10 text-amber-700 dark:text-amber-300" : "bg-emerald-500/10 text-emerald-700 dark:text-emerald-300"}`}><LayoutGrid className="h-7 w-7"/></div><span className={`rounded-full px-3 py-1 text-xs font-bold ${occupied ? "bg-amber-500/10 text-amber-700 dark:text-amber-300" : "bg-emerald-500/10 text-emerald-700 dark:text-emerald-300"}`}>{occupied ? occupancy?.state === "Invoiced" ? "Pagada" : "Ocupada" : "Libre"}</span></div>
    <h3 className="mt-5 text-3xl font-bold">{table.code}</h3><p className="mt-1 text-sm text-muted-foreground">{roomName ?? "Sin salón"}{table.capacity ? ` · ${table.capacity} personas` : ""}</p>
    <div className="mt-5 flex items-center gap-2 text-sm font-semibold text-primary">{occupied ? <><Clock3 className="h-4 w-4"/>Ver cuenta</> : <><CircleCheck className="h-4 w-4"/>Abrir mesa</>}</div>
  </button>;
}

function categoryIcon(name: string): LucideIcon {
  const value = name.toLocaleLowerCase("es");
  if (/bebida|coctel|cóctel|jugo|licor|bar/.test(value)) return GlassWater;
  if (/café|cafe|té|te\b/.test(value)) return Coffee;
  if (/postre|dulce|torta|pastel/.test(value)) return CakeSlice;
  if (/hamburgues|sándwich|sandwich|pan/.test(value)) return Sandwich;
  if (/pescado|marisco/.test(value)) return Fish;
  if (/ensalada|vegetal/.test(value)) return Salad;
  if (/entrada|sopa/.test(value)) return Soup;
  return UtensilsCrossed;
}

function CategoryPill({ active, label, count, icon: Icon, onClick }: {
  active: boolean; label: string; count?: number; icon: LucideIcon;
  onClick: () => void;
}) {
  return <button type="button" onClick={onClick} className={`flex min-h-14 shrink-0 items-center gap-2.5 rounded-2xl border px-3.5 py-2 text-sm font-semibold shadow-sm transition active:scale-[.98] ${active ? "border-primary bg-primary text-primary-foreground shadow-primary/20" : "bg-background hover:border-primary/60 hover:bg-primary/5 hover:shadow-md"}`}>
    <span className={`grid h-8 w-8 place-items-center rounded-xl ${active ? "bg-white/20" : "bg-primary/10 text-primary"}`}><Icon className="h-[18px] w-[18px]"/></span>
    <span>{label}</span>{count !== undefined && <span className={`rounded-lg px-2 py-0.5 text-xs ${active ? "bg-white/20" : "bg-muted text-muted-foreground"}`}>{count}</span>}
  </button>;
}

function ProductTile({ product, disabled, loading, onClick }: {
  product: RestaurantProduct; disabled: boolean; loading: boolean; onClick: () => void;
}) {
  const [imageFailed, setImageFailed] = useState(false);
  return <button disabled={disabled} onClick={onClick} aria-label={`Agregar ${product.name}`}
    className="group min-h-52 overflow-hidden rounded-3xl border bg-background text-left shadow-sm transition hover:-translate-y-0.5 hover:border-primary/70 hover:shadow-md focus-visible:outline-2 focus-visible:outline-primary disabled:opacity-60">
    <div className="relative grid min-h-32 aspect-[4/3] place-items-center overflow-hidden bg-gradient-to-br from-emerald-50 via-teal-50 to-slate-100 text-primary dark:from-emerald-950 dark:via-teal-950 dark:to-slate-900">
      {product.imageUrl && !imageFailed
        // eslint-disable-next-line @next/next/no-img-element
        ? <img src={product.imageUrl} alt="" className="h-full w-full object-cover transition duration-300 group-hover:scale-105" onError={() => setImageFailed(true)}/>
        : <UtensilsCrossed className="h-14 w-14 opacity-50" aria-hidden="true"/>}
      <span className="absolute right-3 top-3 grid h-10 w-10 place-items-center rounded-full bg-background/90 text-primary shadow">{loading ? <Loader2 className="h-5 w-5 animate-spin"/> : <Plus className="h-5 w-5"/>}</span>
    </div><div className="p-4"><p className="line-clamp-2 min-h-11 text-sm font-bold sm:text-base">{product.name}</p><p className="mt-2 text-lg font-extrabold text-primary">{money.format(product.unitPrice)}</p></div>
  </button>;
}
