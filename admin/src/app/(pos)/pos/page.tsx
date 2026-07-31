"use client";

import {
  AlertTriangle,
  Banknote,
  Barcode,
  CheckCircle2,
  Clock3,
  ClipboardList,
  Loader2,
  LogOut,
  Percent,
  Printer,
  RotateCcw,
  Save,
  Settings2,
  Search,
  Trash2,
  UserRound,
  Wifi,
  WifiOff,
  X,
} from "lucide-react";
import { FormEvent, useCallback, useEffect, useRef, useState } from "react";

import { OrdersWorkspace } from "@/components/orders/orders-workspace";
import {
  PosCatalogProduct,
  PosCustomer,
  PosDraft,
  PosDocumentNumberPreview,
  PosIssuedSaleSummary,
  PosPaymentInput,
  PosEdgeClient,
  PosEdgeError,
  type PosClient,
  readEdgeTokenFromLaunch,
  readEdgeUserSession,
} from "@/services/pos/pos-edge-client";
import { loadOnlineRegisterBootstrap } from "@/services/pos/online-pos-bootstrap";
import {
  forgetOnlineRegister,
  OnlinePosClient,
  type OnlineRegisterOption,
  rememberedOnlineRegisterId,
  selectOnlineRegister,
} from "@/services/pos/online-pos-client";
import {
  authorizePosEnrollment,
  redeemPosEnrollment,
} from "@/services/pos/pos-enrollment";
import { PosConfirmDialog } from "./pos-confirm-dialog";
import { PosCustomerSearchDialog } from "./pos-customer-search-dialog";
import { PosDiscountDialog } from "./pos-discount-dialog";
import { PosExitMenuButton } from "./pos-exit-menu-button";
import { PosInvoiceSearchDialog } from "./pos-invoice-search-dialog";
import { PosLocalLogin } from "./pos-local-login";
import { PosOnlineSetup } from "./pos-online-setup";
import { PosPaymentDialog } from "./pos-payment-dialog";
import {
  shouldShowCashChange,
  type PosPaymentSettlement,
} from "./pos-payment-settlement";
import { PosProductSearchDialog } from "./pos-product-search-dialog";


const money = new Intl.NumberFormat("es-CO", {
  style: "currency",
  currency: "COP",
  maximumFractionDigits: 0,
});

export default function PosPage() {
  const scanner = useRef<HTMLInputElement>(null);
  const quantityInputs = useRef(new Map<string, HTMLInputElement>());
  const skipQuantityBlur = useRef<string | null>(null);
  const [client, setClient] = useState<PosClient | null>(null);
  const [onlineOptions, setOnlineOptions] = useState<OnlineRegisterOption[]>([]);
  const [onlineTenantName, setOnlineTenantName] = useState("");
  const [onlineUserName, setOnlineUserName] = useState("");
  const [onlineUserId, setOnlineUserId] = useState("");
  const [edgeEnrollmentToken, setEdgeEnrollmentToken] = useState<string | null>(null);
  const [edgeEnrollmentRequired, setEdgeEnrollmentRequired] = useState(false);
  const [edgeLoginState, setEdgeLoginState] = useState<"preparing" | "required" | null>(null);
  const [edgeLoginError, setEdgeLoginError] = useState<string | null>(null);
  const [setupLoading, setSetupLoading] = useState(true);
  const [setupError, setSetupError] = useState<string | null>(null);
  const [draft, setDraft] = useState<PosDraft | null>(null);
  const [temporaries, setTemporaries] = useState<PosDraft[]>([]);
  const [scan, setScan] = useState("");
  const [busy, setBusy] = useState(false);
  const [edgeReady, setEdgeReady] = useState(false);
  const [serverConnected, setServerConnected] = useState(false);
  const [workstation, setWorkstation] = useState({
    registerCode: "—",
    userDisplayName: "—",
    userId: null as string | null,
  });
  const [message, setMessage] = useState("Esperando producto");
  const [error, setError] = useState<string | null>(null);
  const [temporaryOpen, setTemporaryOpen] = useState(false);
  const [temporaryName, setTemporaryName] = useState("");
  const [paymentOpen, setPaymentOpen] = useState(false);
  const [productSearchOpen, setProductSearchOpen] = useState(false);
  const [customerSearchOpen, setCustomerSearchOpen] = useState(false);
  const [discountOpen, setDiscountOpen] = useState(false);
  const [invoiceSearchOpen, setInvoiceSearchOpen] = useState(false);
  const [selectedCustomer, setSelectedCustomer] = useState<PosCustomer | null>(null);
  const [sidePanel, setSidePanel] = useState<"temporaries" | "orders">("temporaries");
  const [ordersExpanded, setOrdersExpanded] = useState(false);
  const [selectedLineId, setSelectedLineId] = useState<string | null>(null);
  const [confirmation, setConfirmation] = useState<
    | { kind: "line"; lineId: string; productName: string }
    | { kind: "temporary"; draftId: string; name: string }
    | { kind: "sale" }
    | null
  >(null);
  const [lastSettlement, setLastSettlement] = useState<{
    documentId: string;
    documentNumber: string;
    received: number;
    change: number;
  } | null>(null);
  const [nextNumber, setNextNumber] = useState<PosDocumentNumberPreview | null>(null);
  const [temporaryReference, setTemporaryReference] = useState("");
  const hasSelectedLine = Boolean(
    selectedLineId && draft?.lines.some((line) => line.lineId === selectedLineId),
  );
  const showCashChange = lastSettlement ? shouldShowCashChange(lastSettlement) : false;

  const focusScanner = useCallback(() => {
    window.requestAnimationFrame(() => scanner.current?.focus());
  }, []);

  const refreshTemporaries = useCallback(async () => {
    if (!client) return;
    setTemporaries(await client.temporaries());
  }, [client]);

  useEffect(() => {
    if ("serviceWorker" in navigator) {
      void navigator.serviceWorker.register("/pos-sw.js", { scope: "/pos" });
    }
    let active = true;
    const bootstrap = async () => {
      setSetupLoading(true);
      setSetupError(null);
      try {
        const edgeToken = readEdgeTokenFromLaunch();
        let requiresEnrollment = false;
        if (edgeToken) {
          try {
            const edgeClient = new PosEdgeClient(edgeToken, readEdgeUserSession());
            const health = await edgeClient.health();
            if (health.status !== "EnrollmentRequired") {
              if (active) {
                setEdgeEnrollmentToken(edgeToken);
                setServerConnected(health.serverConnected);
                setWorkstation({
                  registerCode: health.registerCode,
                  userDisplayName: health.userDisplayName || "—",
                  userId: health.userId,
                });
                setEdgeLoginState(
                  health.status === "IdentitySynchronizing"
                    ? "preparing"
                    : health.status === "LoginRequired"
                      ? "required"
                      : null,
                );
                setClient(edgeClient);
              }
              return;
            }
            requiresEnrollment = true;
            if (active) setEdgeEnrollmentToken(edgeToken);
          } catch {
            // If the host is absent, the connected web POS remains available.
          }
        }
        const serverBootstrap = await loadOnlineRegisterBootstrap();
        if (!active) return;
        setEdgeEnrollmentRequired(
          requiresEnrollment && serverBootstrap.canEnrollPosDevice,
        );
        const displayName =
          serverBootstrap.userDisplayName.trim() || "Cajero";
        setOnlineTenantName(serverBootstrap.tenantName.trim());
        setOnlineUserName(displayName);
        setOnlineUserId(serverBootstrap.userId);
        const available = serverBootstrap.options;
        setOnlineOptions(available);
        const remembered = rememberedOnlineRegisterId();
        const selected = available.find(
          (option) => option.registerId === remembered,
        );
        if (selected && !requiresEnrollment) {
          window.localStorage.setItem("selected_business_id", selected.businessId);
          const context = await selectOnlineRegister(selected);
          if (active) setClient(new OnlinePosClient(context, serverBootstrap.userId, displayName));
        }
      } catch (caught) {
        if (!active) return;
        setSetupError(
          caught instanceof Error
            ? caught.message
            : "No fue posible preparar las cajas disponibles.",
        );
      } finally {
        if (active) setSetupLoading(false);
      }
    };

    void bootstrap();
    return () => {
      active = false;
    };
  }, []);

  useEffect(() => {
    if (!client) return;
    let active = true;
    let checking = false;
    let hydrated = false;

    const connect = async () => {
      if (checking) return;
      checking = true;
      try {
        const health = await client.health();
        if (active) {
          setServerConnected(health.serverConnected);
          setWorkstation({
            registerCode: health.registerCode,
            userDisplayName: health.userDisplayName || "—",
            userId: health.userId,
          });
        }
        if (
          client.mode === "edge" &&
          (health.status === "IdentitySynchronizing" || health.status === "LoginRequired")
        ) {
          if (active) {
            setEdgeReady(false);
            setEdgeLoginState(
              health.status === "IdentitySynchronizing" ? "preparing" : "required",
            );
          }
          return;
        }
        if (client.mode === "edge" && health.status === "Synchronizing") {
          if (active) {
            setEdgeReady(false);
            setEdgeLoginState(null);
            setMessage("Sincronizando catálogo inicial…");
          }
          return;
        }
        if (active && client.mode === "edge") setEdgeLoginState(null);
        if (!hydrated) {
          const [current, pending, numbers] = await Promise.all([
            client.activeDraft(),
            client.temporaries(),
            client.nextNumbers(),
          ]);
          if (!active) return;
          setDraft(current);
          setTemporaries(pending);
          setNextNumber(numbers?.document ?? null);
          setSelectedCustomer(
            current.customerId
              ? await client.customer(current.customerId)
              : null,
          );
          hydrated = true;
          focusScanner();
        }
        if (active) setEdgeReady(true);
      } catch (caught) {
        hydrated = false;
        if (active) {
          setEdgeReady(false);
          setServerConnected(false);
          if (client.mode === "edge" && caught instanceof PosEdgeError && caught.status === 401) {
            setEdgeLoginState("required");
          }
        }
      } finally {
        checking = false;
      }
    };

    void connect();
    const interval =
      client.mode === "edge"
        ? window.setInterval(() => void connect(), 3_000)
        : null;
    const handleOnline = () => void connect();
    const handleOffline = () => {
      if (client.mode === "online") {
        setServerConnected(false);
        setEdgeReady(false);
      }
    };
    window.addEventListener("online", handleOnline);
    window.addEventListener("offline", handleOffline);
    return () => {
      active = false;
      if (interval !== null) window.clearInterval(interval);
      window.removeEventListener("online", handleOnline);
      window.removeEventListener("offline", handleOffline);
    };
  }, [client, focusScanner]);


  useEffect(() => {
    const handleShortcut = (event: KeyboardEvent) => {
      if (invoiceSearchOpen) return;
      if (
        event.key === "F6" &&
        !busy &&
        !temporaryOpen &&
        !productSearchOpen &&
        !customerSearchOpen &&
        !discountOpen &&
        !paymentOpen &&
        !confirmation
      ) {
        event.preventDefault();
        setInvoiceSearchOpen(true);
      } else if (
        event.key === "F1" &&
        !busy &&
        Boolean(draft?.lines.length) &&
        !temporaryOpen &&
        !productSearchOpen &&
        !customerSearchOpen &&
        !discountOpen &&
        !paymentOpen &&
        !confirmation
      ) {
        event.preventDefault();
        setPaymentOpen(true);
      } else if (
        event.key === "F2" &&
        !busy &&
        !temporaryOpen &&
        !paymentOpen &&
        !customerSearchOpen &&
        !discountOpen &&
        !confirmation
      ) {
        event.preventDefault();
        setProductSearchOpen(true);
      } else if (
        event.key === "F10" &&
        !busy &&
        Boolean(draft) &&
        !temporaryOpen &&
        !paymentOpen &&
        !productSearchOpen &&
        !discountOpen &&
        !confirmation
      ) {
        event.preventDefault();
        setCustomerSearchOpen(true);
      } else if (
        event.key === "F7" &&
        !busy &&
        Boolean(draft?.lines.length) &&
        !temporaryOpen &&
        !paymentOpen &&
        !productSearchOpen &&
        !customerSearchOpen &&
        !discountOpen &&
        !confirmation
      ) {
        event.preventDefault();
        setTemporaryOpen(true);
      } else if (
        event.key === "F3" &&
        !busy &&
        hasSelectedLine &&
        !temporaryOpen &&
        !paymentOpen &&
        !productSearchOpen &&
        !customerSearchOpen &&
        !confirmation
      ) {
        event.preventDefault();
        setDiscountOpen(true);
      } else if (
        event.key === "F4" &&
        !busy &&
        hasSelectedLine &&
        selectedLineId &&
        !temporaryOpen &&
        !paymentOpen &&
        !productSearchOpen &&
        !customerSearchOpen &&
        !discountOpen &&
        !confirmation
      ) {
        event.preventDefault();
        requestRemoveLine(selectedLineId);
      } else if (
        event.key === "F5" &&
        !busy &&
        !temporaryOpen &&
        !paymentOpen &&
        !productSearchOpen &&
        !customerSearchOpen &&
        !discountOpen &&
        !confirmation
      ) {
        event.preventDefault();
        requestCancelSale();
      } else if (event.key === "F8" && !busy) {
        event.preventDefault();
        setSidePanel("temporaries");
      } else if (event.key === "F9" && !busy) {
        event.preventDefault();
        setSidePanel("orders");
      }
    };
    window.addEventListener("keydown", handleShortcut);
    return () => window.removeEventListener("keydown", handleShortcut);
  }, [
    busy,
    confirmation,
    customerSearchOpen,
    discountOpen,
    invoiceSearchOpen,
    draft?.lines.length,
    hasSelectedLine,
    paymentOpen,
    productSearchOpen,
    selectedLineId,
    temporaryOpen,
  ]);

  async function capture(event: FormEvent) {
    event.preventDefault();
    await captureValue(scan.trim());
  }

  async function captureValue(value: string): Promise<boolean> {
    if (!client || !value || busy) return false;
    if (!edgeReady) {
      setError(
        client.mode === "online"
          ? "No hay conexi\u00f3n con Auraly. La venta en l\u00ednea requiere conexi\u00f3n con el servidor."
          : "Los servicios locales de la caja no est\u00e1n disponibles. El c\u00f3digo se conservar\u00e1 para reintentar.",
      );
      setMessage(client.mode === "online" ? "Esperando conexi\u00f3n con Auraly" : "Esperando servicios de la caja");
      focusScanner();
      return false;
    }
    setBusy(true);
    setError(null);
    try {
      const startsNewSale = !draft?.lines.length;
      const result = await client.capture(value, draft?.customerId ?? null);
      if (result.status === "Added" && result.draft) {
        setDraft(result.draft);
        setSelectedLineId(result.draft.lines.at(-1)?.lineId ?? null);
        setMessage(`${result.draft.lines.at(-1)?.description ?? "Producto"} agregado`);
        if (startsNewSale) setLastSettlement(null);
        setScan("");
        return true;
      }
      return false;
    } catch (caught) {
      showError(caught);
      return false;
    } finally {
      setBusy(false);
      focusScanner();
    }
  }

  async function changeQuantity(lineId: string, quantity: number, focusAfter = true) {
    if (!client || !draft || quantity <= 0) return;
    setBusy(true);
    setError(null);
    try {
      const result = await client.changeQuantity(
        draft.draftId.value,
        lineId,
        quantity,
      );
      if (result.draft) setDraft(result.draft);
      setMessage("Cantidad actualizada");
    } catch (caught) {
      showError(caught);
    } finally {
      setBusy(false);
      if (focusAfter) focusScanner();
    }
  }

  async function navigateFromQuantity(
    lineId: string,
    value: number,
    backwards: boolean,
  ) {
    const lines = draft?.lines ?? [];
    const currentIndex = lines.findIndex((line) => line.lineId === lineId);
    const nextIndex = backwards ? currentIndex - 1 : currentIndex + 1;
    const nextLineId =
      nextIndex >= 0 && nextIndex < lines.length ? lines[nextIndex].lineId : null;
    const currentLine = lines[currentIndex];

    skipQuantityBlur.current = lineId;
    if (currentLine && Number.isFinite(value) && value > 0 && value !== currentLine.quantity) {
      await changeQuantity(lineId, value, false);
    } else if (currentLine && (!Number.isFinite(value) || value <= 0)) {
      const input = quantityInputs.current.get(lineId);
      if (input) input.value = String(currentLine.quantity);
    }

    window.requestAnimationFrame(() => {
      if (nextLineId) {
        quantityInputs.current.get(nextLineId)?.focus();
        quantityInputs.current.get(nextLineId)?.select();
      } else {
        focusScanner();
      }
    });
  }

  function focusFirstQuantity() {
    const firstLineId = draft?.lines.at(0)?.lineId;
    if (!firstLineId) return;
    window.requestAnimationFrame(() => {
      quantityInputs.current.get(firstLineId)?.focus();
      quantityInputs.current.get(firstLineId)?.select();
    });
  }

  async function removeLine(lineId: string) {
    if (!client || !draft) return;
    setBusy(true);
    try {
      const updated = await client.removeLine(draft.draftId.value, lineId);
      setDraft(updated);
      setSelectedLineId(updated.lines.at(-1)?.lineId ?? null);
      setMessage("Producto retirado");
    } catch (caught) {
      showError(caught);
    } finally {
      setBusy(false);
      focusScanner();
    }
  }

  function requestRemoveLine(lineId: string) {
    const line = draft?.lines.find((candidate) => candidate.lineId === lineId);
    if (!line || busy) return;
    setConfirmation({ kind: "line", lineId, productName: line.description });
  }

  function requestCancelSale() {
    if (!draft?.lines.length || busy) return;
    setConfirmation({ kind: "sale" });
  }

  async function cancelSale() {
    if (!client || !draft?.lines.length || busy) return;
    setBusy(true);
    setError(null);
    try {
      const next = await client.cancelDraft(draft.draftId.value);
      setDraft(next);
      setSelectedLineId(null);
      setSelectedCustomer(null);
      setScan("");
      setMessage("Venta reiniciada. Nueva venta lista.");
    } catch (caught) {
      showError(caught);
    } finally {
      setBusy(false);
      focusScanner();
    }
  }

  async function applyDiscount(discount: number) {
    if (!client || !draft || !selectedLineId || busy) return;
    setBusy(true);
    setError(null);
    try {
      setDraft(await client.setDiscount(draft.draftId.value, selectedLineId, discount));
      setDiscountOpen(false);
      setMessage(discount > 0 ? "Descuento aplicado" : "Descuento retirado");
    } catch (caught) {
      showError(caught);
    } finally {
      setBusy(false);
      focusScanner();
    }
  }

  async function selectCustomer(customer: PosCustomer | null) {
    if (!client || !draft || busy) return;
    setBusy(true);
    setError(null);
    try {
      const selection = await client.selectCustomer(
        draft.draftId.value,
        customer?.customerId ?? null,
      );
      setDraft(selection.draft);
      setSelectedCustomer(selection.customer);
      setCustomerSearchOpen(false);
      setMessage(
        selection.customer
          ? `${selection.customer.name} seleccionado; precios recalculados`
          : "Consumidor final seleccionado; precios del negocio aplicados",
      );
    } catch (caught) {
      showError(caught);
    } finally {
      setBusy(false);
      focusScanner();
    }
  }

  async function reprintSale(sale: PosIssuedSaleSummary) {
    if (!client || busy) return;
    setBusy(true);
    setError(null);
    try {
      await client.reprint(sale.documentId.value);
      setMessage(`${sale.documentNumber} reimpresa desde su snapshot original`);
      setInvoiceSearchOpen(false);
    } catch (caught) {
      showError(caught);
    } finally {
      setBusy(false);
      focusScanner();
    }
  }

  async function reprintLastSale() {
    if (!client || !lastSettlement || busy) return;
    setBusy(true);
    setError(null);
    try {
      await client.reprint(lastSettlement.documentId);
      setMessage(`${lastSettlement.documentNumber} reimpresa`);
    } catch (caught) {
      showError(caught);
    } finally {
      setBusy(false);
      focusScanner();
    }
  }

  async function confirmDestructiveAction() {
    if (!confirmation) return;
    if (confirmation.kind === "line") {
      await removeLine(confirmation.lineId);
    } else if (confirmation.kind === "temporary") {
      await deleteTemporary(confirmation.draftId);
    } else {
      await cancelSale();
    }
    setConfirmation(null);
  }

  async function saveTemporary(event: FormEvent) {
    event.preventDefault();
    if (!client || !draft || !temporaryName.trim()) return;
    setBusy(true);
    try {
      await client.saveTemporary(
        draft.draftId.value,
        temporaryName,
        temporaryReference,
        "",
      );
      setDraft(await client.activeDraft());
      await refreshTemporaries();
      setTemporaryOpen(false);
      setTemporaryName("");
      setTemporaryReference("");
      setMessage("Venta pausada");
    } catch (caught) {
      showError(caught);
    } finally {
      setBusy(false);
      focusScanner();
    }
  }

  async function recoverTemporary(id: string) {
    if (!client) return;
    setBusy(true);
    try {
      setDraft(await client.recoverTemporary(id));
      await refreshTemporaries();
      setMessage("Venta en espera recuperada");
    } catch (caught) {
      showError(caught);
    } finally {
      setBusy(false);
      focusScanner();
    }
  }

  function requestDeleteTemporary(id: string, name: string) {
    if (busy) return;
    setConfirmation({ kind: "temporary", draftId: id, name });
  }

  async function deleteTemporary(id: string) {
    if (!client || busy) return;
    setBusy(true);
    setError(null);
    try {
      await client.deleteTemporary(id);
      await refreshTemporaries();
      setMessage("Venta en espera eliminada");
    } catch (caught) {
      showError(caught);
    } finally {
      setBusy(false);
      focusScanner();
    }
  }

  function openPayment() {
    if (!draft?.lines.length || busy) return;
    setError(null);
    setPaymentOpen(true);
  }

  async function completeSale(
    payments: PosPaymentInput[],
    settlement: PosPaymentSettlement,
  ) {
    if (!client || !draft || busy) return;
    setBusy(true);
    setError(null);
    try {
      const result = await client.completeSale(
        draft.draftId.value,
        selectedCustomer?.identification ?? null,
        payments,
      );
      setDraft(result.nextDraft);
      setNextNumber(result.nextDocumentNumber);
      setLastSettlement({
        documentId: result.issuedSale.documentId.value,
        documentNumber: result.issuedSale.documentNumber,
        received: settlement.received,
        change: settlement.change,
      });
      setSelectedCustomer(null);
      setSelectedLineId(null);
      setScan("");
      setError(null);
      setPaymentOpen(false);
      const issuedLabel =
        result.printPreviewOpened === false
          ? "emitida. El navegador bloqueó la vista previa; puedes reimprimirla con F6"
          : "emitida e impresa";
      setMessage(
        settlement.change > 0
          ? `${result.issuedSale.documentNumber} ${issuedLabel}. Entregar ${money.format(settlement.change)} de cambio. Nueva venta lista.`
          : `${result.issuedSale.documentNumber} ${issuedLabel} (DIAN ${result.issuedSale.fiscalNumber}). Pago registrado. Nueva venta lista.`,
      );
    } catch (caught) {
      showError(caught);
    } finally {
      setBusy(false);
      focusScanner();
    }
  }

  const searchProducts = useCallback(
    (term: string, skip: number) =>
      client?.searchProducts(term, skip, 50) ??
      Promise.resolve({
        items: [],
        hasMore: false,
        nextOffset: null,
      }),
    [client],
  );

  const searchCustomers = useCallback(
    (term: string, skip: number) =>
      client?.searchCustomers(term, skip, 50) ??
      Promise.resolve({
        items: [],
        hasMore: false,
        nextOffset: null,
      }),
    [client],
  );

  const searchIssuedSales = useCallback(
    (term: string, skip: number) =>
      client?.searchIssuedSales(term, skip, 50) ??
      Promise.resolve({
        items: [],
        hasMore: false,
        nextOffset: null,
      }),
    [client],
  );

  async function selectSearchProduct(product: PosCatalogProduct) {
    const added = await captureValue(product.productCode);
    if (added) setProductSearchOpen(false);
    return added;
  }

  function openOrders() {
    if (!serverConnected) {
      setError(null);
      setMessage("Los pedidos se consultan en línea. Auraly Server no está disponible.");
      focusScanner();
      return;
    }
    setOrdersExpanded(true);
  }

  async function recoverPosOrder(orderId: string) {
    if (!client) throw new Error("La caja no estÃ¡ disponible.");
    const recovered = await client.recoverOrder(orderId);
    setDraft(recovered);
    setSelectedLineId(recovered.lines[0]?.lineId ?? null);
    setOrdersExpanded(false);
    setSidePanel("temporaries");
    setMessage("Pedido recuperado Â· " + recovered.lines.length + " lÃ­neas");
    focusScanner();
  }

  async function invoicePosOrders(
    orderIds: string[],
    paymentMethodCode: string,
  ) {
    if (!client) throw new Error("La caja no estÃ¡ disponible.");
    const result = await client.invoiceOrders(orderIds, paymentMethodCode);
    setMessage(
      result.completedCount +
        " pedido" +
        (result.completedCount === 1 ? "" : "s") +
        " facturado" +
        (result.completedCount === 1 ? "" : "s"),
    );
    return result;
  }

  function showError(caught: unknown) {
    const status = caught instanceof PosEdgeError ? caught.status : 0;
    if (!(caught instanceof PosEdgeError) && client?.mode === "online") {
      setEdgeReady(false);
      setServerConnected(false);
    }
    const text =
      client?.mode === "online" && caught instanceof PosEdgeError
        ? caught.message
        :
      status === 409 &&
      caught instanceof PosEdgeError &&
      caught.message.includes("pendiente de imprimir")
        ? caught.message
        : status === 404
        ? "Producto no encontrado en el catálogo local"
        : status === 409
          ? "La cantidad solicitada no está disponible"
          : status === 503 && caught instanceof PosEdgeError && caught.message.includes("tirilla")
            ? "La factura fue emitida, pero la tirilla no pudo imprimirse. Reintenta sin modificar la venta."
            : status === 503
            ? "La bodega exige validar inventario y no hay conexión"
            : "No fue posible acceder a los servicios locales de la caja";
    setError(text);
    setMessage("Revisa la novedad");
  }

  async function activateOnline(option: OnlineRegisterOption) {
    setSetupError(null);
    try {
      window.localStorage.setItem("selected_business_id", option.businessId);
      const context = await selectOnlineRegister(option);
      setClient(new OnlinePosClient(context, onlineUserId, onlineUserName));
    } catch (caught) {
      setSetupError(
        caught instanceof Error
          ? caught.message
          : "No fue posible seleccionar la caja.",
      );
      throw caught;
    }
  }

  async function enrollOffline(option: OnlineRegisterOption) {
    if (!edgeEnrollmentToken) return;
    setSetupError(null);
    setSetupLoading(true);
    try {
      const authorization = await authorizePosEnrollment(option);
      await redeemPosEnrollment(edgeEnrollmentToken, authorization);
      setSetupError(
        "Configuración protegida. Auraly POS está reiniciando e iniciará la sincronización automática.",
      );
      window.setTimeout(() => window.location.reload(), 2_500);
    } catch (caught) {
      setSetupError(
        caught instanceof Error
          ? caught.message
          : "No fue posible enrolar esta estación.",
      );
      throw caught;
    } finally {
      setSetupLoading(false);
    }
  }

  async function loginLocal(username: string, password: string) {
    if (!(client instanceof PosEdgeClient) || !edgeEnrollmentToken) return;
    setEdgeLoginError(null);
    try {
      const session = await client.login(username, password);
      setWorkstation((current) => ({
        ...current,
        userDisplayName: session.displayName,
        userId: session.userId,
      }));
      setEdgeLoginState(null);
      setClient(new PosEdgeClient(edgeEnrollmentToken, session.token));
    } catch (caught) {
      setEdgeLoginError(
        caught instanceof Error ? caught.message : "No fue posible iniciar sesión en esta caja.",
      );
      throw caught;
    }
  }

  async function logoutLocal() {
    if (!(client instanceof PosEdgeClient) || busy) return;
    try {
      await client.logout();
    } finally {
      setDraft(null);
      setTemporaries([]);
      setSelectedCustomer(null);
      setSelectedLineId(null);
      setNextNumber(null);
      setEdgeReady(false);
      setEdgeLoginError(null);
      setEdgeLoginState("required");
      if (edgeEnrollmentToken) {
        setClient(new PosEdgeClient(edgeEnrollmentToken));
      }
    }
  }

  function changeOnlineRegister() {
    if (client?.mode !== "online" || busy) return;
    forgetOnlineRegister();
    setClient(null);
    setDraft(null);
    setTemporaries([]);
    setSelectedCustomer(null);
    setSelectedLineId(null);
    setNextNumber(null);
    setEdgeReady(false);
    setServerConnected(false);
  }

  if (client instanceof PosEdgeClient && edgeLoginState) {
    return (
      <PosLocalLogin
        registerCode={workstation.registerCode}
        serverConnected={serverConnected}
        preparing={edgeLoginState === "preparing"}
        error={edgeLoginError}
        onLogin={loginLocal}
      />
    );
  }

  if (!client) {
    return (
      <PosOnlineSetup
        options={onlineOptions}
        loading={setupLoading}
        error={setupError}
        tenantName={onlineTenantName || "Auraly"}
        userDisplayName={onlineUserName || "usuario"}
        onSelect={activateOnline}
        edgeCapable={edgeEnrollmentRequired}
        onEnroll={enrollOffline}
      />
    );
  }

  return (
    <main className="min-h-screen bg-[#eef3f3] text-slate-950 xl:h-screen xl:overflow-hidden">
      <header className="flex min-h-14 items-center justify-between gap-4 bg-auraly-background px-5 py-2.5 text-auraly-text shadow-lg">
        <div className="flex items-center gap-2">
          <PosExitMenuButton />
          <StatusChip
            ok={serverConnected}
            label={serverConnected ? "Conectado con Auraly" : "Modo sin conexión"}
            network
          />
        </div>
        <div className="flex min-w-0 items-center gap-3 text-sm">
          <span className="font-bold tracking-tight">Caja {workstation.registerCode}</span>
          {client.mode === "online" && (
            <button
              type="button"
              onClick={changeOnlineRegister}
              disabled={busy}
              title="Cambiar caja"
              className="grid h-8 w-8 place-items-center rounded-lg border border-white/10 text-auraly-secondary transition hover:bg-white/10 hover:text-white disabled:opacity-40"
              aria-label="Cambiar caja"
            >
              <Settings2 className="h-4 w-4" />
            </button>
          )}
          {client.mode === "edge" && (
            <button
              type="button"
              onClick={() => void logoutLocal()}
              disabled={busy}
              title="Cambiar cajero"
              className="grid h-8 w-8 place-items-center rounded-lg border border-white/10 text-auraly-secondary transition hover:bg-white/10 hover:text-white disabled:opacity-40"
              aria-label="Cerrar sesión del cajero"
            >
              <LogOut className="h-4 w-4" />
            </button>
          )}
          <span className="h-4 w-px bg-white/20" aria-hidden="true" />
          <span className="flex min-w-0 items-center gap-1.5 text-auraly-secondary">
            <UserRound className="h-3.5 w-3.5 shrink-0" />
            <span className="truncate">{workstation.userDisplayName}</span>
          </span>
        </div>
      </header>

      <section className="grid min-h-[calc(100vh-3.5rem)] grid-cols-1 gap-3 p-3 xl:h-[calc(100vh-3.5rem)] xl:min-h-0 xl:grid-cols-[minmax(0,1fr)_340px] 2xl:grid-cols-[minmax(0,1fr)_390px]">
        <div className="flex min-w-0 flex-col gap-3 xl:min-h-0">
          <form
            onSubmit={capture}
            className="rounded-2xl border border-teal-900/10 bg-white p-3 shadow-sm"
          >
            <label
              htmlFor="pos-scanner"
              className="mb-2 flex items-center justify-between gap-3 text-sm font-medium text-slate-700"
            >
              <span className="flex items-center gap-2">
                <Barcode className="h-5 w-5 text-teal-700" />
                Escanea o escribe un código
              </span>
              <span className="text-xs font-normal text-slate-500">Enter para agregar</span>
            </label>
            <div className="flex gap-2">
              <input
                ref={scanner}
                id="pos-scanner"
                value={scan}
                onChange={(event) => setScan(event.target.value)}
                onKeyDown={(event) => {
                  if (event.key === "Tab" && draft?.lines.length) {
                    event.preventDefault();
                    focusFirstQuantity();
                  }
                }}
                disabled={busy}
                autoComplete="off"
                inputMode="text"
                className="h-14 min-w-0 flex-1 rounded-xl border-2 border-teal-700/25 bg-slate-50 px-4 text-xl font-semibold tracking-wide outline-none transition focus:border-teal-600 focus:bg-white focus:ring-4 focus:ring-teal-600/10 disabled:opacity-50"
                placeholder="Código de barras, interno o referencia"
                aria-describedby="capture-state"
              />
              <button
                type="submit"
                disabled={!scan.trim() || busy}
                className="min-w-28 rounded-xl bg-teal-700 px-5 font-semibold text-white transition hover:bg-teal-800 focus:outline-none focus:ring-4 focus:ring-teal-600/20 disabled:cursor-not-allowed disabled:opacity-50"
              >
                {busy ? <Loader2 className="mx-auto animate-spin" /> : "Agregar"}
              </button>
              <button
                type="button"
                onClick={() => setProductSearchOpen(true)}
                disabled={busy || !edgeReady}
                className="flex min-w-28 items-center justify-center gap-2 rounded-xl border border-teal-700/25 bg-white px-4 font-semibold text-teal-800 transition hover:bg-teal-50 focus:outline-none focus:ring-4 focus:ring-teal-600/15 disabled:opacity-45"
              >
                <Search className="h-4 w-4" />
                Buscar <span className="rounded bg-teal-50 px-1.5 py-0.5 text-xs">F2</span>
              </button>
            </div>
            <p
              id="capture-state"
              className={`mt-2 flex min-h-5 items-center gap-2 text-sm ${
                error ? "text-red-700" : "text-slate-500"
              }`}
              role={error ? "alert" : "status"}
            >
              {error ? <AlertTriangle className="h-4 w-4" /> : <CheckCircle2 className="h-4 w-4 text-teal-600" />}
              {error ?? message}
            </p>
          </form>

          <div
            className="flex flex-wrap items-center justify-between gap-2 rounded-2xl border border-slate-200 bg-white p-2 shadow-sm"
            aria-label="Acciones de la venta"
          >
            <p className="hidden pl-2 text-xs font-semibold uppercase tracking-[0.14em] text-slate-500 sm:block">
              Acciones de captura
            </p>
            <div className="grid w-full grid-cols-2 gap-2 sm:ml-auto sm:w-auto sm:grid-cols-4">
              <button type="button" disabled={!edgeReady || busy}
                onClick={() => setInvoiceSearchOpen(true)}
                className="flex h-11 items-center justify-center gap-2 rounded-xl border border-teal-200 bg-teal-50 px-3 text-sm font-semibold text-teal-900 transition hover:bg-teal-100 disabled:cursor-not-allowed disabled:border-slate-200 disabled:bg-slate-50 disabled:text-slate-400">
                <Printer className="h-4 w-4" />
                Facturas
                <span className="rounded bg-white/70 px-1.5 py-0.5 text-[10px]">F6</span>
              </button>
              <button type="button" disabled={!hasSelectedLine || busy}
                onClick={() => setDiscountOpen(true)}
                className="flex h-11 items-center justify-center gap-2 rounded-xl border border-amber-200 bg-amber-50 px-3 text-sm font-semibold text-amber-900 transition hover:bg-amber-100 disabled:cursor-not-allowed disabled:border-slate-200 disabled:bg-slate-50 disabled:text-slate-400">
                <Percent className="h-4 w-4" />
                Descuento
                <span className="rounded bg-white/70 px-1.5 py-0.5 text-[10px]">F3</span>
              </button>
              <button type="button" disabled={!hasSelectedLine || busy}
                onClick={() => selectedLineId && requestRemoveLine(selectedLineId)}
                className="flex h-11 items-center justify-center gap-2 rounded-xl border border-red-200 bg-red-50 px-3 text-sm font-semibold text-red-800 transition hover:bg-red-100 disabled:cursor-not-allowed disabled:border-slate-200 disabled:bg-slate-50 disabled:text-slate-400">
                <Trash2 className="h-4 w-4" />
                Eliminar
                <span className="rounded bg-white/70 px-1.5 py-0.5 text-[10px]">F4</span>
              </button>
              <button type="button" disabled={!draft?.lines.length || busy}
                onClick={requestCancelSale}
                className="flex h-11 items-center justify-center gap-2 rounded-xl border border-slate-300 bg-slate-50 px-3 text-sm font-semibold text-slate-700 transition hover:bg-slate-100 disabled:cursor-not-allowed disabled:text-slate-400">
                <RotateCcw className="h-4 w-4" />
                Reiniciar
                <span className="rounded bg-white px-1.5 py-0.5 text-[10px]">F5</span>
              </button>
            </div>
          </div>

          <div className="flex min-h-[360px] flex-1 flex-col overflow-hidden rounded-2xl border border-slate-200 bg-white shadow-sm xl:min-h-0">
            <div className="flex min-h-0 flex-1 flex-col overflow-auto">
              <table className="w-full min-w-[680px] border-collapse text-sm">
                <thead className="sticky top-0 z-10 bg-slate-100 text-left text-xs font-semibold tracking-wide text-slate-600">
                  <tr>
                    <th className="px-4 py-3">Producto</th>
                    <th className="w-28 px-3 py-3 text-right">Cantidad</th>
                    <th className="w-36 px-3 py-3 text-right">Precio unitario</th>
                    <th className="w-36 px-3 py-3 text-right">Total</th>
                    <th className="w-16 px-3 py-3" aria-label="Acciones" />
                  </tr>
                </thead>
                <tbody>
                  {draft?.lines.map((line) => (
                    <tr
                      key={line.lineId}
                      onClick={() => setSelectedLineId(line.lineId)}
                      className={`border-t border-slate-100 transition ${
                        selectedLineId === line.lineId
                          ? "bg-teal-50 ring-2 ring-inset ring-teal-600/25"
                          : "hover:bg-teal-50/40"
                      }`}
                    >
                      <td className="px-4 py-3.5">
                        <p className="text-[15px] font-bold leading-snug text-slate-950">
                          {line.description}
                        </p>
                        <p className="mt-1 text-xs font-medium text-slate-500">
                          {line.productCode} · {line.unitCode} · {priceLabel(line.priceSource)}
                        </p>
                        <div className="mt-2 flex flex-wrap items-center gap-1.5 text-[11px] font-semibold tabular-nums">
                          {line.discount > 0 && (
                            <span className="rounded-full border border-amber-200 bg-amber-50 px-2 py-0.5 text-amber-800">
                              Descuento −{money.format(line.discount)}
                            </span>
                          )}
                          <span
                            className="rounded-full border border-sky-200 bg-sky-50 px-2 py-0.5 text-sky-800"
                            title={`Impuesto ${line.taxCode}`}
                          >
                            IVA {line.taxRate}% · {money.format(line.tax)}
                          </span>
                        </div>
                      </td>
                      <td className="px-3 py-2 text-right">
                        <input
                          key={`${line.lineId}-${line.quantity}`}
                          ref={(element) => {
                            if (element) {
                              quantityInputs.current.set(line.lineId, element);
                            } else {
                              quantityInputs.current.delete(line.lineId);
                            }
                          }}
                          type="number"
                          min="0.001"
                          step="0.001"
                          defaultValue={line.quantity}
                          onFocus={() => setSelectedLineId(line.lineId)}
                          onKeyDown={(event) => {
                            if (event.key === "Enter") {
                              event.preventDefault();
                              skipQuantityBlur.current = line.lineId;
                              const quantity = event.currentTarget.valueAsNumber;
                              void (async () => {
                                if (!Number.isFinite(quantity) || quantity <= 0) {
                                  const input = quantityInputs.current.get(line.lineId);
                                  if (input) input.value = String(line.quantity);
                                } else if (quantity !== line.quantity) {
                                  await changeQuantity(line.lineId, quantity, false);
                                }
                                focusScanner();
                              })();
                            } else if (event.key === "Tab") {
                              event.preventDefault();
                              void navigateFromQuantity(
                                line.lineId,
                                event.currentTarget.valueAsNumber,
                                event.shiftKey,
                              );
                            }
                          }}
                          onBlur={(event) => {
                            if (skipQuantityBlur.current === line.lineId) {
                              skipQuantityBlur.current = null;
                              return;
                            }
                            const quantity = event.currentTarget.valueAsNumber;
                            if (!Number.isFinite(quantity) || quantity <= 0) {
                              event.currentTarget.value = String(line.quantity);
                              focusScanner();
                            } else if (quantity !== line.quantity) {
                              void changeQuantity(line.lineId, quantity);
                            } else {
                              focusScanner();
                            }
                          }}
                          className="h-10 w-24 rounded-lg border border-slate-300 bg-white px-2 text-right font-semibold outline-none focus:border-teal-600 focus:ring-2 focus:ring-teal-600/15"
                          aria-label={`Cantidad de ${line.description}`}
                        />
                      </td>
                      <td className="px-3 py-3 text-right font-medium tabular-nums text-slate-700">
                        {money.format(line.unitPrice)}
                      </td>
                      <td className="px-3 py-3 text-right text-base font-bold tabular-nums text-slate-950">
                        {money.format(line.total)}
                      </td>
                      <td className="px-3 py-2 text-right">
                        <button
                          type="button"
                          onClick={() => requestRemoveLine(line.lineId)}
                          className="inline-flex h-10 w-10 items-center justify-center rounded-lg text-slate-500 transition hover:bg-red-50 hover:text-red-700 focus:outline-none focus:ring-2 focus:ring-red-300"
                          aria-label={`Eliminar ${line.description}`}
                        >
                          <Trash2 className="h-4 w-4" />
                        </button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            {!draft?.lines.length && (
              <div className="relative flex min-h-0 flex-1 flex-col items-center justify-center overflow-hidden px-6 text-center">
                <div className="absolute inset-0 bg-[radial-gradient(circle_at_center,rgba(13,148,136,0.10),transparent_52%)]" />
                <div className="relative grid h-20 w-20 place-items-center rounded-full border border-teal-200 bg-teal-50 shadow-[0_0_0_12px_rgba(20,184,166,0.05)]">
                  <Barcode className="h-9 w-9 text-teal-700" />
                </div>
                <p className="relative mt-6 text-xl font-bold tracking-tight text-slate-900">Caja lista para vender</p>
                <p className="relative mt-1 max-w-md text-sm text-slate-500">
                  Escanea el primer producto o abre la búsqueda. El lector queda preparado para continuar sin usar el mouse.
                </p>
                <div className="relative mt-5 flex flex-wrap items-center justify-center gap-2 text-xs font-semibold">
                  <span className="rounded-full border border-teal-200 bg-white px-3 py-1.5 text-teal-800">Lector activo</span>
                  <span className="rounded-full border border-slate-200 bg-white px-3 py-1.5 text-slate-700">Buscar producto · F2</span>
                  <span className="rounded-full border border-slate-200 bg-white px-3 py-1.5 text-slate-700">
                    Próxima {nextNumber?.isAvailable
                      ? nextNumber.fullNumber
                      : client.mode === "online"
                        ? "al emitir"
                        : "por calcular"}
                  </span>
                </div>
              </div>
            )}
            </div>
          </div>
        </div>

        <aside className="flex min-h-0 flex-col gap-3 xl:overflow-hidden">
          <section className={`shrink-0 rounded-2xl bg-auraly-background p-4 text-auraly-text shadow-lg ${showCashChange ? "xl:basis-[54%]" : ""}`}>
            <div className="mb-3 flex items-center justify-between">
              <div>
                <p className="text-xs uppercase tracking-[0.16em] text-auraly-secondary">Venta actual</p>
                <p className="mt-1 text-sm font-medium">
                  {selectedCustomer?.name ?? "Consumidor final"}
                </p>
                {selectedCustomer && (
                  <p className="mt-0.5 text-xs text-auraly-secondary">
                    {selectedCustomer.identification}
                  </p>
                )}
                <p className="mt-0.5 text-xs text-auraly-secondary">
                  Próxima: {nextNumber?.isAvailable
                    ? nextNumber.fullNumber
                    : client.mode === "online"
                      ? "se asigna al emitir"
                      : "Serie no disponible"}
                </p>
              </div>
              <span className="rounded-lg bg-white/10 px-2 py-1 text-xs">
                {draft?.lines.length ?? 0} líneas
              </span>
            </div>
            <button
              type="button"
              disabled={!edgeReady || busy}
              onClick={() => setCustomerSearchOpen(true)}
              className="mb-3 flex h-10 w-full items-center justify-center gap-2 rounded-xl border border-auraly-accent/40 bg-white/5 text-sm font-semibold transition hover:bg-white/10 disabled:opacity-40"
            >
              <UserRound className="h-4 w-4" />
              Buscar cliente
              <span className="rounded bg-white/10 px-1.5 py-0.5 text-xs">F10</span>
            </button>
            <dl className="space-y-2 text-sm">
              <TotalRow label="Subtotal" value={draft?.untaxedAmount ?? 0} />
              <TotalRow label="Impuestos" value={draft?.taxAmount ?? 0} />
              <div className="border-t border-white/15 pt-3">
                <dt className="text-sm text-auraly-secondary">Total a pagar</dt>
                <dd className="text-right text-3xl font-bold tracking-tight text-auraly-light">
                  {money.format(draft?.payableAmount ?? 0)}
                </dd>
              </div>
            </dl>
            <button
              type="button"
              disabled={!draft?.lines.length || busy}
              onClick={openPayment}
              className="mt-3 flex h-12 w-full items-center justify-center gap-2 rounded-xl bg-auraly-accent px-4 text-base font-bold text-auraly-background transition hover:bg-auraly-light disabled:cursor-not-allowed disabled:opacity-40"
            >
              Cobrar
              <span className="rounded bg-black/10 px-2 py-0.5 text-xs font-semibold">F1</span>
            </button>

            <button
              type="button"
              disabled={!draft?.lines.length || busy}
              onClick={() => setTemporaryOpen(true)}
              className="mt-2 flex h-10 w-full items-center justify-center gap-2 rounded-xl border border-auraly-accent/40 bg-white/5 text-sm font-semibold transition hover:bg-white/10 disabled:cursor-not-allowed disabled:opacity-40"
            >
              <Save className="h-4 w-4" />
              Pausar venta
              <span className="rounded bg-white/10 px-2 py-0.5 text-xs font-semibold">F7</span>
            </button>

          </section>

          {showCashChange && lastSettlement ? (
            <section
              className="relative flex min-h-0 flex-1 overflow-hidden rounded-2xl border border-emerald-300 bg-emerald-50 p-4 shadow-sm"
              role="status"
              aria-live="assertive"
            >
              <div className="absolute -right-12 -top-12 h-40 w-40 rounded-full bg-emerald-200/45" />
              <div className="relative flex h-full w-full flex-col justify-between">
                <div className="flex items-center gap-3">
                  <span className="grid h-10 w-10 place-items-center rounded-xl bg-emerald-700 text-white shadow-sm">
                    <Banknote className="h-5 w-5" />
                  </span>
                  <div>
                    <p className="text-xs font-bold uppercase tracking-[0.16em] text-emerald-700">Entregar al cliente</p>
                    <p className="text-xs text-emerald-800">Venta {lastSettlement.documentNumber} completada</p>
                  </div>
                </div>
                <div className="py-3 text-center">
                  <p className="text-sm font-medium text-emerald-800">Cambio</p>
                  <p className="text-4xl font-black tracking-tight tabular-nums text-emerald-950">
                    {money.format(lastSettlement.change)}
                  </p>
                  <p className="mt-1 text-sm text-emerald-800">
                    Recibido: <span className="font-bold tabular-nums">{money.format(lastSettlement.received)}</span>
                  </p>
                </div>
                <button type="button" disabled={busy} onClick={() => void reprintLastSale()}
                  className="mx-auto flex h-9 items-center justify-center gap-2 rounded-xl border border-emerald-700/25 bg-white px-4 text-sm font-bold text-emerald-900 hover:bg-emerald-100 disabled:opacity-50">
                  <Printer className="h-4 w-4" />
                  Reimprimir última factura
                </button>
                <p className="mt-1 text-center text-xs text-emerald-700">
                  El cambio se ocultará al agregar el primer producto.
                </p>
              </div>
            </section>
          ) : (
          <section className="flex min-h-0 flex-1 flex-col overflow-hidden rounded-2xl border border-slate-200 bg-white p-4 shadow-sm">
            <div
              role="tablist"
              aria-label="Documentos pendientes"
              className="mb-4 grid grid-cols-2 gap-1 rounded-xl bg-slate-100 p-1"
            >
              <button
                type="button"
                role="tab"
                aria-selected={sidePanel === "temporaries"}
                onClick={() => setSidePanel("temporaries")}
                className={`flex min-h-11 items-center justify-center gap-2 rounded-lg px-3 text-sm font-semibold transition ${
                  sidePanel === "temporaries"
                    ? "bg-white text-teal-800 shadow-sm"
                    : "text-slate-600 hover:text-slate-900"
                }`}
              >
                <Clock3 className="h-4 w-4" />
                En espera
                <span className="rounded-full bg-teal-50 px-2 py-0.5 text-xs text-teal-800">
                  {temporaries.length}
                </span>
                <span className="text-[10px] text-slate-400">F8</span>
              </button>
              <button
                type="button"
                role="tab"
                aria-selected={sidePanel === "orders"}
                onClick={() => setSidePanel("orders")}
                className={`flex min-h-11 items-center justify-center gap-2 rounded-lg px-3 text-sm font-semibold transition ${
                  sidePanel === "orders"
                    ? "bg-white text-teal-800 shadow-sm"
                    : "text-slate-600 hover:text-slate-900"
                }`}
              >
                <ClipboardList className="h-4 w-4" />
                Pedidos
                <span className="rounded-full bg-teal-50 px-2 py-0.5 text-xs text-teal-800">
                  0
                </span>
                <span className="text-[10px] text-slate-400">F9</span>
              </button>
            </div>

            {sidePanel === "temporaries" ? (
              <div className="min-h-0 flex-1 overflow-auto">
                <div className="mb-3">
                  <p className="font-semibold text-slate-900">Ventas en espera</p>
                  <p className="text-xs text-slate-500">Pausadas para continuar después</p>
                </div>
                <div className="space-y-2">
                  {temporaries.map((temporary) => (
                    <article
                      key={temporary.draftId.value}
                      className="rounded-xl border border-slate-200 p-3"
                    >
                      <div className="flex items-start justify-between gap-3">
                        <div className="min-w-0">
                          <p className="truncate font-medium">{temporary.name}</p>
                          <p className="mt-0.5 text-xs text-slate-500">
                            {temporary.reference || "Sin referencia"} · {temporary.lines.length} líneas
                          </p>
                        </div>
                        <p className="font-semibold tabular-nums">{money.format(temporary.payableAmount)}</p>
                      </div>
                      <div className="mt-3 grid grid-cols-2 gap-2">
                        <button type="button"
                          onClick={() => void recoverTemporary(temporary.draftId.value)}
                          disabled={Boolean(draft?.lines.length) || busy}
                          className="flex h-9 items-center justify-center gap-2 rounded-lg bg-teal-50 text-sm font-semibold text-teal-800 transition hover:bg-teal-100 disabled:cursor-not-allowed disabled:opacity-45">
                          <RotateCcw className="h-4 w-4" />
                          Continuar
                        </button>
                        <button type="button"
                          onClick={() => requestDeleteTemporary(
                            temporary.draftId.value,
                            temporary.name ?? "Venta sin nombre",
                          )}
                          disabled={busy}
                          className="flex h-9 items-center justify-center gap-2 rounded-lg border border-red-200 bg-red-50 text-sm font-semibold text-red-800 transition hover:bg-red-100 disabled:opacity-45">
                          <Trash2 className="h-4 w-4" />
                          Eliminar
                        </button>
                      </div>
                    </article>
                  ))}
                  {!temporaries.length && (
                    <p className="rounded-xl border border-dashed border-slate-300 p-5 text-center text-sm text-slate-500">
                      No hay ventas en espera.
                    </p>
                  )}
                </div>
              </div>
            ) : (
              <OrdersWorkspace
                compact
                connected={serverConnected}
                loadPage={(filters) => client!.orders(filters)}
                loadDetail={(orderId) => client!.order(orderId)}
                onRecover={(order) => recoverPosOrder(order.orderId)}
                onInvoiceSelected={(orders, method) =>
                  invoicePosOrders(orders.map((order) => order.orderId), method)
                }
                onExpand={openOrders}
              />
            )}
          </section>
          )}
        </aside>
      </section>

      {ordersExpanded && client && (
        <div className="fixed inset-0 z-50 flex flex-col bg-slate-50">
          <header className="flex h-16 shrink-0 items-center justify-between border-b border-slate-200 bg-white px-6">
            <div>
              <p className="text-xs font-semibold uppercase tracking-[0.18em] text-teal-700">
                Auraly Commerce
              </p>
              <h2 className="text-xl font-bold text-slate-950">Pedidos por facturar</h2>
            </div>
            <button
              type="button"
              onClick={() => {
                setOrdersExpanded(false);
                focusScanner();
              }}
              className="grid h-11 w-11 place-items-center rounded-xl border border-slate-200 text-slate-600 transition hover:bg-slate-100"
              aria-label="Cerrar pedidos"
            >
              <X className="h-5 w-5" />
            </button>
          </header>
          <main className="min-h-0 flex-1 overflow-auto p-5">
            <OrdersWorkspace
              connected={serverConnected}
              loadPage={(filters) => client.orders(filters)}
              loadDetail={(orderId) => client.order(orderId)}
              onRecover={(order) => recoverPosOrder(order.orderId)}
              onInvoiceSelected={(orders, method) =>
                invoicePosOrders(orders.map((order) => order.orderId), method)
              }
            />
          </main>
        </div>
      )}

      {productSearchOpen && client && (
        <PosProductSearchDialog
          busy={busy}
          onSearch={searchProducts}
          onSelect={selectSearchProduct}
          onCancel={() => {
            setProductSearchOpen(false);
            focusScanner();
          }}
        />
      )}

      {customerSearchOpen && client && (
        <PosCustomerSearchDialog
          busy={busy}
          onSearch={searchCustomers}
          onSelect={selectCustomer}
          onCancel={() => {
            setCustomerSearchOpen(false);
            focusScanner();
          }}
        />
      )}

      {paymentOpen && draft && (
        <PosPaymentDialog
          total={draft.payableAmount}
          busy={busy}
          onCancel={() => {
            setPaymentOpen(false);
            focusScanner();
          }}
          onConfirm={completeSale}
        />
      )}

      {invoiceSearchOpen && client && (
        <PosInvoiceSearchDialog
          busy={busy}
          onSearch={searchIssuedSales}
          onReprint={reprintSale}
          onCancel={() => {
            setInvoiceSearchOpen(false);
            focusScanner();
          }}
        />
      )}

      {discountOpen && draft && selectedLineId && (() => {
        const line = draft.lines.find((candidate) => candidate.lineId === selectedLineId);
        return line ? (
          <PosDiscountDialog
            productName={line.description}
            currentDiscount={line.discount}
            maximum={line.quantity * line.unitPrice}
            taxRate={line.taxRate}
            busy={busy}
            onConfirm={applyDiscount}
            onCancel={() => {
              setDiscountOpen(false);
              focusScanner();
            }}
          />
        ) : null;
      })()}

      {confirmation && (
        <PosConfirmDialog
          title={
            confirmation.kind === "line"
              ? "¿Eliminar este producto?"
              : confirmation.kind === "temporary"
                ? "¿Eliminar esta venta en espera?"
              : "¿Reiniciar toda la venta?"
          }
          description={
            confirmation.kind === "line"
              ? `${confirmation.productName} se retirará de la venta actual.`
              : confirmation.kind === "temporary"
                ? `${confirmation.name} se eliminará definitivamente de esta caja.`
              : "Se eliminarán todos los productos capturados y se abrirá una venta limpia."
          }
          confirmLabel={
            confirmation.kind === "sale" ? "Sí, reiniciar" : "Sí, eliminar"
          }
          busy={busy}
          onConfirm={confirmDestructiveAction}
          onCancel={() => {
            setConfirmation(null);
            focusScanner();
          }}
        />
      )}

      {temporaryOpen && (
        <div className="fixed inset-0 z-50 grid place-items-center bg-slate-950/55 p-4">
          <form
            onSubmit={saveTemporary}
            className="w-full max-w-md rounded-2xl bg-white p-5 shadow-2xl"
          >
            <h2 className="text-lg font-semibold">Pausar venta</h2>
            <p className="mt-1 text-sm text-slate-500">
              No asigna consecutivo fiscal ni genera movimientos.
            </p>
            <label className="mt-5 block text-sm font-medium">
              Nombre
              <input
                autoFocus
                required
                value={temporaryName}
                onChange={(event) => setTemporaryName(event.target.value)}
                className="mt-1 h-11 w-full rounded-lg border border-slate-300 px-3 outline-none focus:border-teal-600 focus:ring-2 focus:ring-teal-600/15"
                placeholder="Ej. Cliente espera"
              />
            </label>
            <label className="mt-3 block text-sm font-medium">
              Referencia
              <input
                value={temporaryReference}
                onChange={(event) => setTemporaryReference(event.target.value)}
                className="mt-1 h-11 w-full rounded-lg border border-slate-300 px-3 outline-none focus:border-teal-600 focus:ring-2 focus:ring-teal-600/15"
                placeholder="Opcional"
              />
            </label>
            <div className="mt-5 flex justify-end gap-2">
              <button
                type="button"
                onClick={() => {
                  setTemporaryOpen(false);
                  focusScanner();
                }}
                className="h-10 rounded-lg border border-slate-300 px-4 font-medium"
              >
                Cancelar
              </button>
              <button
                type="submit"
                disabled={busy || !temporaryName.trim()}
                className="h-10 rounded-lg bg-teal-700 px-4 font-semibold text-white disabled:opacity-50"
              >
                Guardar
              </button>
            </div>
          </form>
        </div>
      )}
    </main>
  );
}

function StatusChip({
  ok,
  label,
  network = false,
}: {
  ok: boolean;
  label: string;
  network?: boolean;
}) {
  const Icon = network ? (ok ? Wifi : WifiOff) : ok ? CheckCircle2 : AlertTriangle;
  return (
    <span
      className={`flex items-center gap-1.5 rounded-full px-3 py-1.5 ${
        ok ? "bg-emerald-400/15 text-emerald-100" : "bg-amber-400/15 text-amber-100"
      }`}
    >
      <Icon className="h-3.5 w-3.5" />
      {label}
    </span>
  );
}

function TotalRow({ label, value }: { label: string; value: number }) {
  return (
    <div className="flex items-center justify-between gap-3">
      <dt className="text-auraly-secondary">{label}</dt>
      <dd className="font-semibold tabular-nums">{money.format(value)}</dd>
    </div>
  );
}

function priceLabel(source: string) {
  if (source === "PriceList") return "Lista de precio";
  if (source === "PriceChannel") return "Canal de precio";
  return "Precio del negocio";
}
