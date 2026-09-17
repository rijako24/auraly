"use client";

import {
  AlertTriangle,
  ArrowDownToLine,
  ArrowUpFromLine,
  Banknote,
  Barcode,
  Calculator,
  CheckCircle2,
  Clock3,
  ClipboardList,
  Loader2,
  LogOut,
  Package,
  PencilLine,
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
  XCircle,
} from "lucide-react";
import { FormEvent, useCallback, useEffect, useMemo, useRef, useState } from "react";

import { useRouter } from "next/navigation";
import { canOpenPosAdministrativeMenu } from "@/lib/default-start-route";
import { realtimeReconnectDelay } from "@/lib/realtime-reconnect-policy";
import { OrdersWorkspace } from "@/components/orders/orders-workspace";
import { localOrderDateValue, orderDayRange } from "@/services/orders/order-date-filter";
import {
  loadCommerceOrder,
  loadCommerceOrders,
} from "@/services/orders/commerce-orders-client";
import { SalesReturnWorkspace } from "@/components/returns/sales-return-workspace";
import {
  PosCatalogProduct,
  type PosSaleDocumentType,
  type PosCashMovementDirection,
  PosCustomer,
  type PosCreateCustomerInput,
  PosDraft,
  PosDocumentNumberPreview,
  PosIssuedSaleSummary,
  PosPaymentInput,
  PosEdgeClient,
  PosEdgeError,
  type PosClient,
  type PosAuthorizedClosurePreview,
  type PosWorkSessionPaymentCount,
  type PosCaptureResult,
  type PosInventoryValidation,
  type PosSensitiveAuthorization,
  type PosDraftLineUpdate,
  clearEdgeUserSession,
  readEdgeTokenFromLaunch,
  readEdgeUserSession,
} from "@/services/pos/pos-edge-client";
import {
  loadSalesWorkspaceBootstrap,
  type SalesWorkspaceBootstrap,
} from "@/services/pos/online-pos-bootstrap";
import {
  forgetSalesWorkspace,
  OnlinePosClient,
  type SalesWorkspaceOption,
  rememberedSalesWorkspaceKey,
  selectSalesWorkspace,
  salesWorkspaceKey,
  closePrintPreview,
  openHalfLetterPrintPreview,
  renderReceiptsReceipt,
  loadServerIssuedSaleReceipt,
  searchServerHistoryCustomers,
  searchServerHistoryProducts,
  searchServerIssuedSales,
} from "@/services/pos/online-pos-client";
import type { OrderInvoiceSequenceProgress } from "@/services/pos/pos-order-print-routing";
import {
  authorizePosEnrollment,
  redeemPosEnrollment,
  waitForRedeemedPosEdge,
} from "@/services/pos/pos-enrollment";
import {
  connectRedeemedPosEdge,
  completePendingPosEnrollment,
  isPosPreparationPending,
  shouldCompletePosEnrollment,
} from "@/services/pos/pos-enrollment-transition";
import {
  canIssuePosDocument,
  dianQuotaExhaustedMessage,
  fiscalConfigurationRequiredMessage,
} from "@/services/pos/pos-fiscal-guard";
import { PosConfirmDialog } from "./pos-confirm-dialog";
import { PosCashMovementDialog } from "./pos-cash-movement-dialog";
import { PosCashClosureDialog } from "./pos-cash-closure-dialog";
import { PosCashDenominationDialog } from "./pos-cash-denomination-dialog";
import { PosCustomerSearchDialog } from "./pos-customer-search-dialog";
import { PosDocumentTypeDialog } from "./pos-document-type-dialog";
import { PosDesktopUpdater } from "./pos-desktop-updater";
import { PosLineEditorDialog } from "./pos-line-editor-dialog";
import { PosGenericProductDialog } from "./pos-generic-product-dialog";
import { PosExitMenuButton } from "./pos-exit-menu-button";
import { PosInvoiceSearchDialog } from "./pos-invoice-search-dialog";
import { PosInventoryResolutionDialog } from "./pos-inventory-resolution-dialog";
import { PosQuantityAvailabilityDialog, type PosQuantityShortage } from "./pos-quantity-availability-dialog";
import { temporaryNameForCustomer } from "./pos-temporary-name";
import { PosOnlineSetup } from "./pos-online-setup";
import { PosPaymentDialog } from "./pos-payment-dialog";
import { PosPrinterDialog } from "./pos-printer-dialog";
import {
  shouldShowCashChange,
  splitCreditCheckout,
  type PosPaymentSettlement,
} from "./pos-payment-settlement";
import { PosProductSearchDialog } from "./pos-product-search-dialog";
import { PosSupervisorApprovalDialog } from "./pos-supervisor-approval-dialog";
import { PosSynchronizationEventsDialog } from "./pos-synchronization-events-dialog";
import {
  posApprovalClient,
  type PosApprovalRequest,
} from "@/services/pos/pos-approval-client";
import { approvalRequestConfirmsExistingPermission } from "@/services/pos/pos-approval-permission";
import { calculateEffectiveRetailUnitPrice } from "./pos-retail-price";
import {
  canRequestOrderSave,
  orderSaveRequiresCustomerSelection,
  removingLastRecoveredOrderLineCancelsOrder,
  shouldSaveOrderAfterCustomerSelection,
} from "./pos-order-save-availability";
import { consumeOrderRecoveryUrl } from "./pos-order-recovery-url";
import { capturedLineAfterAddition, shouldOpenGenericProductPricing } from "./pos-capture-presentation";
import { capturePosFunctionShortcut, isPosCashDrawerShortcut, isPosDenominationCalculatorShortcut, POS_ACTION_SHORTCUTS } from "./pos-function-shortcut";
import { parsePosBarcodeCapture, submitPosCaptureOnEnter } from "./pos-barcode-capture";
import { acceptsPosQuantityDraft, blocksPosQuantityKey, validatePosQuantity } from "./pos-quantity-validation";
import { useAuthStore } from "@/stores/auth-store";
import {
  enrolledWorkspaceOption,
  shouldAutoActivateRememberedWorkspace,
  shouldUseEnrolledPosRuntime,
  workspaceActivationMode,
} from "@/services/pos/pos-launch-session";
import { posInventoryPolicyPresentation } from "./pos-inventory-policy";
import type { PosPreparationHealth } from "./pos-preparation-progress";
import { posPublicError } from "./pos-public-error";
import { saleRequiresBelowCostAuthorization } from "./pos-sale-authorization";


const money = new Intl.NumberFormat("es-CO", {
  style: "currency",
  currency: "COP",
  maximumFractionDigits: 0,
});

const effectiveUnitMoney = new Intl.NumberFormat("es-CO", {
  style: "currency",
  currency: "COP",
  minimumFractionDigits: 0,
  maximumFractionDigits: 6,
});

let rejectedScanAudioContext: AudioContext | null = null;

function playRejectedScanTone() {
  try {
    rejectedScanAudioContext ??= new AudioContext();
    const context = rejectedScanAudioContext;
    void context.resume().then(() => {
      const startedAt = context.currentTime;
      const oscillator = context.createOscillator();
      const gain = context.createGain();

      oscillator.type = "triangle";
      oscillator.frequency.setValueAtTime(390, startedAt);
      oscillator.frequency.exponentialRampToValueAtTime(155, startedAt + 0.18);
      gain.gain.setValueAtTime(0.0001, startedAt);
      gain.gain.exponentialRampToValueAtTime(0.09, startedAt + 0.012);
      gain.gain.exponentialRampToValueAtTime(0.0001, startedAt + 0.2);
      oscillator.connect(gain);
      gain.connect(context.destination);
      oscillator.start(startedAt);
      oscillator.stop(startedAt + 0.21);
    }).catch(() => undefined);
  } catch {
    // The persistent visual signal remains authoritative when audio is unavailable.
  }
}

function describeWorkspaceBootstrapError(caught: unknown): string {
  if (typeof caught === "object" && caught !== null) {
    const failure = caught as { message?: unknown; statusCode?: unknown };
    const message = typeof failure.message === "string" ? failure.message.trim() : "";
    const statusCode = typeof failure.statusCode === "number" ? failure.statusCode : undefined;

    if (statusCode === 401)
      return "Tu sesión ya no es válida para Punto de venta. Inicia sesión nuevamente.";
    if (statusCode === 403)
      return message || "Tu usuario no tiene permiso para facturar en esta empresa.";
    if (message) return message;
  }

  if (caught instanceof Error && caught.message.trim()) return caught.message;
  return "No fue posible preparar las sedes y bodegas disponibles.";
}

function describeCaptureFailure(result: PosCaptureResult) {
  if (result.status === "InsufficientInventory") {
    const available = result.availability?.availableQuantity;
    return {
      error: available == null
        ? "No hay inventario suficiente en esta bodega."
        : `No hay inventario suficiente en esta bodega. Disponible: ${available}.`,
      message: "Inventario insuficiente",
    };
  }
  return null;
}

function authorizationIsCurrent(authorization: PosSensitiveAuthorization | null) {
  return Boolean(
    authorization &&
    (!authorization.expiresAt || Date.parse(authorization.expiresAt) > Date.now()),
  );
}

async function runOnlineOrderRequest<T>(operation: () => Promise<T>): Promise<T> {
  try {
    return await operation();
  } catch (caught) {
    if (
      caught instanceof TypeError ||
      (caught instanceof DOMException &&
        (caught.name === "AbortError" || caught.name === "TimeoutError"))
    )
      throw new Error(
        "No hay conexión con Auraly. Pedidos requiere conexión con el servidor; inténtalo nuevamente cuando vuelva la red.",
      );
    throw caught;
  }
}

export default function PosPage() {
  const scanner = useRef<HTMLInputElement>(null);
  const captureInFlight = useRef(false);
  const router = useRouter();
  const permissions = useAuthStore((state) => state.user?.permissions ?? []);
  const cloudUser = useAuthStore((state) => state.user);
  const cloudAuthenticated = useAuthStore((state) => state.isAuthenticated);
  const logoutCloud = useAuthStore((state) => state.logout);
  const quantityInputs = useRef(new Map<string, HTMLInputElement>());
  const lineRows = useRef(new Map<string, HTMLTableRowElement>());
  const recoveredOrderFromUrl = useRef<string | null>(null);
  const skipQuantityBlur = useRef<string | null>(null);
  const closureOperationId = useRef<string | null>(null);
  const closureAuthorization = useRef<PosSensitiveAuthorization | null>(null);
  const lineRemovalAuthorization = useRef<PosSensitiveAuthorization | null>(null);
  const restartAuthorization = useRef<PosSensitiveAuthorization | null>(null);
  const temporaryRemovalAuthorization = useRef<PosSensitiveAuthorization | null>(null);
  const shortcutAction = useRef<(event: KeyboardEvent, shortcut: string) => void>(() => undefined);
  const protectedActionHandlers = useRef({
    discount: () => Promise.resolve(),
    removeLine: (lineId: string) => { void lineId; return Promise.resolve(); },
    restartSale: () => Promise.resolve(),
  });
  const initialEdgeHealth = useRef<{
    client: PosEdgeClient;
    health: Awaited<ReturnType<PosClient["health"]>>;
  } | null>(null);
  const webOrderClient = useRef<{
    key: string;
    client: OnlinePosClient;
  } | null>(null);
  const edgeClientBeforeOnlineOrder = useRef<PosEdgeClient | null>(null);
  const [client, setClient] = useState<PosClient | null>(null);
  const [workspaceChanging, setWorkspaceChanging] = useState(false);
  const [onlineOptions, setOnlineOptions] = useState<SalesWorkspaceOption[]>([]);
  const [onlineTenantName, setOnlineTenantName] = useState("");
  const [onlineUserName, setOnlineUserName] = useState("");
  const [onlineUserId, setOnlineUserId] = useState("");
  const [edgeEnrollmentToken, setEdgeEnrollmentToken] = useState<string | null>(null);
  const [edgeEnrollmentRequired, setEdgeEnrollmentRequired] = useState(false);
  const [canEnrollOffline, setCanEnrollOffline] = useState(false);
  const [enrollmentAvailability, setEnrollmentAvailability] = useState<{
    active: number;
    maximum: number;
    reason: string | null;
  } | null>(null);
  const [edgeLoginState, setEdgeLoginState] = useState<"preparing" | "required" | null>(null);
  const [edgeLoginError, setEdgeLoginError] = useState<string | null>(null);
  const [preparationHealth, setPreparationHealth] = useState<PosPreparationHealth | null>(null);
  const [edgePermissions, setEdgePermissions] = useState<string[]>([]);
  const [setupLoading, setSetupLoading] = useState(true);
  const [setupError, setSetupError] = useState<string | null>(null);
  const [setupNotice, setSetupNotice] = useState<string | null>(null);
  const [preparationActive, setPreparationActive] = useState(false);
  const preparationActiveRef = useRef(false);
  const [preparationSelection, setPreparationSelection] = useState<{
    option: SalesWorkspaceOption;
    documentType: PosSaleDocumentType;
  } | null>(null);
  const [workspaceConfigurationOffline, setWorkspaceConfigurationOffline] =
    useState(false);
  const [draft, setDraft] = useState<PosDraft | null>(null);
  const [temporaries, setTemporaries] = useState<PosDraft[]>([]);
  const [scan, setScan] = useState("");
  const rejectionTimer = useRef<number | null>(null);
  const [scanRejection, setScanRejection] = useState<{
    phase: "idle" | "animating" | "latched";
    value: string;
  }>({ phase: "idle", value: "" });
  const [quantityShortage, setQuantityShortage] = useState<PosQuantityShortage | null>(null);
  const pendingShortageCapture = useRef<
    { kind: "code"; code: string } | { kind: "product"; product: PosCatalogProduct } | null
  >(null);
  const saveOrderAfterCustomerSelection = useRef(false);
  const [productSearchFocusRequest, setProductSearchFocusRequest] = useState(0);
  const [productAvailabilityRequest, setProductAvailabilityRequest] = useState(0);
  const [paymentFocusRequest, setPaymentFocusRequest] = useState(0);
  const [quantityDrafts, setQuantityDrafts] = useState<Record<string, string>>({});
  const [busy, setBusy] = useState(false);
  const [edgeReady, setEdgeReady] = useState(false);
  const [serverConnected, setServerConnected] = useState(false);
  const [pushConnected, setPushConnected] = useState(false);
  const [synchronization, setSynchronization] = useState({
    inProgress: false,
    automaticRetryScheduled: false,
    automaticRetryAttempt: 0,
    lastAt: null as string | null,
    failed: false,
    pendingCount: 0,
    oldestPendingAt: null as string | null,
    error: null as string | null,
  });
  const [workstation, setWorkstation] = useState({
    deviceSeriesCode: "\u2014",
    businessId: "",
    warehouseId: "",
    businessName: "",
    warehouseName: "",
    warehouseAllowsNegativeStockSales: false,
    userDisplayName: "\u2014",
    userId: null as string | null,
    workSessionId: null as string | null,
    deviceId: null as string | null,
    fiscalReady: false,
    fiscalWarnings: [] as string[],
    dianQuotaAvailable: null as boolean | null,
  });
  const canOpenAdministrativeMenu = canOpenPosAdministrativeMenu(
    cloudAuthenticated,
    permissions,
  );
  const canChangeWorkspace = (client?.mode === "edge" ? edgePermissions : permissions)
    .includes("pos.workspace.change");

  function applyWorkspaceBootstrap(
    bootstrap: SalesWorkspaceBootstrap,
    fallbackDisplayName = "Cajero",
  ) {
    const displayName = bootstrap.userDisplayName.trim() || fallbackDisplayName;
    window.localStorage.setItem("selected_tenant_id", bootstrap.tenantId);
    setCanEnrollOffline(bootstrap.canEnrollPosDevice);
    setEnrollmentAvailability({
      active: bootstrap.activeEnrolledDeviceCount,
      maximum: bootstrap.maximumEnrolledDevices,
      reason: bootstrap.enrollmentUnavailableReason,
    });
    setOnlineOptions(bootstrap.options);
    setOnlineTenantName(bootstrap.tenantName.trim());
    setOnlineUserName(displayName);
    setOnlineUserId(bootstrap.userId);
    return { displayName, options: bootstrap.options };
  }
  const [returnsOpen, setReturnsOpen] = useState(false);
  const [synchronizationEventsOpen, setSynchronizationEventsOpen] = useState(false);
  const [message, setMessage] = useState("Esperando producto");
  const [error, setError] = useState<string | null>(null);
  const [temporaryOpen, setTemporaryOpen] = useState(false);
  const [temporaryName, setTemporaryName] = useState("");
  const [paymentOpen, setPaymentOpen] = useState(false);
  const [saleSettlement, setSaleSettlement] = useState<import("@/services/pos/pos-edge-client").PosSaleSettlement | null>(null);
  const [saleSettlementError, setSaleSettlementError] = useState(false);
  const [inventoryResolution, setInventoryResolution] = useState<PosInventoryValidation | null>(null);
  const [productSearchOpen, setProductSearchOpen] = useState(false);
  const [priceVerifierMode, setPriceVerifierMode] = useState(false);
  const [customerSearchOpen, setCustomerSearchOpen] = useState(false);
  const [discountOpen, setDiscountOpen] = useState(false);
  const [genericProductLine, setGenericProductLine] = useState<PosDraft["lines"][number] | null>(null);
  const [invoiceSearchOpen, setInvoiceSearchOpen] = useState(false);
  const [documentTypeOpen, setDocumentTypeOpen] = useState(false);
  const [cashMovementDirection, setCashMovementDirection] =
    useState<PosCashMovementDirection | null>(null);
  const [denominationCalculatorOpen, setDenominationCalculatorOpen] = useState(false);
  const [closurePreview, setClosurePreview] =
    useState<PosAuthorizedClosurePreview | null>(null);
  const [closureAttempt, setClosureAttempt] = useState<{
    countedCash: number;
    paymentCounts: PosWorkSessionPaymentCount[];
    note: string | null;
  } | null>(null);

  useEffect(() => {
    let cancelled = false;
    if (!client || !draft?.lines.length) {
      setSaleSettlement(null);
      setSaleSettlementError(false);
      return () => { cancelled = true; };
    }
    void client.previewSettlement(draft.draftId.value)
      .then((value) => {
        if (!cancelled) {
          setSaleSettlement(value);
          setSaleSettlementError(false);
        }
      })
      .catch(() => {
        if (!cancelled) {
          setSaleSettlement(null);
          setSaleSettlementError(true);
        }
      });
    return () => { cancelled = true; };
  }, [client, draft]);
  const [printerOpen, setPrinterOpen] = useState(false);
  const [selectedCustomer, setSelectedCustomer] = useState<PosCustomer | null>(null);
  const [pricingTransition, setPricingTransition] = useState(false);
  const [documentType, setDocumentType] = useState<PosSaleDocumentType>("SalesReceipt");
  const documentTypeRef = useRef<PosSaleDocumentType>(documentType);

  useEffect(() => {
    documentTypeRef.current = documentType;
  }, [documentType]);

  const clearScanRejection = useCallback(() => {
    if (rejectionTimer.current !== null) window.clearTimeout(rejectionTimer.current);
    rejectionTimer.current = null;
    setScanRejection({ phase: "idle", value: "" });
  }, []);

  const rejectScan = useCallback((value: string) => {
    if (rejectionTimer.current !== null) window.clearTimeout(rejectionTimer.current);
    setScanRejection({ phase: "animating", value });
    playRejectedScanTone();
    rejectionTimer.current = window.setTimeout(() => {
      setScanRejection({ phase: "latched", value });
      rejectionTimer.current = null;
    }, 720);
  }, []);

  useEffect(() => () => {
    if (rejectionTimer.current !== null) window.clearTimeout(rejectionTimer.current);
  }, []);

  const [sidePanel, setSidePanel] = useState<"temporaries" | "orders">("temporaries");
  const [ordersRefreshVersion, setOrdersRefreshVersion] = useState(0);
  const [ordersCount, setOrdersCount] = useState(0);
  const [ordersExpanded, setOrdersExpanded] = useState(false);
  const [selectedLineId, setSelectedLineId] = useState<string | null>(null);
  const [confirmation, setConfirmation] = useState<
    | { kind: "line"; lineId: string; productName: string }
    | { kind: "temporary"; draftId: string; name: string }
    | { kind: "sale"; sourceOrderNumber: string | null }
    | { kind: "order-save"; orderNumber: string }
    | null
  >(null);
  const [sensitiveApproval, setSensitiveApproval] = useState<{
    approval: PosApprovalRequest | null;
    operationId: string;
    permissionResource: string;
    lineId: string | null;
    context: Record<string, unknown>;
    execute: (authorization: PosSensitiveAuthorization) => Promise<void>;
  } | null>(null);
  const [sensitiveApprovalError, setSensitiveApprovalError] = useState<string | null>(null);
  const salesSessionButton = useRef<HTMLButtonElement>(null);
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
  const orderSaveAvailable = canRequestOrderSave({
    lineCount: draft?.lines.length ?? 0,
    busy,
  });
  // Online commands own their connectivity result: attempt the canonical API
  // and show its real error instead of blocking on a stale health snapshot.
  const salesReady = client?.mode === "online" ? Boolean(client) : edgeReady;

  const revealLine = useCallback((lineId: string | null) => {
    if (!lineId) return;
    window.requestAnimationFrame(() =>
      window.requestAnimationFrame(() =>
        lineRows.current.get(lineId)?.scrollIntoView({
          block: "end",
          behavior: "auto",
        }),
      ),
    );
  }, []);
  const focusScanner = useCallback(() => {
    window.requestAnimationFrame(() => window.requestAnimationFrame(() => {
      if (document.querySelector('[data-pos-focus-surface="modal"]')) return;
      scanner.current?.focus({ preventScroll: true });
    }));
  }, []);
  const focusProductSearch = useCallback(() => {
    setProductSearchFocusRequest((current) => current + 1);
  }, []);
  const showError = useCallback((caught: unknown) => {
    const status = caught instanceof PosEdgeError ? caught.status : 0;
    const onlineTransportFailure = client?.mode === "online" && caught instanceof TypeError;
    const publicError = caught instanceof Error
      ? posPublicError(caught.message, "No fue posible completar la operación.")
      : null;
    if (onlineTransportFailure) {
      setEdgeReady(false);
      setServerConnected(false);
    }
    const text = onlineTransportFailure
      ? "No hay conexión con Auraly. La venta en línea requiere conexión con el servidor."
      : client?.mode === "online" && caught instanceof PosEdgeError
        ? publicError
        : status === 409 && caught instanceof PosEdgeError
          ? publicError
          : status === 404
            ? "Producto no encontrado en el catálogo local"
            : status === 503 && caught instanceof PosEdgeError && caught.message.includes("tirilla")
                ? "La factura fue emitida, pero la tirilla no pudo imprimirse. Reintenta sin modificar la venta."
                : status === 503
                  ? "La bodega exige validar inventario y no hay conexión"
                  : "No fue posible acceder a los servicios locales del equipo";
    setError(text);
    setMessage("Revisa la novedad");
  }, [client?.mode]);

  const getWebOrderClient = useCallback(async () => {
    if (client instanceof OnlinePosClient) return client;
    if (!cloudUser)
      throw new Error("La sesión web de pedidos no está disponible.");
    if (!workstation.businessId || !workstation.warehouseId)
      throw new Error("Selecciona la sede y la bodega antes de trabajar con pedidos.");
    const key = [
      cloudUser.userId,
      workstation.businessId,
      workstation.warehouseId,
      workstation.workSessionId ?? "no-session",
      edgeEnrollmentToken ?? "browser",
    ].join(":");
    if (webOrderClient.current?.key === key)
      return webOrderClient.current.client;
    const context = await selectSalesWorkspace({
      businessId: workstation.businessId,
      businessName: workstation.businessName,
      warehouseId: workstation.warehouseId,
      warehouseCode: workstation.warehouseId,
      warehouseName: workstation.warehouseName,
      warehouseAllowsNegativeStockSales:
        workstation.warehouseAllowsNegativeStockSales,
      hasActiveEdgeEnrollment: Boolean(edgeEnrollmentToken),
      fiscalReadyForOnlineSales: workstation.fiscalReady,
      fiscalReadyForEnrollment: workstation.fiscalReady,
      hasDianDocumentQuota: workstation.dianQuotaAvailable !== false,
      fiscalWarningMessages: workstation.fiscalWarnings,
    });
    const online = new OnlinePosClient(
      context,
      cloudUser.userId,
      `${cloudUser.firstName} ${cloudUser.lastName}`.trim() || cloudUser.username,
      edgeEnrollmentToken,
    );
    webOrderClient.current = { key, client: online };
    return online;
  }, [
    client,
    cloudUser,
    edgeEnrollmentToken,
    workstation.businessId,
    workstation.businessName,
    workstation.dianQuotaAvailable,
    workstation.fiscalReady,
    workstation.fiscalWarnings,
    workstation.warehouseAllowsNegativeStockSales,
    workstation.warehouseId,
    workstation.warehouseName,
    workstation.workSessionId,
  ]);

  const recoverOrderOnline = useCallback(async (orderId: string) => {
    const { orderClient, recovered } = await runOnlineOrderRequest(async () => {
      const online = await getWebOrderClient();
      return { orderClient: online, recovered: await online.recoverOrder(orderId) };
    });
    if (client instanceof PosEdgeClient)
      edgeClientBeforeOnlineOrder.current = client;
    if (client !== orderClient) setClient(orderClient);
    return { orderClient, recovered };
  }, [client, getWebOrderClient]);

  const saveDraftAsOnlineOrder = useCallback(async (value: PosDraft) => {
    return runOnlineOrderRequest(async () => {
      const orderClient = await getWebOrderClient();
      const edge = client instanceof PosEdgeClient ? client : null;
      return orderClient.saveOrder(
        value,
        edge
          ? () => edge.clearAfterOnlineCommit(value.draftId.value)
          : undefined,
      );
    });
  }, [client, getWebOrderClient]);

  const printOrdersOnline = useCallback((orderIds: string[]) =>
    runOnlineOrderRequest(async () =>
      (await getWebOrderClient()).printOrders(orderIds)),
  [getWebOrderClient]);
  useEffect(() => {
    const saved = window.localStorage.getItem("auraly.pos.document-type");
    if (saved === "SalesInvoice" || saved === "SalesReceipt")
      setDocumentType(saved);
  }, []);

  useEffect(() => {
    if (client instanceof PosEdgeClient && edgeLoginState === "required")
      window.location.replace("/login");
  }, [client, edgeLoginState]);


  useEffect(() => {
    setQuantityDrafts(
      Object.fromEntries(
        (draft?.lines ?? []).map((line) => [line.lineId, String(line.quantity)]),
      ),
    );
  }, [draft]);

  const refreshTemporaries = useCallback(async () => {
    if (!client) return;
    setTemporaries(await client.temporaries());
  }, [client]);

  useEffect(() => {
    if (!client) {
      setOrdersCount(0);
      return;
    }
    if (sidePanel === "orders") return;
    let active = true;
    const range = orderDayRange(localOrderDateValue());
    void loadCommerceOrders({ ...range, status: "Available", page: 1, pageSize: 1 })
      .then((page) => {
        if (active) setOrdersCount(page.totalCount);
      })
      .catch(() => {
        if (active) setOrdersCount(0);
      });
    return () => { active = false; };
  }, [client, ordersRefreshVersion, sidePanel]);

  useEffect(() => {
    let active = true;
    const bootstrap = async () => {
      const workspaceChangeRequested =
        new URLSearchParams(window.location.search).get("workspace") === "change";
      setSetupLoading(true);
      setSetupError(null);
      try {
        const edgeToken = readEdgeTokenFromLaunch();
        if (edgeToken) {
          if (active) setEdgeEnrollmentToken(edgeToken);
          try {
            const edgeClient = new PosEdgeClient(edgeToken, readEdgeUserSession());
            let health = await edgeClient.health();
            if (health.userId && !health.workSessionId) {
              const session = await edgeClient.openWorkSession();
              health = {
                ...health,
                userDisplayName: session.displayName,
                userId: session.userId,
                workSessionId: session.workSessionId,
                permissions: session.permissions,
              };
            }
            if (active) {
              setPreparationHealth(health);
              setEdgePermissions(health.permissions ?? []);
              setSynchronization({
                inProgress: health.synchronizationInProgress,
                automaticRetryScheduled: health.automaticRetryScheduled ?? false,
                automaticRetryAttempt: health.automaticRetryAttempt ?? 0,
                lastAt: health.lastSynchronizationAt,
                failed: health.lastSynchronizationFailed,
                pendingCount: health.pendingSynchronizationCount,
                oldestPendingAt: health.oldestPendingSynchronizationAt,
                error: posPublicError(health.lastSynchronizationError),
              });
              setServerConnected(health.serverConnected);
              setPushConnected(health.pushConnected);
              setWorkstation({
                deviceSeriesCode: health.deviceSeriesCode,
                businessId: health.businessId,
                warehouseId: health.warehouseId,
                businessName: health.businessName,
                warehouseName: health.warehouseName,
                warehouseAllowsNegativeStockSales: health.warehouseAllowsNegativeStockSales,
                userDisplayName: health.userDisplayName || "\u2014",
                userId: health.userId,
                workSessionId: health.workSessionId ?? null,
                deviceId: health.deviceId ?? null,
                fiscalReady: health.fiscalReady,
                fiscalWarnings: health.fiscalWarnings ?? [],
                dianQuotaAvailable: health.dianQuotaAvailable ?? null,
              });
              if (health.dianQuotaAvailable === false && documentTypeRef.current === "SalesInvoice")
                setError(dianQuotaExhaustedMessage);
            }

            if (shouldUseEnrolledPosRuntime(health, workspaceChangeRequested)) {
              if (active) {
                initialEdgeHealth.current = { client: edgeClient, health };
                setEdgeLoginState(
                  !health.identityReady || isPosPreparationPending(health.status)
                    ? "preparing"
                    : health.status === "LoginRequired"
                      ? "required"
                      : null,
                );
                setClient(edgeClient);
              }
              return;
            }
          } catch {
            if (active) {
              setClient(new PosEdgeClient(edgeToken, readEdgeUserSession()));
              setEdgeLoginState("preparing");
              setEdgeLoginError(
                "El servicio local está reiniciando. Auraly volverá a conectarse automáticamente.",
              );
            }
            return;
          }
        }

        const serverBootstrap = await loadSalesWorkspaceBootstrap();
        if (!active) return;
        setEdgeEnrollmentRequired(Boolean(edgeToken));
        const { displayName, options: available } =
          applyWorkspaceBootstrap(serverBootstrap);
        if (workspaceChangeRequested) {
          setWorkspaceChanging(true);
          return;
        }
        const remembered = rememberedSalesWorkspaceKey();
        if (!shouldAutoActivateRememberedWorkspace(remembered)) return;
        const selected = available.find(
          (option) => salesWorkspaceKey(option.businessId, option.warehouseId) === remembered,
        );
        if (selected) {
          window.localStorage.setItem("selected_business_id", selected.businessId);
          const context = await selectSalesWorkspace(selected);
          if (active) {
            const onlineClient = new OnlinePosClient(
              context,
              serverBootstrap.userId,
              displayName,
              edgeToken,
            );
            setClient(onlineClient);
          }
        }
      } catch (caught) {
        if (!active) return;
        setSetupError(describeWorkspaceBootstrapError(caught));
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
    let refreshRequested = false;
    let hydrated = false;
    let stopLiveState: (() => void) | null = null;
    let reconnectTimer: number | null = null;
    let failedReconnectAttempts = 0;

    const scheduleReconnect = () => {
      if (!active || client.mode !== "online" || reconnectTimer !== null) return;
      const delay = realtimeReconnectDelay(failedReconnectAttempts);
      failedReconnectAttempts += 1;
      if (delay === null) return;
      reconnectTimer = window.setTimeout(() => {
        reconnectTimer = null;
        void connect();
      }, delay);
    };

    const applyHealth = (health: Awaited<ReturnType<typeof client.health>>) => {
      if (!active) return;
      if (client.mode === "edge") setPreparationHealth(health);
      if (client.mode === "edge") setEdgePermissions(health.permissions ?? []);
      setSynchronization({
        inProgress: health.synchronizationInProgress,
        automaticRetryScheduled: health.automaticRetryScheduled ?? false,
        automaticRetryAttempt: health.automaticRetryAttempt ?? 0,
        lastAt: health.lastSynchronizationAt,
        failed: health.lastSynchronizationFailed,
        pendingCount: health.pendingSynchronizationCount,
        oldestPendingAt: health.oldestPendingSynchronizationAt,
        error: posPublicError(health.lastSynchronizationError),
      });
      setServerConnected(health.serverConnected);
      setPushConnected(health.pushConnected);
      setWorkstation({
        deviceSeriesCode: health.deviceSeriesCode,
        businessId: health.businessId,
        warehouseId: health.warehouseId,
        businessName: health.businessName,
        warehouseName: health.warehouseName,
        warehouseAllowsNegativeStockSales: health.warehouseAllowsNegativeStockSales,
        userDisplayName: health.userDisplayName || "\u2014",
        userId: health.userId,
        workSessionId: health.workSessionId ?? null,
        deviceId: health.deviceId ?? null,
        fiscalReady: health.fiscalReady,
        fiscalWarnings: health.fiscalWarnings ?? [],
        dianQuotaAvailable: health.dianQuotaAvailable ?? null,
      });
      if (health.dianQuotaAvailable === false && documentType === "SalesInvoice")
        setError(dianQuotaExhaustedMessage);
    };

    const connect = async () => {
      if (checking) {
        refreshRequested = true;
        return;
      }
      checking = true;
      try {
        const preparedHealth = initialEdgeHealth.current?.client === client
          ? initialEdgeHealth.current.health
          : null;
        if (preparedHealth) initialEdgeHealth.current = null;
        let health = preparedHealth ?? await client.health();
        applyHealth(health);
        if (client instanceof PosEdgeClient && shouldCompletePosEnrollment(
          health.status,
          health.initialEnrollmentSessionAvailable === true,
          Boolean(health.userId),
        )) {
          try {
            const completed = await completePendingPosEnrollment(client);
            health = completed.health;
            const session = completed.session;
            applyHealth(health);
            if (active) {
              setWorkstation((current) => ({
                ...current,
                userDisplayName: session.displayName,
                userId: session.userId,
                workSessionId: session.workSessionId,
              }));
              setEdgePermissions(session.permissions);
              setEdgeLoginError(null);
              const preparationPending = isPosPreparationPending(health.status);
              setEdgeLoginState(preparationPending ? "preparing" : null);
              setEdgeReady(!preparationPending);
            }
          } catch (caught) {
            if (active) {
              setEdgeLoginState("preparing");
              setEdgeLoginError(posPublicError(
                caught instanceof Error ? caught.message : null,
                "No fue posible abrir la sesión local inicial.",
              ) ?? "No fue posible abrir la sesión local inicial.");
            }
            return;
          }
        }
        if (client instanceof PosEdgeClient && health.userId && !health.workSessionId) {
          const session = await client.openWorkSession();
          if (active) {
            setWorkstation((current) => ({
              ...current,
              userDisplayName: session.displayName,
              userId: session.userId,
              workSessionId: session.workSessionId,
            }));
            setEdgePermissions(session.permissions);
          }
        }
        if (
          client.mode === "edge" &&
          (isPosPreparationPending(health.status) || health.status === "LoginRequired")
        ) {
          if (active) {
            setEdgeReady(false);
            setEdgeLoginState(
              isPosPreparationPending(health.status)
                ? "preparing"
                : "required",
            );
          }
          return;
        }
        if (active && client.mode === "edge") {
          if (preparationActiveRef.current && health.status === "Ready") {
            // Keep the terminal checkpoint visible before opening the POS.
            await new Promise((resolve) => window.setTimeout(resolve, 350));
          }
          if (!active) return;
          setEdgeLoginState(null);
          preparationActiveRef.current = false;
          setPreparationActive(false);
          setSetupNotice(null);
          setSetupError(null);
        }
        if (!hydrated) {
          const [current, pending, numbers] = await Promise.all([
            client.activeDraft(),
            client.temporaries(),
            client.nextNumbers(documentType),
          ]);
          if (!active) return;
          setDraft(current);
          setTemporaries(pending);
          setNextNumber(numbers?.document ?? null);
          setSelectedCustomer(
            current.customerId
              ? await client.customer(current.customerId, current.customerPartySiteId)
              : null,
          );
          hydrated = true;
          focusScanner();
        }
        if (active) {
          failedReconnectAttempts = 0;
          if (reconnectTimer !== null) window.clearTimeout(reconnectTimer);
          reconnectTimer = null;
          setEdgeReady(true);
        }
      } catch (caught) {
        hydrated = false;
        if (active) {
          setEdgeReady(false);
          setServerConnected(false);
          if (client.mode === "edge" && caught instanceof PosEdgeError && caught.status === 401) {
            setEdgeLoginState("required");
          }
        }
        scheduleReconnect();
      } finally {
        checking = false;
        if (active && refreshRequested) {
          refreshRequested = false;
          void connect();
        }
      }
    };

    const startLiveState = () => {
      if (!active || stopLiveState) return;
      stopLiveState = client instanceof PosEdgeClient
        ? client.watchLocalState(() => void connect())
        : client instanceof OnlinePosClient
          ? client.watchWarehousePolicy((allowsNegativeStock) =>
              setWorkstation((current) => ({
                ...current,
                warehouseAllowsNegativeStockSales: allowsNegativeStock,
              })))
          : null;
    };
    void connect().finally(startLiveState);
    const handleOnline = () => {
      failedReconnectAttempts = 0;
      void connect();
    };
    const handleOffline = () => {
      if (client.mode === "online") {
        setServerConnected(false);
        setEdgeReady(false);
      }
    };
    const handleVisibility = () => {
      if (document.visibilityState !== "visible") return;
      failedReconnectAttempts = 0;
      void connect();
    };
    window.addEventListener("online", handleOnline);
    window.addEventListener("offline", handleOffline);
    document.addEventListener("visibilitychange", handleVisibility);
    return () => {
      active = false;
      if (reconnectTimer !== null) window.clearTimeout(reconnectTimer);
      stopLiveState?.();
      window.removeEventListener("online", handleOnline);
      window.removeEventListener("offline", handleOffline);
      document.removeEventListener("visibilitychange", handleVisibility);
    };
  }, [client, documentType, focusScanner]);

  useEffect(() => {
    if (!client || !edgeReady || !draft || busy || typeof window === "undefined") return;
    const recoveryRequest = consumeOrderRecoveryUrl(window.location.href);
    const orderId = recoveryRequest.orderId;
    if (!orderId || recoveredOrderFromUrl.current === orderId) return;
    recoveredOrderFromUrl.current = orderId;
    window.history.replaceState(null, "", recoveryRequest.nextUrl);
    setBusy(true);
    setError(null);
    void recoverOrderOnline(orderId)
      .then(({ orderClient, recovered }) => {
        setDraft(recovered);
        setSelectedCustomer(null);
        if (recovered.customerId) {
          void orderClient.customer(recovered.customerId, recovered.customerPartySiteId)
            .then(setSelectedCustomer)
            .catch(() => setMessage("Pedido recuperado; no fue posible actualizar el cliente."));
        }
        setSelectedLineId(recovered.lines[0]?.lineId ?? null);
        setSidePanel("temporaries");
        setMessage(`Pedido recuperado · ${recovered.lines.length} líneas`);
      })
      .catch((caught) => {
        recoveredOrderFromUrl.current = null;
        showError(caught);
      })
      .finally(() => {
        setBusy(false);
        focusScanner();
      });
  }, [busy, client, draft, edgeReady, focusScanner, recoverOrderOnline, showError]);

  useEffect(() => {
    if (!(client instanceof OnlinePosClient) || !draft?.sourceOrderId || busy) return;
    const orderId = draft.sourceOrderId;
    const handleRenewalFailure = (caught: unknown) => {
      setError(caught instanceof Error
        ? caught.message
        : "Se perdió la ocupación del pedido; recupéralo nuevamente antes de continuar.");
      setMessage("El pedido ya no está ocupado por esta sesión");
    };
    let stopped = false;
    let timer = window.setTimeout(async function renewAndSchedule() {
      try {
        await client.renewRecoveredOrder(orderId);
        if (!stopped) timer = window.setTimeout(renewAndSchedule, 4 * 60 * 1000);
      } catch (caught) {
        if (!stopped) handleRenewalFailure(caught);
      }
    }, 4 * 60 * 1000);
    const releaseOnPageExit = () => {
      void client.releaseRecoveredOrder(orderId).catch(() => undefined);
    };
    window.addEventListener("pagehide", releaseOnPageExit);
    return () => {
      stopped = true;
      window.clearTimeout(timer);
      window.removeEventListener("pagehide", releaseOnPageExit);
    };
  }, [busy, client, draft?.sourceOrderId]);

  protectedActionHandlers.current = {
    discount: openDiscount,
    removeLine: requestRemoveLine,
    restartSale: requestCancelSale,
  };

  async function requestRemoveLine(lineId: string) {
    const line = draft?.lines.find((candidate) => candidate.lineId === lineId);
    if (!line || busy) return;
    if (removingLastRecoveredOrderLineCancelsOrder({
      sourceOrderId: draft?.sourceOrderId,
      lineCount: draft?.lines.length ?? 0,
    })) {
      await requestCancelSale();
      return;
    }
    try {
      await authorizeSensitiveEntry(
        "sales.lines.remove",
        lineId,
        { action: "OpenRemoveLine", product: line.description, quantity: line.quantity },
        async (authorization) => {
          lineRemovalAuthorization.current = authorization;
          setConfirmation({ kind: "line", lineId, productName: line.description });
        },
      );
    } catch (caught) {
      showError(caught);
    }
  }

  async function requestCancelSale() {
    if (!draft?.lines.length || busy) return;
    try {
      await authorizeSensitiveEntry(
        "sales.drafts.restart",
        null,
        { action: "OpenRestartSale", lineCount: draft.lines.length, total: draft.payableAmount },
        async (authorization) => {
          restartAuthorization.current = authorization;
          setConfirmation({
            kind: "sale",
            sourceOrderNumber: draft.sourceOrderId ? draft.reference || "el pedido recuperado" : null,
          });
        },
      );
    } catch (caught) {
      showError(caught);
    }
  }

  const persistTemporary = useCallback(async (name: string, reference: string) => {
    const normalizedName = name.trim();
    if (!client || !draft || !normalizedName || busy) return;
    setBusy(true);
    try {
      await client.saveTemporary(draft.draftId.value, normalizedName, reference, "");
      setDraft(await client.activeDraft());
      setSelectedCustomer(null);
      setSelectedLineId(null);
      setScan("");
      setLastSettlement(null);
      await refreshTemporaries();
      setTemporaryOpen(false);
      setTemporaryName("");
      setTemporaryReference("");
      setMessage("Venta pausada");
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "No fue posible pausar la venta.");
    } finally {
      setBusy(false);
      focusScanner();
    }
  }, [busy, client, draft, focusScanner, refreshTemporaries]);

  const requestPauseSale = useCallback(async () => {
    const customerName = temporaryNameForCustomer(selectedCustomer);
    if (!customerName) {
      setTemporaryName("");
      setTemporaryReference("");
      setTemporaryOpen(true);
      return;
    }
    await persistTemporary(customerName, "");
  }, [persistTemporary, selectedCustomer]);

  const saveOrder = useCallback(async () => {
    if (!client || !draft?.lines.length || busy) return;
    if (orderSaveRequiresCustomerSelection(draft.customerId)) {
      saveOrderAfterCustomerSelection.current = true;
      setError("Selecciona un cliente antes de guardar el pedido.");
      setMessage("El pedido necesita cliente");
      setCustomerSearchOpen(true);
      return;
    }
    setBusy(true);
    setError(null);
    try {
      const wasRecovered = Boolean(draft.sourceOrderId);
      const saved = await saveDraftAsOnlineOrder(draft);
      setDraft(saved.nextDraft);
      setSelectedCustomer(null);
      setSelectedLineId(null);
      setScan("");
      setLastSettlement(null);
      setSidePanel("orders");
      setOrdersRefreshVersion((current) => current + 1);
      setMessage(`${saved.order.orderNumber} ${wasRecovered ? "actualizado" : "guardado"}; inventario reservado en Pedidos`);
      if (wasRecovered && edgeClientBeforeOnlineOrder.current) {
        setClient(edgeClientBeforeOnlineOrder.current);
        edgeClientBeforeOnlineOrder.current = null;
      }
    } catch (caught) {
      const detail = typeof caught === "object" && caught !== null && "message" in caught
        ? String((caught as { message: unknown }).message)
        : "No fue posible guardar el pedido.";
      setError(detail);
      setMessage("Revisa la novedad del pedido");
    } finally {
      setBusy(false);
      focusScanner();
    }
  }, [busy, client, draft, focusScanner, saveDraftAsOnlineOrder]);

  const requestSaveOrder = useCallback(() => {
    if (!draft?.sourceOrderId) {
      void saveOrder();
      return;
    }
    setConfirmation({
      kind: "order-save",
      orderNumber: draft.reference?.trim() || draft.sourceOrderId,
    });
  }, [draft, saveOrder]);

  const activePosPermissions = client?.mode === "edge" ? edgePermissions : permissions;
  const canOpenCashDrawer = activePosPermissions
    .includes("work-sessions.cash.drawer.open");
  const canReadProductAvailability = (client?.mode === "edge" ? edgePermissions : permissions)
    .includes("pos.inventory.availability.read");

  const openCashDrawer = useCallback(async () => {
    if (!client || busy || !workstation.workSessionId) return;
    setBusy(true);
    setError(null);
    try {
      await client.openCashDrawer();
      setMessage("Cajón de dinero abierto");
    } catch (caught) {
      showError(caught);
    } finally {
      setBusy(false);
    }
  }, [busy, client, showError, workstation.workSessionId]);

  shortcutAction.current = (event, shortcut) => {
    if (
      invoiceSearchOpen ||
      returnsOpen ||
      documentTypeOpen ||
      cashMovementDirection ||
      denominationCalculatorOpen ||
      closurePreview
    ) return;
    const canOpenSessionAction =
        Boolean(workstation.workSessionId) &&
        !busy &&
        !temporaryOpen &&
        !paymentOpen &&
        !productSearchOpen &&
        !customerSearchOpen &&
        !discountOpen &&
        !printerOpen &&
        !denominationCalculatorOpen &&
        !closurePreview &&
        !confirmation;
    if (event.ctrlKey || event.altKey || event.shiftKey) return;
    if (productSearchOpen && shortcut === POS_ACTION_SHORTCUTS.editLines) {
      setProductAvailabilityRequest((current) => current + 1);
      return;
    }
    if (
        !event.ctrlKey &&
        shortcut === POS_ACTION_SHORTCUTS.returns &&
        serverConnected &&
        !busy &&
        !temporaryOpen &&
        !productSearchOpen &&
        !customerSearchOpen &&
        !discountOpen &&
        !paymentOpen &&
        !confirmation
      ) {
        setReturnsOpen(true);
      } else
      if (
        shortcut === POS_ACTION_SHORTCUTS.invoices &&
        !busy &&
        !temporaryOpen &&
        !productSearchOpen &&
        !customerSearchOpen &&
        !discountOpen &&
        !paymentOpen &&
        !confirmation
      ) {
        setInvoiceSearchOpen(true);
      } else if (
        shortcut === POS_ACTION_SHORTCUTS.payment &&
        !busy &&
        Boolean(draft?.lines.length) &&
        !temporaryOpen &&
        !productSearchOpen &&
        !customerSearchOpen &&
        !discountOpen &&
        !paymentOpen &&
        !confirmation
      ) {
        void openPayment();
      } else if (
        shortcut === POS_ACTION_SHORTCUTS.productSearch &&
        !busy &&
        !temporaryOpen &&
        !paymentOpen &&
        !customerSearchOpen &&
        !discountOpen &&
        !confirmation
      ) {
        if (productSearchOpen) {
          setPriceVerifierMode((current) => !current);
          focusProductSearch();
        } else {
          setPriceVerifierMode(false);
          setProductSearchOpen(true);
        }
      } else if (
        !event.ctrlKey &&
        shortcut === POS_ACTION_SHORTCUTS.customerSearch &&
        !busy &&
        Boolean(draft) &&
        !temporaryOpen &&
        !paymentOpen &&
        !productSearchOpen &&
        !discountOpen &&
        !confirmation
      ) {
        setCustomerSearchOpen(true);
      } else if (
        !event.ctrlKey &&
        shortcut === POS_ACTION_SHORTCUTS.pauseSale &&
        !busy &&
        Boolean(draft?.lines.length) &&
        !temporaryOpen &&
        !paymentOpen &&
        !productSearchOpen &&
        !customerSearchOpen &&
        !discountOpen &&
        !confirmation
      ) {
        void requestPauseSale();
      } else if (
        shortcut === POS_ACTION_SHORTCUTS.editLines &&
        !busy &&
        Boolean(draft?.lines.length) &&
        !temporaryOpen &&
        !paymentOpen &&
        !productSearchOpen &&
        !customerSearchOpen &&
        !confirmation
      ) {
        void protectedActionHandlers.current.discount();
      } else if (
        shortcut === POS_ACTION_SHORTCUTS.removeLine &&
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
        void protectedActionHandlers.current.removeLine(selectedLineId);
      } else if (
        shortcut === POS_ACTION_SHORTCUTS.restartSale &&
        !busy &&
        !temporaryOpen &&
        !paymentOpen &&
        !productSearchOpen &&
        !customerSearchOpen &&
        !discountOpen &&
        !confirmation
      ) {
        void protectedActionHandlers.current.restartSale();
      } else if (shortcut === POS_ACTION_SHORTCUTS.closeSession && canOpenSessionAction) {
        salesSessionButton.current?.click();
      } else if (!event.ctrlKey && shortcut === POS_ACTION_SHORTCUTS.saveOrder && orderSaveAvailable) {
        requestSaveOrder();
    }
  };

  useEffect(() => {
    const handleShortcut = (event: KeyboardEvent) => {
      if (paymentOpen) return;
      capturePosFunctionShortcut(event, shortcut => shortcutAction.current(event, shortcut));
    };
    window.addEventListener("keydown", handleShortcut, true);
    return () => window.removeEventListener("keydown", handleShortcut, true);
  }, [paymentOpen]);

  useEffect(() => {
    const handleCashDrawerShortcut = (event: KeyboardEvent) => {
      if (!isPosCashDrawerShortcut(event) || !canOpenCashDrawer) return;

      const canOpenCashMovement =
        Boolean(workstation.workSessionId) &&
        !busy &&
        !temporaryOpen &&
        !paymentOpen &&
        !productSearchOpen &&
        !customerSearchOpen &&
        !discountOpen &&
        !printerOpen &&
        !denominationCalculatorOpen &&
        !closurePreview &&
        !confirmation &&
        !invoiceSearchOpen &&
        !returnsOpen &&
        !documentTypeOpen &&
        !cashMovementDirection;
      if (!canOpenCashMovement) return;

      event.preventDefault();
      event.stopImmediatePropagation();
      void openCashDrawer();
    };
    window.addEventListener("keydown", handleCashDrawerShortcut, true);
    return () => window.removeEventListener("keydown", handleCashDrawerShortcut, true);
  }, [
    busy,
    canOpenCashDrawer,
    cashMovementDirection,
    closurePreview,
    confirmation,
    customerSearchOpen,
    denominationCalculatorOpen,
    discountOpen,
    documentTypeOpen,
    invoiceSearchOpen,
    openCashDrawer,
    paymentOpen,
    printerOpen,
    productSearchOpen,
    returnsOpen,
    temporaryOpen,
    workstation.workSessionId,
  ]);

  useEffect(() => {
    const openSynchronizationEvents = (event: KeyboardEvent) => {
      if (!event.ctrlKey || event.altKey || event.shiftKey || event.key.toLowerCase() !== "l") return;
      if (!client) return;
      event.preventDefault();
      event.stopPropagation();
      setSynchronizationEventsOpen(true);
    };
    window.addEventListener("keydown", openSynchronizationEvents, true);
    return () => window.removeEventListener("keydown", openSynchronizationEvents, true);
  }, [client]);

  useEffect(() => {
    const openDenominationCalculator = (event: KeyboardEvent) => {
      if (!isPosDenominationCalculatorShortcut(event) || !client) return;
      const canOpen =
        !busy &&
        !temporaryOpen &&
        !paymentOpen &&
        !productSearchOpen &&
        !customerSearchOpen &&
        !discountOpen &&
        !printerOpen &&
        !denominationCalculatorOpen &&
        (!closurePreview || !closureAttempt) &&
        !confirmation &&
        !invoiceSearchOpen &&
        !returnsOpen &&
        !documentTypeOpen &&
        !cashMovementDirection;
      if (!canOpen) return;
      event.preventDefault();
      event.stopImmediatePropagation();
      setDenominationCalculatorOpen(true);
    };
    window.addEventListener("keydown", openDenominationCalculator, true);
    return () => window.removeEventListener("keydown", openDenominationCalculator, true);
  }, [
    busy,
    cashMovementDirection,
    client,
    closureAttempt,
    closurePreview,
    confirmation,
    customerSearchOpen,
    denominationCalculatorOpen,
    discountOpen,
    documentTypeOpen,
    invoiceSearchOpen,
    paymentOpen,
    printerOpen,
    productSearchOpen,
    returnsOpen,
    temporaryOpen,
  ]);

  async function capture(event: FormEvent) {
    event.preventDefault();
    if (captureInFlight.current) return;
    captureInFlight.current = true;
    const value = scan.trim();
    if (value) setScan("");
    pendingShortageCapture.current = null;
    setQuantityShortage(null);
    try {
      const parsed = parsePosBarcodeCapture(value);
      if (!parsed.valid) {
        setError(parsed.message);
        setMessage("Revisa la captura");
        rejectScan(value);
        focusScanner();
        return;
      }
      await captureValue(
        parsed.code,
        value.includes("*") ? parsed.quantity : undefined,
      );
    } finally {
      captureInFlight.current = false;
    }
  }

  function beginGenericProductPricing(line: PosDraft["lines"][number] | undefined) {
    if (!shouldOpenGenericProductPricing(line)) return false;
    setGenericProductLine(line ?? null);
    setMessage("Define el precio del producto genérico");
    setScan("");
    return true;
  }

  async function captureValue(value: string, requestedQuantity?: number): Promise<boolean> {
    if (!client || !value || busy) return false;
    if (!salesReady) {
      setError(
        client.mode === "online"
          ? "No hay conexi\u00f3n con Auraly. La venta en l\u00ednea requiere conexi\u00f3n con el servidor."
          : "Los servicios locales del equipo no est\u00e1n disponibles. El producto no fue agregado.",
      );
      setMessage(client.mode === "online" ? "Esperando conexi\u00f3n con Auraly" : "Esperando servicios del equipo");
      focusScanner();
      return false;
    }
    setBusy(true);
    setError(null);
    let quantityToFocus: string | null = null;
    let restoreScannerFocus = true;
    try {
      const startsNewSale = !draft?.lines.length;
      const result = await client.capture(
        value,
        draft?.customerId ?? null,
        requestedQuantity,
      );
      if (result.status === "Added" && result.draft) {
        clearScanRejection();
        const capturedLine = capturedLineAfterAddition(draft?.lines ?? [], result.draft.lines);
        const confirmedDraft = result.draft;
        setDraft(confirmedDraft);
        quantityToFocus = capturedLine?.lineId ?? null;
        setSelectedLineId(quantityToFocus);
        revealLine(quantityToFocus);
        if (beginGenericProductPricing(capturedLine)) {
          restoreScannerFocus = false;
          if (startsNewSale) setLastSettlement(null);
          return true;
        }
        setMessage(`${capturedLine?.description ?? "Producto"} agregado · cantidad ${(capturedLine?.quantity ?? requestedQuantity ?? 1).toLocaleString("es-CO")}`);
        if (startsNewSale) setLastSettlement(null);
        setScan("");
        return true;
      }
      if (result.status === "NotFound") {
        setScan("");
        rejectScan(value);
        setMessage("Producto no encontrado");
      } else {
        const failure = describeCaptureFailure(result);
        if (failure) {
          setError(null);
          setMessage(failure.message);
          if (result.status === "InsufficientInventory" && result.availability) {
            pendingShortageCapture.current = { kind: "code", code: value };
            setQuantityShortage({
              lineId: null,
              productName: result.capturedProduct?.product.name ?? value,
              requestedQuantity: result.availability.requestedQuantity,
              availableQuantity: result.availability.availableQuantity,
              maximumLineQuantity: Math.max(0, result.maximumQuantity ?? result.availability.availableQuantity),
              allowsFractionalSale: result.capturedProduct?.product.allowsFractionalSale ?? false,
              managesInventory: true,
            });
          }
        }
      }
      return false;
    } catch (caught) {
      showError(caught);
      return false;
    } finally {
      setBusy(false);
      if (restoreScannerFocus) focusScanner();
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
      if (result.status === "Added") {
        if (result.draft) setDraft(result.draft);
        if (inventoryResolution && result.draft)
          await validateRecoveredInventory(result.draft.draftId.value);
        setMessage("Cantidad actualizada");
      } else {
        const confirmed = draft.lines.find((line) => line.lineId === lineId);
        if (confirmed)
          setQuantityDrafts((current) => ({
            ...current, [lineId]: String(confirmed.quantity),
          }));
        const failure = describeCaptureFailure(result);
        if (failure) {
          setError(null);
          setMessage(failure.message);
          if (result.status === "InsufficientInventory" && result.availability && confirmed) {
            const otherQuantity = draft.lines
              .filter((line) => line.productId.value === confirmed.productId.value && line.lineId !== lineId)
              .reduce((total, line) => total + line.quantity, 0);
            setQuantityShortage({
              lineId,
              productName: confirmed.description,
              requestedQuantity: quantity,
              availableQuantity: result.availability.availableQuantity,
              maximumLineQuantity: Math.max(
                0,
                result.maximumQuantity ?? result.availability.availableQuantity - otherQuantity,
              ),
              allowsFractionalSale: confirmed.allowsFractionalSale,
              managesInventory: true,
            });
          }
        }
      }
    } catch (caught) {
      showError(caught);
      const confirmed = draft.lines.find((line) => line.lineId === lineId);
      if (confirmed)
        setQuantityDrafts((current) => ({
          ...current, [lineId]: String(confirmed.quantity),
        }));
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
      setQuantityDrafts((current) => ({
        ...current, [lineId]: String(currentLine.quantity),
      }));
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

  function focusLastQuantity() {
    const lastLineId = draft?.lines.at(-1)?.lineId;
    if (!lastLineId) return;
    window.requestAnimationFrame(() => {
      quantityInputs.current.get(lastLineId)?.focus();
      quantityInputs.current.get(lastLineId)?.select();
    });
  }

  async function requestSensitiveApproval(
    permissionResource: string,
    lineId: string | null,
    context: Record<string, unknown>,
    operationId: string,
    execute: (authorization: PosSensitiveAuthorization) => Promise<void>,
  ) {
    setSensitiveApprovalError(null);
    setSensitiveApproval({
      approval: null,
      operationId,
      permissionResource,
      lineId,
      context,
      execute,
    });
    const activeClient = client;
    if (serverConnected && activeClient) {
      const businessId = workstation.businessId;
      if (!businessId || !draft)
        throw new Error("No fue posible identificar el negocio de esta venta.");
      try {
        const approval = await activeClient.createApproval({
          businessId,
          deviceId: workstation.deviceId,
          workSessionId: workstation.workSessionId,
          draftId: draft.draftId.value,
          lineId,
          permissionResource,
          contextJson: JSON.stringify(context),
        });
        setSensitiveApproval((current) => current?.operationId === operationId
          ? { ...current, approval }
          : current);
      } catch (caught) {
        // The API resolves current permissions from the authoritative store. The
        // browser token can lag behind a role/seed update until its next renewal.
        if (!approvalRequestConfirmsExistingPermission(caught)) {
          setSensitiveApprovalError(caught instanceof Error
            ? caught.message
            : "No fue posible enviar la solicitud remota. Puedes autorizar con la clave de un usuario que tenga el permiso o reintentar.");
          return;
        }
        setSensitiveApproval((current) => current?.operationId === operationId ? null : current);
        await execute({ operationId });
        return;
      }
    }
  }

  async function authorizeSensitiveEntry(
    permissionResource: string,
    lineId: string | null,
    context: Record<string, unknown>,
    onAuthorized: (authorization: PosSensitiveAuthorization) => Promise<void>,
  ) {
    const operationId = crypto.randomUUID();
    const activePermissions = client?.mode === "edge" ? edgePermissions : permissions;
    if (activePermissions.includes(permissionResource)) {
      await onAuthorized({ operationId });
      return;
    }
    await requestSensitiveApproval(
      permissionResource, lineId, context, operationId, onAuthorized,
    );
  }

  async function authorizeSensitiveConfirmation(
    permissionResource: string,
    lineId: string | null,
    context: Record<string, unknown>,
    authorization: PosSensitiveAuthorization | null,
    onAuthorized: (authorization: PosSensitiveAuthorization) => Promise<void>,
  ) {
    if (authorizationIsCurrent(authorization)) {
      await onAuthorized(authorization!);
      return;
    }
    await authorizeSensitiveEntry(
      permissionResource,
      lineId,
      context,
      onAuthorized,
    );
  }

  async function openDiscount() {
    if (!draft?.lines.length || busy) return;
    setDiscountOpen(true);
  }

  async function closeSalesSession() {
    if (!client || busy) return;
    if (!workstation.workSessionId) {
      setError("No hay una sesión de venta abierta para cerrar.");
      return;
    }
    setBusy(true);
    setError(null);
    try {
      const activeDraft = draft ?? await client.activeDraft();
      if (!draft) setDraft(activeDraft);
      const closePermission = temporaries.length > 0
        ? "work-sessions.close-with-paused-sales"
        : "work-sessions.close";
      await authorizeSensitiveEntry(
        closePermission,
        null,
        {
          action: "OpenWorkSessionClosure",
          workSessionId: workstation.workSessionId,
          businessName: workstation.businessName,
          warehouseName: workstation.warehouseName,
        },
        async (authorization) => {
          const preview = await client.previewWorkSessionClosure(
            activeDraft.draftId.value,
            authorization,
          );
          closureOperationId.current = authorization.operationId ?? crypto.randomUUID();
          closureAuthorization.current = authorization;
          setClosurePreview(preview);
        },
      );
    } catch (caught) {
      const detail = caught instanceof Error
        ? caught.message
        : "No fue posible preparar el cierre de la sesión de venta.";
      setError(detail);
      setMessage("No se pudo iniciar el cierre de sesión");
    } finally {
      setBusy(false);
    }
  }

  async function confirmSalesSessionClosure(paymentCounts: PosWorkSessionPaymentCount[], note: string | null) {
    if (!client || !closurePreview || busy) return;
    setBusy(true);
    setError(null);
    try {
      const countedCash = paymentCounts.find(value => value.paymentMethodCode === "Cash")?.countedAmount ?? 0;
      const submitted = closureAttempt ?? { countedCash, paymentCounts, note };
      if (!closureAttempt) setClosureAttempt(submitted);
      const finish = async (authorization: PosSensitiveAuthorization) => {
        const operationId = authorization.operationId ?? closureOperationId.current ?? crypto.randomUUID();
        closureOperationId.current = operationId;
        await client.closeWorkSession({
          operationId,
          authorizationToken: closurePreview.authorizationToken,
          draftId: draft?.draftId.value ?? "",
          authorization,
          countedCash: submitted.countedCash,
          paymentCounts: submitted.paymentCounts,
          note: submitted.note,
        });
        if (draft?.sourceOrderId && client instanceof OnlinePosClient)
          await client.releaseRecoveredOrder(draft.sourceOrderId).catch(() => undefined);
        setClosurePreview(null);
        setClosureAttempt(null);
        closureOperationId.current = null;
        closureAuthorization.current = null;
        if (client instanceof PosEdgeClient) {
          await logoutLocal(true);
          return;
        }
        forgetSalesWorkspace();
        router.push("/dashboard");
      };

      const closePermission = temporaries.length > 0
        ? "work-sessions.close-with-paused-sales"
        : "work-sessions.close";
      await authorizeSensitiveConfirmation(
        closePermission,
        null,
        {
          action: "ConfirmWorkSessionClosure",
          workSessionId: workstation.workSessionId,
          businessName: workstation.businessName,
          warehouseName: workstation.warehouseName,
        },
        closureAuthorization.current,
        finish,
      );
    } catch (caught) {
      const detail = caught instanceof Error
        ? caught.message
        : "No fue posible cerrar e imprimir la sesión de venta.";
      setError(detail);
      setMessage("El cierre no se completó; corrige la novedad y reintenta");
    } finally {
      setBusy(false);
    }
  }

  async function completeRemoteApproval(approvalRequestId: string) {
    if (!sensitiveApproval) return;
    const pending = sensitiveApproval;
    setSensitiveApproval(null);
    setBusy(true);
    setSensitiveApprovalError(null);
    try {
      await pending.execute({
        operationId: pending.operationId,
        approvalRequestId,
        expiresAt: pending.approval?.expiresAt,
      });
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "No fue posible aplicar la acción autorizada.");
      setMessage("La autorización fue aprobada, pero la acción no pudo completarse");
    } finally {
      setBusy(false);
    }
  }

  async function retrySensitiveApproval() {
    if (!sensitiveApproval || busy) return;
    const pending = sensitiveApproval;
    setBusy(true);
    setSensitiveApprovalError(null);
    try {
      await requestSensitiveApproval(
        pending.permissionResource,
        pending.lineId,
        pending.context,
        pending.operationId,
        pending.execute,
      );
    } catch (caught) {
      setSensitiveApprovalError(caught instanceof Error
        ? caught.message
        : "No fue posible reenviar la solicitud de autorización.");
    } finally {
      setBusy(false);
    }
  }

  async function completeLocalApproval(secret: string) {
    if (!sensitiveApproval) return;
    const pending = sensitiveApproval;
    let approvedOnline = false;
    setBusy(true);
    setSensitiveApprovalError(null);
    try {
      if (client?.mode === "online") {
        const approval = pending.approval;
        if (!approval) throw new Error("La solicitud de aprobación no está disponible.");
        await posApprovalClient.authorizeLocally(approval.approvalRequestId, secret);
        approvedOnline = true;
        setSensitiveApproval(null);
        await pending.execute({
          operationId: pending.operationId,
          approvalRequestId: approval.approvalRequestId,
          expiresAt: approval.expiresAt,
        });
      } else {
        await pending.execute({
          operationId: pending.operationId,
          supervisorSecret: secret,
          expiresAt: new Date(Date.now() + 2 * 60 * 1000).toISOString(),
        });
        setSensitiveApproval(null);
      }
    } catch (caught) {
      const detail = caught instanceof Error ? caught.message : "La credencial no pudo autorizar la acción.";
      if (approvedOnline) {
        setError(detail);
        setMessage("La autorización fue aprobada, pero la acción no pudo completarse");
      } else {
        setSensitiveApprovalError(detail);
      }
    } finally {
      setBusy(false);
    }
  }

  async function removeLine(lineId: string) {
    if (!client || !draft) return;
    setBusy(true);
    try {
      const line = draft.lines.find((candidate) => candidate.lineId === lineId);
      await authorizeSensitiveConfirmation(
        "sales.lines.remove",
        lineId,
        { action: "ConfirmRemoveLine", product: line?.description ?? "Producto" },
        lineRemovalAuthorization.current,
        async (authorization) => {
          const updated = await client.removeLine(draft.draftId.value, lineId, authorization);
          lineRemovalAuthorization.current = null;
          setConfirmation(null);
          setDraft(updated);
          if (inventoryResolution)
            await validateRecoveredInventory(updated.draftId.value);
          setSelectedLineId(updated.lines.at(-1)?.lineId ?? null);
          setMessage("Producto retirado");
        },
      );
    } catch (caught) {
      showError(caught);
    } finally {
      setBusy(false);
      focusScanner();
    }
  }

  async function cancelSale() {
    if (!client || !draft?.lines.length || busy) return;
    setBusy(true);
    setError(null);
    try {
      await authorizeSensitiveConfirmation(
        "sales.drafts.restart",
        null,
        { action: "ConfirmRestartSale", lineCount: draft.lines.length, total: draft.payableAmount },
        restartAuthorization.current,
        async (authorization) => {
          const next = await client.cancelDraft(draft.draftId.value, authorization);
          restartAuthorization.current = null;
          setConfirmation(null);
          setDraft(next);
          setSelectedLineId(null);
          setSelectedCustomer(null);
          setScan("");
          setMessage(draft.sourceOrderId
            ? "Pedido eliminado y venta reiniciada. Nueva venta lista."
            : "Venta reiniciada. Nueva venta lista.");
          if (draft.sourceOrderId && edgeClientBeforeOnlineOrder.current) {
            setClient(edgeClientBeforeOnlineOrder.current);
            edgeClientBeforeOnlineOrder.current = null;
          }
        },
      );
    } catch (caught) {
      showError(caught);
    } finally {
      setBusy(false);
      focusScanner();
    }
  }

  async function applyLineEdits(lines: PosDraftLineUpdate[], includesProratedDiscount: boolean) {
    if (!client || !draft || busy) return;
    setBusy(true);
    setError(null);
    try {
      setDraft(await client.updateLines(draft.draftId.value, lines, includesProratedDiscount));
      setDiscountOpen(false);
      setMessage("Cambios aplicados solamente a esta venta");
    } catch (caught) {
      showError(caught);
    } finally {
      setBusy(false);
      focusScanner();
    }
  }

  async function confirmGenericProduct(publicUnitPrice: number, documentUnitCost: number) {
    if (!client || !draft || !genericProductLine || busy) return;
    setBusy(true);
    setError(null);
    try {
      const updated = await client.updateLines(
        draft.draftId.value,
        draft.lines.map(line => ({
          lineId: line.lineId,
          description: line.description,
          publicUnitPrice: line.lineId === genericProductLine.lineId ? publicUnitPrice : line.publicUnitPrice,
          discount: line.lineId === genericProductLine.lineId ? 0 : line.discount,
          documentUnitCost: line.lineId === genericProductLine.lineId ? documentUnitCost : line.documentUnitCost,
        })),
        false,
      );
      setDraft(updated);
      setGenericProductLine(null);
      setMessage(`${genericProductLine.description} agregado`);
      window.setTimeout(focusScanner, 0);
    } catch (caught) {
      showError(caught);
    } finally {
      setBusy(false);
    }
  }

  async function cancelGenericProduct() {
    if (!client || !draft || !genericProductLine || busy) return;
    setBusy(true);
    try {
      const updated = await client.discardUnpricedGenericLine(draft.draftId.value, genericProductLine.lineId);
      setDraft(updated);
      setGenericProductLine(null);
      setMessage("Producto genérico cancelado");
      window.setTimeout(focusScanner, 0);
    } catch (caught) {
      showError(caught);
    } finally {
      setBusy(false);
    }
  }

  async function selectCustomer(customer: PosCustomer | null) {
    if (!client || !draft || busy) return;
    setBusy(true);
    setPricingTransition(true);
    setError(null);
    try {
      const selection = await client.selectCustomer(
        draft.draftId.value,
        customer?.customerId ?? null,
        customer?.partySiteId ?? null,
      );
      setDraft(selection.draft);
      setSelectedCustomer(selection.customer);
      setCustomerSearchOpen(false);
      const continueSavingOrder = shouldSaveOrderAfterCustomerSelection({
        pendingOrderSave: saveOrderAfterCustomerSelection.current,
        selectedCustomerId: selection.customer?.customerId,
      });
      saveOrderAfterCustomerSelection.current = false;
      if (continueSavingOrder) {
        setPricingTransition(false);
        setMessage("Guardando pedido e inventario reservado…");
        const wasRecovered = Boolean(selection.draft.sourceOrderId);
        const saved = await saveDraftAsOnlineOrder(selection.draft);
        setDraft(saved.nextDraft);
        setSelectedCustomer(null);
        setSelectedLineId(null);
        setScan("");
        setLastSettlement(null);
        setSidePanel("orders");
        setOrdersRefreshVersion((current) => current + 1);
        setMessage(`${saved.order.orderNumber} ${wasRecovered ? "actualizado" : "guardado"}; inventario reservado en Pedidos`);
        if (wasRecovered && edgeClientBeforeOnlineOrder.current) {
          setClient(edgeClientBeforeOnlineOrder.current);
          edgeClientBeforeOnlineOrder.current = null;
        }
        return;
      }
      setMessage(
        selection.customer
          ? `${selection.customer.name} seleccionado; precios recalculados`
          : "Consumidor final seleccionado; precios del negocio aplicados",
      );
    } catch (caught) {
      showError(caught);
    } finally {
      setBusy(false);
      setPricingTransition(false);
      focusScanner();
    }
  }

  async function reprintSale(sale: PosIssuedSaleSummary) {
    if (!client || busy) return;
    setError(null);
    try {
      const receipt = await loadServerIssuedSaleReceipt(
        salesHistoryScope,
        sale.documentId.value,
      );
      await client.printHistoricalReceipt(receipt);
      setMessage(`${sale.documentNumber} reimpresa desde su snapshot original`);
    } catch (caught) {
      showError(caught);
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
      lineRemovalAuthorization.current = null;
    } else if (confirmation.kind === "temporary") {
      await deleteTemporary(confirmation.draftId);
    } else if (confirmation.kind === "order-save") {
      await saveOrder();
    } else {
      await cancelSale();
      restartAuthorization.current = null;
    }
    setConfirmation(null);
  }

  const loadSensitiveApproval = useCallback(async (approvalRequestId: string) => {
    if (!client) throw new Error("La caja no está disponible para consultar la autorización.");
    return client.approval(approvalRequestId) as Promise<PosApprovalRequest>;
  }, [client]);

  const subscribeSensitiveApprovals = useCallback(async (onChanged: () => void) => {
    if (client instanceof PosEdgeClient)
      return client.watchLocalState(onChanged);
    return posApprovalClient.subscribe(onChanged);
  }, [client]);

  async function saveTemporary(event: FormEvent) {
    event.preventDefault();
    await persistTemporary(temporaryName, temporaryReference);
  }

  async function recoverTemporary(id: string) {
    if (!client) return;
    setBusy(true);
    let requiresResolution = false;
    try {
      const recovered = await client.recoverTemporary(id);
      setDraft(recovered);
      requiresResolution = !(await validateRecoveredInventory(recovered.draftId.value));
      await refreshTemporaries();
      setMessage(requiresResolution ? "Venta recuperada: corrige el inventario" : "Venta en espera recuperada");
    } catch (caught) {
      showError(caught);
    } finally {
      setBusy(false);
      if (!requiresResolution) focusScanner();
    }
  }

  async function validateRecoveredInventory(draftId: string) {
    if (!client) return false;
    const validation = await client.validateDraftInventory(draftId);
    setInventoryResolution(validation.isValid ? null : validation);
    return validation.isValid;
  }

  async function requestDeleteTemporary(id: string, name: string) {
    if (busy) return;
    try {
      await authorizeSensitiveEntry(
        "sales.drafts.paused.delete",
        null,
        { action: "OpenDeletePausedSale", draftId: id, name },
        async (authorization) => {
          temporaryRemovalAuthorization.current = authorization;
          setConfirmation({ kind: "temporary", draftId: id, name });
        },
      );
    } catch (caught) {
      showError(caught);
    }
  }

  async function deleteTemporary(id: string) {
    if (!client || busy) return;
    setBusy(true);
    setError(null);
    try {
      await client.deleteTemporary(id, temporaryRemovalAuthorization.current ?? undefined);
      await refreshTemporaries();
      temporaryRemovalAuthorization.current = null;
      setMessage("Venta en espera eliminada");
    } catch (caught) {
      showError(caught);
    } finally {
      setBusy(false);
      focusScanner();
    }
  }

  async function openPayment() {
    if (!client || !draft?.lines.length || busy) return;
    if (inventoryResolution) {
      setError("Resuelve primero los productos con inventario insuficiente.");
      return;
    }
    setError(null);
    setBusy(true);
    try {
      const settlement = await client.previewSettlement(draft.draftId.value);
      setSaleSettlement(settlement);
      setPaymentOpen(true);
    } catch (caught) {
      showError(caught);
    } finally {
      setBusy(false);
    }
  }
  async function changeDocumentType(value: PosSaleDocumentType) {
    if (!client || busy) return;
    if (!canIssuePosDocument(value, workstation.fiscalReady, workstation.dianQuotaAvailable !== false)) {
      setDocumentTypeOpen(false);
      setError(workstation.fiscalReady && workstation.dianQuotaAvailable === false
        ? dianQuotaExhaustedMessage : fiscalConfigurationRequiredMessage);
      if (paymentOpen) setPaymentFocusRequest((current) => current + 1);
      else focusScanner();
      return;
    }
    if (value === documentType) {
      setDocumentTypeOpen(false);
      if (paymentOpen) setPaymentFocusRequest((current) => current + 1);
      else focusScanner();
      return;
    }

    setBusy(true);
    setError(null);
    try {
      const numbers = await client.nextNumbers(value);
      setDocumentType(value);
      setNextNumber(numbers?.document ?? null);
      setDocumentTypeOpen(false);
      setMessage(
        value === "SalesInvoice"
          ? "Factura electrónica seleccionada"
          : "Comprobante de venta seleccionado",
      );
    } catch (caught) {
      showError(caught);
    } finally {
      setBusy(false);
      if (paymentOpen) setPaymentFocusRequest((current) => current + 1);
      else focusScanner();
    }
  }


  async function completeSale(
    payments: PosPaymentInput[],
    settlement: PosPaymentSettlement,
    authorization: PosSensitiveAuthorization | null = null,
  ) {
    if (!client || !draft || (busy && !authorizationIsCurrent(authorization))) return;
    const effectiveDocumentType = selectedCustomer?.requiresElectronicInvoice
      ? "SalesInvoice"
      : documentType;
    if (!canIssuePosDocument(effectiveDocumentType, workstation.fiscalReady,
      workstation.dianQuotaAvailable !== false)) {
      setError(workstation.fiscalReady && workstation.dianQuotaAvailable === false
        ? dianQuotaExhaustedMessage : fiscalConfigurationRequiredMessage);
      return;
    }
    if (saleRequiresBelowCostAuthorization(draft.lines) &&
        !authorizationIsCurrent(authorization)) {
      try {
        await authorizeSensitiveConfirmation(
          "sales.below-cost",
          null,
          {
            action: "CompleteBelowCostSale",
            lineCount: draft.lines.length,
            total: draft.payableAmount,
            products: draft.lines.map((line) => line.description),
          },
          null,
          async (approved) => completeSale(payments, settlement, approved),
        );
      } catch (caught) {
        showError(caught);
      }
      return;
    }
    const localPrintPreview = client.mode === "edge"
      ? openHalfLetterPrintPreview()
      : null;
    setBusy(true);
    setError(null);
    try {
      const checkout = splitCreditCheckout(payments, selectedCustomer);
      const result = await client.completeSale(
        draft.draftId.value,
        selectedCustomer?.identification ?? null,
        checkout.payments,
        effectiveDocumentType,
        checkout.credit,
        authorization ?? undefined,
      );
      if (client.mode === "edge" && (result.printedDirectly || result.printCompletion))
        closePrintPreview(localPrintPreview);
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
      setSaleSettlement(null);
      if (draft.sourceOrderId && edgeClientBeforeOnlineOrder.current) {
        setClient(edgeClientBeforeOnlineOrder.current);
        edgeClientBeforeOnlineOrder.current = null;
      }

      const printAfterCompletedSale = async () => {
        try {
          if (result.printCompletion) {
            await result.printCompletion;
          } else if (
            client.mode === "edge" &&
            result.printedDirectly === false &&
            result.receipt
          ) {
            await renderReceiptsReceipt(
              localPrintPreview,
              [result.receipt],
              {
                businessId: workstation.businessId,
                businessName: workstation.businessName,
                warehouseId: workstation.warehouseId,
                warehouseName: workstation.warehouseName,
                workSessionId: workstation.workSessionId ?? "",
              },
            );
          }
        } catch (printFailure) {
          closePrintPreview(localPrintPreview);
          const detail = printFailure instanceof Error
            ? printFailure.message
            : "No fue posible imprimir la venta.";
          setError(`La venta quedó registrada. ${detail}`);
        }
      };
      window.setTimeout(() => void printAfterCompletedSale(), 0);

      const issuedLabel = result.printedDirectly
        ? "emitida e impresa directamente"
        : "emitida; impresión enviada";
      setMessage(
        settlement.change > 0
          ? `${result.issuedSale.documentNumber} ${issuedLabel}. Entregar ${money.format(settlement.change)} de cambio. Nueva venta lista.`
          : effectiveDocumentType === "SalesInvoice"
            ? `${result.issuedSale.documentNumber} ${issuedLabel} (DIAN ${result.issuedSale.fiscalNumber}). Pago registrado. Nueva venta lista.`
            : `${result.issuedSale.documentNumber} ${issuedLabel}. Pago registrado. Nueva venta lista.`,
      );
    } catch (caught) {
      closePrintPreview(localPrintPreview);
      showError(caught);
      if (caught instanceof Error && caught.message.toLocaleLowerCase("es-CO").includes("inventario")) {
        setPaymentOpen(false);
        await validateRecoveredInventory(draft.draftId.value).catch(() => undefined);
      }
    } finally {
      setBusy(false);
      focusScanner();
    }
  }

  const searchProducts = useCallback(
    (term: string, skip: number) =>
      client?.searchProducts(
        term,
        skip,
        50,
        priceVerifierMode ? null : selectedCustomer?.customerId ?? null,
        priceVerifierMode,
      ) ??
      Promise.resolve({
        items: [],
        hasMore: false,
        nextOffset: null,
      }),
    [client, priceVerifierMode, selectedCustomer?.customerId],
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

  const loadProductAvailability = useCallback(
    (productId: string, signal?: AbortSignal) =>
      client?.productWarehouseAvailability(productId, signal) ?? Promise.resolve([]),
    [client],
  );

  const customerCountries = useCallback(
    () => client?.customerCountries() ?? Promise.resolve([]),
    [client],
  );

  const customerDivisions = useCallback(
    (countryId: string) => client?.customerDivisions(countryId) ?? Promise.resolve([]),
    [client],
  );

  const customerCities = useCallback(
    (divisionId: string) => client?.customerCities(divisionId) ?? Promise.resolve([]),
    [client],
  );

  const createCustomer = useCallback(
    (input: PosCreateCustomerInput) => {
      if (!client) return Promise.reject(new Error("El punto de venta no está disponible."));
      return client.createCustomer(input);
    },
    [client],
  );
  const salesHistoryScope = useMemo(
    () => ({
      businessId: workstation.businessId,
      warehouseId: workstation.warehouseId,
      workSessionId: workstation.workSessionId ?? "",
    }),
    [workstation.businessId, workstation.warehouseId, workstation.workSessionId],
  );
  const searchIssuedSales = useCallback(
    (filters: import("@/services/pos/pos-edge-client").PosIssuedSaleFilters, skip: number) =>
      searchServerIssuedSales(salesHistoryScope, filters, skip, 20),
    [salesHistoryScope],
  );
  const searchHistoryCustomers = useCallback(
    (search: string, skip: number) => searchServerHistoryCustomers(salesHistoryScope, search, skip, 10),
    [salesHistoryScope],
  );
  const searchHistoryProducts = useCallback(
    (search: string, skip: number) => searchServerHistoryProducts(salesHistoryScope, search, skip, 10),
    [salesHistoryScope],
  );
  const loadIssuedSaleDetail = useCallback(
    (sale: PosIssuedSaleSummary) => loadServerIssuedSaleReceipt(salesHistoryScope, sale.documentId.value),
    [salesHistoryScope],
  );

  async function selectSearchProduct(product: PosCatalogProduct) {
    if (priceVerifierMode) {
      setMessage(`${product.name}: ${new Intl.NumberFormat("es-CO", {style:"currency",currency:"COP",maximumFractionDigits:0}).format(product.unitPrice)}${(product.promotionDiscount ?? 0) > 0 ? ` · promoción ${new Intl.NumberFormat("es-CO", {style:"currency",currency:"COP",maximumFractionDigits:0}).format(product.promotionDiscount ?? 0)}` : ""}`);
      focusProductSearch();
      return false;
    }
    const added = await captureSelectedProduct(product);
    if (added) setProductSearchOpen(false);
    return added;
  }

  async function synchronizeNow() {
    if (!client || client.mode !== "edge") return;
    setSynchronization((current) => ({ ...current, inProgress: true, failed: false, error: null }));
    try {
      await client.synchronizeNow();
      setMessage("Auraly está subiendo los pendientes y descargando los cambios de esta estación.");
    } catch (caught) {
      const publicMessage = posPublicError(
        caught instanceof Error ? caught.message : null,
        "No fue posible iniciar la actualización.",
      ) ?? "No fue posible iniciar la actualización.";
      setSynchronization((current) => ({
        ...current,
        inProgress: false,
        failed: true,
        error: publicMessage,
      }));
      if (edgeLoginState === "preparing" || preparationActive) {
        setEdgeLoginError(publicMessage);
      } else {
        setMessage("No fue posible iniciar la actualización. Puedes seguir facturando con los datos locales.");
      }
    }
  }

  async function retryPreparation() {
    setSetupError(null);
    setEdgeLoginError(null);
    if (client instanceof PosEdgeClient) {
      await synchronizeNow();
      return;
    }
    if (preparationSelection) {
      await prepareInstalledPos(
        preparationSelection.option,
        preparationSelection.documentType,
      ).catch(() => undefined);
    }
  }

  const synchronizationTitle = synchronization.inProgress
    ? synchronization.pendingCount > 0
      ? `Subiendo ${synchronization.pendingCount} documento${synchronization.pendingCount === 1 ? "" : "s"} pendiente${synchronization.pendingCount === 1 ? "" : "s"}`
      : "Subiendo pendientes y descargando cambios"
    : synchronization.pendingCount > 0
      ? `${synchronization.pendingCount} documento${synchronization.pendingCount === 1 ? "" : "s"} pendiente${synchronization.pendingCount === 1 ? "" : "s"} por subir${!serverConnected ? ". API desconectada." : !pushConnected ? ". Señal interrumpida; la sincronización manual sigue disponible." : "."}`
    : !serverConnected
      ? `Sin conexión para actualizar datos. ${synchronization.error ?? "Haz clic para revisar."}`
    : !pushConnected
      ? "La API está conectada, pero la señal en tiempo real está interrumpida. Puedes sincronizar manualmente."
    : synchronization.failed
      ? synchronization.pendingCount > 0
        ? `${synchronization.pendingCount} documento${synchronization.pendingCount === 1 ? "" : "s"} sin sincronizar. ${synchronization.error ?? "Haz clic para reintentar."}`
        : `La última actualización tuvo un problema. ${synchronization.error ?? "Haz clic para reintentar."}`
      : synchronization.lastAt
        ? `Última sincronización: ${new Date(synchronization.lastAt).toLocaleString("es-CO")}`
        : "Sincronización local pendiente";

  async function captureSelectedProduct(
    product: PosCatalogProduct,
    requestedQuantity?: number,
  ): Promise<boolean> {
    if (!client || busy) return false;
    setBusy(true);
    setError(null);
    let quantityToFocus: string | null = null;
    let restoreScannerFocus = true;
    try {
      let scaleWeight: number | null = null;
      let manualWeight = false;
      if (product.isWeighable && requestedQuantity === undefined) {
        try {
          scaleWeight = (await client.readScaleWeight()).weight;
        } catch (caught) {
          manualWeight = true;
          setError(caught instanceof Error
            ? `${caught.message} Ingresa la cantidad decimal manualmente.`
            : "La balanza no respondió. Ingresa la cantidad decimal manualmente.");
        }
      }
      const startsNewSale = !draft?.lines.length;
      const linesBeforeCapture = draft?.lines ?? [];
      const explicitQuantity = requestedQuantity ?? scaleWeight ?? undefined;
      const result = await client.captureSelectedProduct(
        product,
        draft?.customerId ?? null,
        explicitQuantity,
      );
      if (result.status !== "Added" || !result.draft) {
        const failure = describeCaptureFailure(result);
        if (failure) {
          setError(null);
          setMessage(failure.message);
          if (result.status === "InsufficientInventory" && result.availability) {
            pendingShortageCapture.current = { kind: "product", product };
            setQuantityShortage({
              lineId: null,
              productName: product.name,
              requestedQuantity: result.availability.requestedQuantity,
              availableQuantity: result.availability.availableQuantity,
              maximumLineQuantity: Math.max(0, result.maximumQuantity ?? result.availability.availableQuantity),
              allowsFractionalSale: product.allowsFractionalSale,
              managesInventory: true,
            });
          }
        }
        return false;
      }
      const confirmedDraft = result.draft;
      const addedLine = capturedLineAfterAddition(linesBeforeCapture, confirmedDraft.lines);
      setDraft(confirmedDraft);
      quantityToFocus = addedLine?.lineId ?? null;
      setSelectedLineId(quantityToFocus);
      revealLine(quantityToFocus);
      if (beginGenericProductPricing(addedLine)) {
        restoreScannerFocus = false;
        if (startsNewSale) setLastSettlement(null);
        return true;
      }
      setMessage(requestedQuantity !== undefined
        ? `${product.name} agregado · cantidad ${requestedQuantity.toLocaleString("es-CO")}`
        : scaleWeight !== null
        ? `${product.name}: ${scaleWeight.toLocaleString("es-CO",{maximumFractionDigits:3})} kg leídos de la balanza`
        : `${product.name} agregado${manualWeight ? "; escribe el peso" : ""}`);
      if (startsNewSale) setLastSettlement(null);
      setScan("");
      return true;
    } catch (caught) {
      showError(caught);
      return false;
    } finally {
      setBusy(false);
      if (restoreScannerFocus) focusScanner();
    }
  }
  function openOrders() {
    setOrdersExpanded(true);
  }

  async function recoverPosOrder(orderId: string) {
    const { orderClient, recovered } = await recoverOrderOnline(orderId);
    const recoveredCustomer = recovered.customerId
      ? await orderClient.customer(recovered.customerId, recovered.customerPartySiteId).catch(() => null)
      : null;
    setDraft(recovered);
    setSelectedCustomer(recoveredCustomer);
    if (recovered.customerId && !recoveredCustomer)
      setError("El pedido se recuperó, pero no fue posible cargar los datos del cliente.");
    setSelectedLineId(recovered.lines[0]?.lineId ?? null);
    setOrdersExpanded(false);
    setSidePanel("orders");
    setOrdersRefreshVersion((current) => current + 1);
    setMessage("Pedido recuperado \u00b7 " + recovered.lines.length + " l\u00edneas");
    focusScanner();
  }

  async function invoicePosOrders(
    orderIds: string[],
    paymentMethodCode: string,
    documentType: "SalesInvoice" | "SalesReceipt",
    idempotencyKey: string,
    onProgress?: (progress: OrderInvoiceSequenceProgress) => void,
    transfer?: { bankAccountId: string | null; reference: string; notes: string | null },
  ) {
    const result = await runOnlineOrderRequest(async () => {
      const orderClient = await getWebOrderClient();
      return orderClient.invoiceOrders(
        orderIds,
        paymentMethodCode,
        documentType,
        transfer?.reference,
        transfer?.bankAccountId,
        transfer?.notes,
        true,
        idempotencyKey,
        onProgress,
        true,
      );
    });
    setMessage(
      (result.printError ? result.printError + " · " : "") +
        result.completedCount +
        " pedido" +
        (result.completedCount === 1 ? "" : "s") +
        " facturado" +
        (result.completedCount === 1 ? "" : "s"),
    );
    return result;
  }

  async function activateOnline(option: SalesWorkspaceOption, initialDocumentType: PosSaleDocumentType) {
    setSetupError(null);
    const requestedWorkspace = salesWorkspaceKey(option.businessId, option.warehouseId);
    const activation = workspaceActivationMode(
      client?.mode ?? null,
      workstation.businessId,
      workstation.warehouseId,
      option.businessId,
      option.warehouseId,
    );
    if (activation === "reenrollment-required") {
      throw new Error(
        "Un equipo enrolado cambia de sede o bodega únicamente mediante un nuevo enrolamiento.",
      );
    }
    if (activation === "keep-edge") {
      setDocumentType(initialDocumentType);
      window.localStorage.setItem("auraly.pos.document-type", initialDocumentType);
      setWorkspaceChanging(false);
      focusScanner();
      return;
    }
    if (
      workspaceChanging &&
      client?.mode === "online" &&
      rememberedSalesWorkspaceKey() === requestedWorkspace
    ) {
      setWorkspaceChanging(false);
      focusScanner();
      return;
    }
      setDocumentType(initialDocumentType);
      window.localStorage.setItem("auraly.pos.document-type", initialDocumentType);
    try {
      window.localStorage.setItem("selected_business_id", option.businessId);
      const context = await selectSalesWorkspace(option, workspaceChanging);
      const onlineClient = new OnlinePosClient(context, onlineUserId, onlineUserName, edgeEnrollmentToken);
      setDraft(null);
      setTemporaries([]);
      setSelectedCustomer(null);
      setSelectedLineId(null);
      setNextNumber(null);
      setLastSettlement(null);
      setScan("");
      const health = await onlineClient.health();
      setWorkstation({
        deviceSeriesCode: "\u2014",
        businessId: context.businessId,
        warehouseId: context.warehouseId,
        businessName: context.businessName,
        warehouseName: context.warehouseName,
        warehouseAllowsNegativeStockSales: context.warehouseAllowsNegativeStockSales,
        userDisplayName: onlineUserName || "—",
        userId: onlineUserId || null,
        workSessionId: null,
        deviceId: null,
        fiscalReady: health.fiscalReady,
        fiscalWarnings: health.fiscalWarnings,
        dianQuotaAvailable: health.dianQuotaAvailable,
      });
      setServerConnected(true);
      setClient(onlineClient);
      setWorkspaceChanging(false);
    } catch (caught) {
      setSetupError(
        caught instanceof Error
          ? caught.message
          : "No fue posible seleccionar la sede y la bodega.",
      );
      throw caught;
    }
  }

  async function prepareInstalledPos(
    option: SalesWorkspaceOption,
    initialDocumentType: PosSaleDocumentType,
    authorization?: PosSensitiveAuthorization,
  ) {
    if (!edgeEnrollmentToken) return;
    preparationActiveRef.current = true;
    setPreparationActive(true);
    setPreparationSelection({ option, documentType: initialDocumentType });
    setSetupError(null);
    setEdgeLoginError(null);
    setSetupNotice("Autorizando y preparando esta caja…");
    window.localStorage.setItem("auraly.pos.document-type", initialDocumentType);
    setSetupLoading(true);
    try {
      const enrollment = await authorizePosEnrollment(
        option,
        draft?.draftId.value,
        authorization,
      );
      setSetupNotice("Guardando la identidad segura de la caja…");
      await redeemPosEnrollment(edgeEnrollmentToken, enrollment);
      setSetupNotice("Reiniciando el servicio local y preparando usuarios, permisos y catálogo…");
      setEdgeLoginState("preparing");
      setClient(null);
      const { client: edgeClient, health } = await connectRedeemedPosEdge(
        clearEdgeUserSession,
        () => waitForRedeemedPosEdge(edgeEnrollmentToken),
        () => new PosEdgeClient(edgeEnrollmentToken),
      );
      setPreparationHealth(health);
      setClient(edgeClient);
      setSetupNotice(null);
    } catch (caught) {
      const message = caught instanceof Error
        ? caught.message
        : "No fue posible enrolar esta estación.";
      setSetupNotice(null);
      setSetupError(posPublicError(
        message,
        "No fue posible terminar la preparación de esta caja.",
      ) ?? "No fue posible terminar la preparación de esta caja.");
      throw caught;
    } finally {
      setSetupLoading(false);
    }
  }

  async function logoutLocal(force = false) {
    if (!(client instanceof PosEdgeClient) || (busy && !force)) return;
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

  async function changeOnlineWorkspace() {
    if (!client || busy) return;
    setSetupError(null);
    setWorkspaceConfigurationOffline(false);
    setWorkspaceChanging(true);
    setSetupLoading(true);
    if (client.mode === "edge") {
      setCanEnrollOffline(false);
      setEnrollmentAvailability(null);
      setOnlineOptions([enrolledWorkspaceOption(workstation)]);
      setSetupLoading(false);
      return;
    }
    try {
      const serverBootstrap = await loadSalesWorkspaceBootstrap();
      applyWorkspaceBootstrap(
        serverBootstrap,
        workstation.userDisplayName || "Usuario",
      );
    } catch (caught) {
      const detail = describeWorkspaceBootstrapError(caught);
      const message = `Sin conexión con Auraly. Puedes revisar la configuración actual, pero no cambiarla ni preparar este equipo. ${detail}`;
      setWorkspaceConfigurationOffline(true);
      setCanEnrollOffline(false);
      setEnrollmentAvailability(null);
      setOnlineOptions(!workstation.businessId || !workstation.warehouseId
        ? []
        : [{
            businessId: workstation.businessId,
            businessName: workstation.businessName,
            warehouseId: workstation.warehouseId,
            warehouseCode: "",
            warehouseName: workstation.warehouseName,
            warehouseAllowsNegativeStockSales: workstation.warehouseAllowsNegativeStockSales,
            hasActiveEdgeEnrollment: false,
          }]);
      setSetupError(message);
    } finally {
      setSetupLoading(false);
    }
  }

  if (preparationActive || (client instanceof PosEdgeClient && edgeLoginState === "preparing")) {
    const preparationError =
      setupError ??
      edgeLoginError ??
      (synchronization.failed && !synchronization.automaticRetryScheduled
        ? synchronization.error
        : null);
    return (
      <PosOnlineSetup
          options={onlineOptions}
          loading={false}
          error={null}
          tenantName={onlineTenantName || workstation.businessName || "Auraly"}
          userDisplayName={onlineUserName || workstation.userDisplayName || "usuario"}
          onSelect={activateOnline}
          edgeCapable={edgeEnrollmentRequired}
          canEnrollOffline={canEnrollOffline}
          enrollmentUnavailableReason={enrollmentAvailability?.reason}
          enrollmentCapacity={enrollmentAvailability}
          onEnroll={prepareInstalledPos}
          enrollmentState={client?.mode === "edge" ? "enrolled" : "available"}
          preparation={{
            health: preparationHealth,
            message: setupNotice,
            error: preparationError,
            retrying: setupLoading || synchronization.inProgress || busy,
            onRetry: preparationError ? () => void retryPreparation() : undefined,
            onBack: () => router.back(),
          }}
        />
    );
  }
  async function logoutOnlineUser() {
    if (busy) return;
    setBusy(true);
    try {
      await logoutCloud();
      router.replace("/login");
    } finally {
      setBusy(false);
    }
  }

  if (!client || workspaceChanging) {
    return (
      <PosOnlineSetup
        options={onlineOptions}
        loading={setupLoading}
        error={setupError}
        notice={setupNotice}
        tenantName={onlineTenantName || "Auraly"}
        userDisplayName={onlineUserName || "usuario"}
        onSelect={activateOnline}
        onCancel={workspaceChanging ? () => {
          setWorkspaceConfigurationOffline(false);
          setWorkspaceChanging(false);
        } : undefined}
        edgeCapable={edgeEnrollmentRequired}
        canEnrollOffline={canEnrollOffline}
        enrollmentUnavailableReason={enrollmentAvailability?.reason}
        enrollmentCapacity={enrollmentAvailability}
        onEnroll={prepareInstalledPos}
        enrollmentState={client?.mode === "edge" ? "enrolled" : edgeEnrollmentRequired ? "available" : "web"}
        configurationOffline={workspaceConfigurationOffline}
        configuredDocumentType={workspaceChanging ? documentType : undefined}
      />
    );
  }

  return (
    <main className="min-h-screen bg-[#eef3f3] text-slate-950 xl:h-screen xl:overflow-hidden">
      <PosDesktopUpdater />
      <header className="flex min-h-14 items-center justify-between gap-4 bg-auraly-background px-5 py-2.5 text-auraly-text shadow-lg">
        <div className="flex items-center gap-2">
          {canOpenAdministrativeMenu && <PosExitMenuButton />}
          <StatusChip
            ok={serverConnected}
            label={serverConnected ? "Conectado con Auraly" : "Modo sin conexión"}
            network
          />
          <InventoryPolicyChip allowsNegativeStock={workstation.warehouseAllowsNegativeStockSales} />
          <div className="flex items-center gap-1" aria-label="Atajos de caja">
            <button
              type="button"
              ref={salesSessionButton}
              onClick={() => void closeSalesSession()}
              disabled={busy}
              title={`Cerrar sesión de venta (${POS_ACTION_SHORTCUTS.closeSession})`}
              aria-keyshortcuts={POS_ACTION_SHORTCUTS.closeSession}
              className="flex h-8 items-center gap-1.5 rounded-full border border-sky-300/20 px-3 text-xs font-semibold text-sky-200 transition hover:bg-sky-300/10 hover:text-white disabled:opacity-40"
            >
              <Banknote className="h-3.5 w-3.5" />
              <span className="hidden md:inline">Cerrar sesión de venta</span>
              <kbd className="hidden xl:inline text-[10px] opacity-70">{POS_ACTION_SHORTCUTS.closeSession}</kbd>
            </button>
            <button
              type="button"
              onClick={() => setCashMovementDirection("In")}
              disabled={busy || !workstation.workSessionId}
              title="Registrar entrada de dinero"
              className="flex h-8 items-center gap-1.5 rounded-full border border-emerald-300/20 px-3 text-xs font-semibold text-emerald-200 transition hover:bg-emerald-300/10 hover:text-white disabled:opacity-40"
            >
              <ArrowDownToLine className="h-3.5 w-3.5" />
              <span className="hidden md:inline">Entrada de dinero</span>
            </button>
            <button
              type="button"
              onClick={() => setCashMovementDirection("Out")}
              disabled={busy || !workstation.workSessionId}
              title="Registrar salida de dinero"
              className="flex h-8 items-center gap-1.5 rounded-full border border-amber-300/20 px-3 text-xs font-semibold text-amber-200 transition hover:bg-amber-300/10 hover:text-white disabled:opacity-40"
            >
              <ArrowUpFromLine className="h-3.5 w-3.5" />
              <span className="hidden md:inline">Salida de dinero</span>
            </button>
            <button
              type="button"
              onClick={() => setDenominationCalculatorOpen(true)}
              disabled={busy}
              title="Calculadora de denominaciones (Ctrl+D)"
              aria-keyshortcuts="Control+D"
              className="flex h-8 items-center gap-1.5 rounded-full border border-teal-300/20 px-3 text-xs font-semibold text-teal-100 transition hover:bg-teal-300/10 hover:text-white disabled:opacity-40"
            >
              <Calculator className="h-3.5 w-3.5" />
              <span className="hidden md:inline">Denominaciones</span>
              <kbd className="hidden xl:inline text-[10px] opacity-70">Ctrl+D</kbd>
            </button>
          </div>
          {client.mode === "edge" && (
            <button
              type="button"
              onClick={() => setSynchronizationEventsOpen(true)}
              title={synchronizationTitle}
              aria-label={synchronizationTitle}
              className={`flex h-8 items-center gap-1.5 rounded-full border px-3 text-xs font-semibold transition ${synchronization.failed || synchronization.pendingCount > 0 ? "border-amber-300/40 text-amber-200" : "border-white/10 text-auraly-secondary hover:bg-white/10 hover:text-white"}`}
            >
              <RotateCcw className={`h-3.5 w-3.5 ${synchronization.inProgress ? "animate-spin" : ""}`} />
              <span>{synchronization.inProgress
                ? "Sincronizando"
                : synchronization.pendingCount > 0
                  ? `${synchronization.pendingCount} por subir`
                : !serverConnected
                  ? "API desconectada"
                : !pushConnected
                  ? "Señal interrumpida"
                : synchronization.failed
                  ? "Revisar datos"
                    : "Datos al día"}</span>
            </button>
          )}
          {client.mode === "online" && !canOpenAdministrativeMenu && (
            <button
              type="button"
              onClick={() => void logoutOnlineUser()}
              disabled={busy}
              title="Cerrar sesión"
              className="flex h-8 items-center gap-1.5 rounded-lg border border-white/10 px-2.5 text-xs font-semibold text-auraly-secondary transition hover:bg-white/10 hover:text-white disabled:opacity-40"
            >
              <LogOut className="h-4 w-4" />
              <span className="hidden lg:inline">Cerrar sesión</span>
            </button>
          )}
        </div>
        <div className="flex min-w-0 items-center gap-3 text-sm">
          {canChangeWorkspace ? (
            <button
              type="button"
              onClick={() => void changeOnlineWorkspace()}
              disabled={busy}
              title="Cambiar sede, bodega o documento predeterminado"
              className="flex min-w-0 h-8 items-center gap-1.5 rounded-lg border border-white/10 px-2.5 text-sm font-bold tracking-tight text-auraly-secondary transition hover:bg-white/10 hover:text-white disabled:opacity-40"
              aria-label="Cambiar ubicación del punto de venta"
            >
              <span className="min-w-0 truncate">{workstation.businessName || "Sede"} · {workstation.warehouseName || "Bodega"}</span>
              <Settings2 className="h-4 w-4" />
            </button>
          ) : <span className="min-w-0 truncate font-bold tracking-tight">{workstation.businessName || "Sede"} · {workstation.warehouseName || "Bodega"}</span>}
          {client && (
            <button
              type="button"
              onClick={() => setPrinterOpen(true)}
              disabled={busy}
              title="Configurar impresora, cajón y balanza"
              className="flex h-8 items-center gap-1.5 rounded-lg border border-white/10 px-2.5 text-xs font-semibold text-auraly-secondary transition hover:bg-white/10 hover:text-white disabled:opacity-40"
            >
              <Printer className="h-4 w-4" />
              <span className="hidden lg:inline">Periféricos</span>
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

      {workstation.fiscalWarnings.length > 0 && (
        <div role="alert" className="flex items-start gap-3 border-b border-amber-300/40 bg-amber-100 px-5 py-3 text-sm text-amber-950">
          <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
          <div><strong className="block">Atención con la resolución DIAN</strong><ul className="mt-1 list-disc pl-5">{workstation.fiscalWarnings.map((warning) => <li key={warning}>{warning}</li>)}</ul></div>
        </div>
      )}

      <section className="grid min-h-[calc(100vh-3.5rem)] grid-cols-1 gap-3 p-3 xl:h-[calc(100vh-3.5rem)] xl:min-h-0 xl:grid-cols-[minmax(0,1fr)_340px] 2xl:grid-cols-[minmax(0,1fr)_390px]">
        <div className="flex min-w-0 flex-col gap-3 xl:min-h-0">
          <form
            onSubmit={capture}
            className={`relative overflow-hidden rounded-2xl border bg-white p-3 shadow-sm transition-all duration-300 ${scanRejection.phase !== "idle" ? "border-red-500 shadow-lg shadow-red-200/70 ring-4 ring-red-300/50" : "border-teal-900/10"} ${scanRejection.phase === "animating" ? "pos-scan-rejected" : ""}`}
          >
            <label
              htmlFor="pos-scanner"
              className="mb-2 flex items-center justify-between gap-3 text-sm font-medium text-slate-700"
            >
              <span className="flex items-center gap-2">
                <Barcode className="h-5 w-5 text-teal-700" />
                Escanea o escribe un código
              </span>
              {scanRejection.phase === "idle" ? (
                <span className="text-xs font-normal text-slate-500">Enter para agregar</span>
              ) : (
                <span className="inline-flex items-center gap-1.5 rounded-full bg-red-600 px-3 py-1 text-xs font-bold uppercase tracking-wide text-white shadow-sm">
                  <XCircle className="h-3.5 w-3.5" />
                  Lectura rechazada
                </span>
              )}
            </label>
            <div className="flex gap-2">
              <input
                ref={scanner}
                id="pos-scanner"
                value={scan}
                onChange={(event) => {
                  setScan(event.target.value);
                  if (scanRejection.phase !== "idle") {
                    clearScanRejection();
                    setError(null);
                    setMessage("Leyendo nuevo código");
                  }
                }}
                onKeyDown={(event) => {
                  // Barcode readers send Enter as a synthetic keyboard event and
                  // some installed WebView versions skip implicit form submission.
                  if (!submitPosCaptureOnEnter(event) &&
                    (event.key === "ArrowDown" || event.key === "ArrowUp") &&
                    draft?.lines.length
                  ) {
                    event.preventDefault();
                    focusLastQuantity();
                  } else if (event.key === "Tab" && draft?.lines.length) {
                    event.preventDefault();
                    focusFirstQuantity();
                  }
                }}
                disabled={busy || !salesReady}
                autoComplete="off"
                inputMode="text"
                className={`h-14 min-w-0 flex-1 rounded-xl border-2 px-4 text-xl font-semibold tracking-wide outline-none transition disabled:opacity-50 ${scanRejection.phase !== "idle" ? "border-red-600 bg-white text-red-950 focus:border-red-600 focus:ring-4 focus:ring-red-500/20" : "border-teal-700/25 bg-slate-50 focus:border-teal-600 focus:bg-white focus:ring-4 focus:ring-teal-600/10"}`}
                placeholder="Código de barras, interno o referencia"
                aria-describedby="capture-state"
              />
              <button
                type="submit"
                disabled={!scan.trim() || busy || !salesReady}
                className="min-w-28 rounded-xl bg-teal-700 px-5 font-semibold text-white transition hover:bg-teal-800 focus:outline-none focus:ring-4 focus:ring-teal-600/20 disabled:cursor-not-allowed disabled:opacity-50"
              >
                {busy ? <Loader2 className="mx-auto animate-spin" /> : "Agregar"}
              </button>
              <button
                type="button"
                onClick={() => { setPriceVerifierMode(false); setProductSearchOpen(true); }}
                disabled={busy || !salesReady}
                className="flex min-w-28 items-center justify-center gap-2 rounded-xl border border-teal-700/25 bg-white px-4 font-semibold text-teal-800 transition hover:bg-teal-50 focus:outline-none focus:ring-4 focus:ring-teal-600/15 disabled:opacity-45"
              >
                <Search className="h-4 w-4" />
                Buscar <span className="rounded bg-teal-50 px-1.5 py-0.5 text-xs">{POS_ACTION_SHORTCUTS.productSearch}</span>
              </button>
            </div>
            {scanRejection.phase !== "idle" && (
              <div className="mt-2 flex items-center gap-2 rounded-xl border border-red-200 bg-gradient-to-r from-red-50 to-rose-50 px-3 py-2 text-sm text-red-900" role="alert">
                <span className="grid h-8 w-8 shrink-0 place-items-center rounded-full bg-red-600 text-white shadow-sm shadow-red-300">
                  <X className="h-5 w-5" strokeWidth={3} />
                </span>
                <span className="min-w-0">
                  <strong className="block">Este producto no pasó</strong>
                  <span className="block truncate text-xs text-red-700">Código: {scanRejection.value || "captura inválida"} · vuelve a escanearlo o búscalo con {POS_ACTION_SHORTCUTS.productSearch}</span>
                </span>
              </div>
            )}
            {(scanRejection.phase === "idle" || error) && <p
              id="capture-state"
              className={`mt-2 flex min-h-5 items-center gap-2 text-sm ${
                error ? "text-red-700" : "text-slate-500"
              }`}
              role={error ? "alert" : "status"}
            >
              {error ? <AlertTriangle className="h-4 w-4" /> : <CheckCircle2 className="h-4 w-4 text-teal-600" />}
              {error ?? message}
            </p>}
          </form>

          <div
            className="flex flex-wrap items-center justify-between gap-2 rounded-2xl border border-slate-200 bg-white p-2 shadow-sm"
            aria-label="Acciones de la venta"
          >
            <p className="hidden pl-2 text-xs font-semibold uppercase tracking-[0.14em] text-slate-500 sm:block">
              Acciones de captura
            </p>
            <div className="grid w-full grid-cols-2 gap-2 sm:ml-auto sm:w-auto xl:grid-cols-5">
              <button type="button" disabled={!draft?.lines.length || busy}
                onClick={() => void openDiscount()}
                className="flex h-11 items-center justify-center gap-2 rounded-xl border border-amber-200 bg-amber-50 px-3 text-sm font-semibold text-amber-900 transition hover:bg-amber-100 disabled:cursor-not-allowed disabled:border-slate-200 disabled:bg-slate-50 disabled:text-slate-400">
                <PencilLine className="h-4 w-4" />
                Editar líneas
                <span className="rounded bg-white/70 px-1.5 py-0.5 text-[10px]">{POS_ACTION_SHORTCUTS.editLines}</span>
              </button>
              <button type="button" disabled={!hasSelectedLine || busy}
                onClick={() => { if (selectedLineId) void requestRemoveLine(selectedLineId); }}
                className="flex h-11 items-center justify-center gap-2 rounded-xl border border-red-200 bg-red-50 px-3 text-sm font-semibold text-red-800 transition hover:bg-red-100 disabled:cursor-not-allowed disabled:border-slate-200 disabled:bg-slate-50 disabled:text-slate-400">
                <Trash2 className="h-4 w-4" />
                Eliminar
                <span className="rounded bg-white/70 px-1.5 py-0.5 text-[10px]">{POS_ACTION_SHORTCUTS.removeLine}</span>
              </button>
              <button type="button" disabled={!draft?.lines.length || busy}
                onClick={() => void requestCancelSale()}
                className="flex h-11 items-center justify-center gap-2 rounded-xl border border-slate-300 bg-slate-50 px-3 text-sm font-semibold text-slate-700 transition hover:bg-slate-100 disabled:cursor-not-allowed disabled:text-slate-400">
                <RotateCcw className="h-4 w-4" />
                Reiniciar
                <span className="rounded bg-white px-1.5 py-0.5 text-[10px]">{POS_ACTION_SHORTCUTS.restartSale}</span>
              </button>
              <button type="button" disabled={!salesReady || busy}
                onClick={() => setInvoiceSearchOpen(true)}
                className="flex h-11 items-center justify-center gap-2 rounded-xl border border-teal-200 bg-teal-50 px-3 text-sm font-semibold text-teal-900 transition hover:bg-teal-100 disabled:cursor-not-allowed disabled:border-slate-200 disabled:bg-slate-50 disabled:text-slate-400">
                <Printer className="h-4 w-4" />
                Facturas
                <span className="rounded bg-white/70 px-1.5 py-0.5 text-[10px]">{POS_ACTION_SHORTCUTS.invoices}</span>
              </button>
              <button type="button"
                disabled={!serverConnected || busy}
                onClick={() => setReturnsOpen(true)}
                title={serverConnected ? "Abrir devoluciones" : "Requiere conexión con Auraly Server"}
                className="flex h-11 items-center justify-center gap-2 rounded-xl border border-teal-200 bg-white px-3 text-sm font-semibold text-teal-900 transition hover:bg-teal-50 disabled:cursor-not-allowed disabled:border-slate-200 disabled:bg-slate-50 disabled:text-slate-400">
                <RotateCcw className="h-4 w-4" />
                Devoluciones
                <span className="rounded bg-teal-50 px-1.5 py-0.5 text-[10px]">{POS_ACTION_SHORTCUTS.returns}</span>
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
                    <th className="w-36 whitespace-nowrap px-3 py-3 text-right" title="Precio de venta con IVA incluido">Precio venta</th>
                    <th className="w-36 px-3 py-3 text-right">Total</th>
                    <th className="w-16 px-3 py-3" aria-label="Acciones" />
                  </tr>
                </thead>
                <tbody>
                  {draft?.lines.map((line) => (
                    <tr
                      key={line.lineId}
                      ref={(element) => {
                        if (element) {
                          lineRows.current.set(line.lineId, element);
                        } else {
                          lineRows.current.delete(line.lineId);
                        }
                      }}
                      onClick={() => {
                        setSelectedLineId(line.lineId);
                        window.requestAnimationFrame(() => {
                          quantityInputs.current.get(line.lineId)?.focus();
                          quantityInputs.current.get(line.lineId)?.select();
                        });
                      }}
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
                              Descuento -{money.format(line.discount)}
                            </span>
                          )}
                          {(line.promotionDiscount ?? 0) > 0 && (
                            <span className="rounded-full border border-emerald-200 bg-emerald-50 px-2 py-0.5 text-emerald-800">
                              Promoción -{money.format(line.promotionDiscount ?? 0)}
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
                          ref={(element) => {
                            if (element) {
                              quantityInputs.current.set(line.lineId, element);
                            } else {
                              quantityInputs.current.delete(line.lineId);
                            }
                          }}
                          type="number"
                          inputMode={line.allowsFractionalSale ? "decimal" : "numeric"}
                          min={line.allowsFractionalSale ? "0.001" : "1"}
                          step={line.allowsFractionalSale ? "0.001" : "1"}
                          value={quantityDrafts[line.lineId] ?? String(line.quantity)}
                          onChange={(event) => {
                            const value = event.target.value;
                            if (!acceptsPosQuantityDraft(value, { allowsFractionalSale: line.allowsFractionalSale, managesInventory: false })) return;
                            setQuantityDrafts((current) => ({
                              ...current, [line.lineId]: value.replace(",", "."),
                            }));
                          }}
                          onFocus={() => setSelectedLineId(line.lineId)}
                          onKeyDown={(event) => {
                            if (blocksPosQuantityKey(event.key, { allowsFractionalSale: line.allowsFractionalSale, managesInventory: false })) {
                              event.preventDefault();
                              return;
                            }
                            if (event.key === "ArrowDown" || event.key === "ArrowUp") {
                              event.preventDefault();
                              void navigateFromQuantity(
                                line.lineId,
                                event.currentTarget.valueAsNumber,
                                event.key === "ArrowUp",
                              );
                            } else if (event.key === "Enter") {
                              event.preventDefault();
                              void navigateFromQuantity(
                                line.lineId,
                                event.currentTarget.valueAsNumber,
                                false,
                              );
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
                            const validation = validatePosQuantity(event.currentTarget.value, { allowsFractionalSale: line.allowsFractionalSale, managesInventory: false });
                            if (!validation.valid && validation.reason !== "whole-units") {
                              setQuantityDrafts((current) => ({
                                ...current, [line.lineId]: String(line.quantity),
                              }));
                              focusScanner();
                            } else if (!validation.valid) {
                              setQuantityDrafts((current) => ({ ...current, [line.lineId]: String(line.quantity) }));
                              setError(`${line.description} solo se vende en unidades completas.`);
                              focusScanner();
                            } else if (validation.quantity !== line.quantity) {
                              void changeQuantity(line.lineId, validation.quantity);
                            } else {
                              focusScanner();
                            }
                          }}
                          className="h-10 w-24 rounded-lg border border-slate-300 bg-white px-2 text-right font-semibold outline-none focus:border-teal-600 focus:ring-2 focus:ring-teal-600/15"
                          aria-label={`Cantidad de ${line.description}`}
                        />
                      </td>
                      <td className="px-3 py-3 text-right font-medium tabular-nums text-slate-700">
                        {effectiveUnitMoney.format(calculateEffectiveRetailUnitPrice(
                          line.total,
                          line.quantity,
                        ))}
                      </td>
                      <td className="px-3 py-3 text-right text-base font-bold tabular-nums text-slate-950">
                        {money.format(line.total)}
                      </td>
                      <td className="px-3 py-2 text-right">
                        <button
                          type="button"
                          onClick={() => void requestRemoveLine(line.lineId)}
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
                <p className="relative mt-6 text-xl font-bold tracking-tight text-slate-900">Lista para vender</p>
                <p className="relative mt-1 max-w-md text-sm text-slate-500">
                  Escanea el primer producto o abre la búsqueda. El lector queda preparado para continuar sin usar el mouse.
                </p>
                <div className="relative mt-5 flex flex-wrap items-center justify-center gap-2 text-xs font-semibold">
                  <span className="rounded-full border border-teal-200 bg-white px-3 py-1.5 text-teal-800">Lector activo</span>
                  <span className="rounded-full border border-slate-200 bg-white px-3 py-1.5 text-slate-700">Buscar producto · {POS_ACTION_SHORTCUTS.productSearch}</span>
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
                {draft?.sourceOrderId && (
                  <p className="mt-1 inline-flex rounded-md bg-amber-300/15 px-2 py-1 text-xs font-bold text-amber-200">
                    Pedido recuperado · ocupado por esta sesión
                  </p>
                )}
              </div>
              <span className="rounded-lg bg-white/10 px-2 py-1 text-xs">
                {draft?.lines.length ?? 0} líneas
              </span>
            </div>
            <button
              type="button"
              disabled={!salesReady || busy}
              onClick={() => setCustomerSearchOpen(true)}
              title="Buscar cliente"
              className="mb-3 flex h-10 w-full items-center justify-center gap-2 rounded-xl border border-auraly-accent/40 bg-white/5 text-sm font-semibold transition hover:bg-white/10 disabled:opacity-40"
            >
              <UserRound className="h-4 w-4" />
              Buscar cliente
              <span className="rounded bg-white/10 px-1.5 py-0.5 text-xs">{POS_ACTION_SHORTCUTS.customerSearch}</span>
            </button>
            <dl className="space-y-2 text-sm">
              <TotalRow label="Subtotal" value={draft?.untaxedAmount ?? 0} />
              <TotalRow label="Total impuestos" value={draft?.taxAmount ?? 0} />
              {(saleSettlement?.withholdingTotal ?? 0) > 0 && <>
                <TotalRow label="Total bruto" value={saleSettlement?.grossAmount ?? draft?.payableAmount ?? 0} />
                <TotalRow label="Retenciones" value={-(saleSettlement?.withholdingTotal ?? 0)} />
              </>}
              <div className="border-t border-white/15 pt-3">
                <dt className="text-sm text-auraly-secondary">Neto por cobrar</dt>
                <dd className="text-right text-3xl font-bold tracking-tight text-auraly-light">
                  {money.format(saleSettlement?.netAmount ?? draft?.payableAmount ?? 0)}
                </dd>
              </div>
            </dl>
            {saleSettlementError && (
              <p className="mt-2 rounded-lg bg-amber-400/10 px-3 py-2 text-xs text-amber-200">
                No fue posible calcular las retenciones en este momento. Al cobrar se validarán nuevamente.
              </p>
            )}
            <button
              type="button"
              disabled={!draft?.lines.length || busy}
              onClick={openPayment}
              className="mt-3 flex h-12 w-full items-center justify-center gap-2 rounded-xl bg-auraly-accent px-4 text-base font-bold text-auraly-background transition hover:bg-auraly-light disabled:cursor-not-allowed disabled:opacity-40"
            >
              Cobrar
              <span className="rounded bg-black/10 px-2 py-0.5 text-xs font-semibold">{POS_ACTION_SHORTCUTS.payment}</span>
            </button>

            <div className="mt-2 grid grid-cols-2 gap-2">
              <button
                type="button"
                disabled={!draft?.lines.length || Boolean(draft?.sourceOrderId) || busy}
                onClick={() => void requestPauseSale()}
                title={draft?.sourceOrderId ? "Guarda los cambios del pedido o reinicia la venta para liberarlo" : "Guardar como venta temporal"}
                className="flex h-10 min-w-0 items-center justify-center gap-1.5 rounded-xl border border-auraly-accent/40 bg-white/5 px-2 text-sm font-semibold transition hover:bg-white/10 disabled:cursor-not-allowed disabled:opacity-40"
              >
                <Save className="h-4 w-4 shrink-0" />
                <span className="truncate">Pausar venta</span>
                <span className="shrink-0 rounded bg-white/10 px-1.5 py-0.5 text-xs font-semibold">{POS_ACTION_SHORTCUTS.pauseSale}</span>
              </button>

              <button
                type="button"
                disabled={!orderSaveAvailable}
                onClick={requestSaveOrder}
                title={draft?.customerId
                  ? "Reserva las existencias en la bodega Pedidos y limpia la venta"
                  : "Selecciona el cliente y guarda el pedido"}
                className="flex h-10 min-w-0 items-center justify-center gap-1.5 rounded-xl border border-emerald-300/45 bg-emerald-400/10 px-2 text-sm font-semibold text-emerald-100 transition hover:bg-emerald-400/20 disabled:cursor-not-allowed disabled:opacity-40"
              >
                <ClipboardList className="h-4 w-4 shrink-0" />
                <span className="truncate">{draft?.sourceOrderId ? "Guardar cambios" : "Guardar pedido"}</span>
                <span className="shrink-0 rounded bg-white/10 px-1.5 py-0.5 text-xs font-semibold">{POS_ACTION_SHORTCUTS.saveOrder}</span>
              </button>
            </div>

          </section>

          {showCashChange && lastSettlement ? (
            <section
              className="relative flex min-h-0 flex-1 overflow-hidden rounded-2xl border border-emerald-300 bg-emerald-50 p-4 shadow-sm"
              role="status"
              aria-live="assertive"
            >
              <button type="button" onClick={() => { setLastSettlement(null); focusScanner(); }} aria-label="Cerrar aviso de cambio" className="absolute right-3 top-3 z-10 grid h-9 w-9 place-items-center rounded-full border border-emerald-700/20 bg-white/80 text-emerald-900 shadow-sm hover:bg-white"><X className="h-5 w-5"/></button>
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
                   {ordersCount}
                </span>
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
            ) : !ordersExpanded ? (
              <OrdersWorkspace
                key={`compact-orders-${ordersRefreshVersion}`}
                compact
                invoicePrintMode="always"
                initialStatus="Available"
                activeOrderId={draft?.sourceOrderId}
                loadPage={loadCommerceOrders}
                loadDetail={loadCommerceOrder}
                onRecover={(order) => recoverPosOrder(order.orderId)}
                onPrintSelected={async (orders) =>
                  printOrdersOnline(orders.map((order) => order.orderId))}
                onInvoiceSelected={(orders, documentType, paymentMethodCode, _printAfterInvoice, idempotencyKey, onProgress) =>
                  invoicePosOrders(
                    orders.map((order) => order.orderId),
                    paymentMethodCode,
                    documentType,
                    idempotencyKey,
                    onProgress,
                  )
                }
                onConfigurePrinting={() => setPrinterOpen(true)}
                onCountChange={setOrdersCount}
                onExpand={openOrders}
              />
            ) : null
            }
          </section>
          )}
        </aside>
      </section>

      {ordersExpanded && client && (
        <div className="fixed inset-0 z-50 flex flex-col bg-slate-50">
          <header className="flex h-16 shrink-0 items-center justify-between border-b border-slate-200 bg-white px-6">
            <div>
              <p className="text-xs font-semibold uppercase tracking-[0.18em] text-teal-700">
                Auraly
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
              key={`expanded-orders-${ordersRefreshVersion}`}
              invoicePrintMode="always"
              initialStatus="Available"
              activeOrderId={draft?.sourceOrderId}
              loadPage={loadCommerceOrders}
              loadDetail={loadCommerceOrder}
              onRecover={(order) => recoverPosOrder(order.orderId)}
              onPrintSelected={async (orders) =>
                printOrdersOnline(orders.map((order) => order.orderId))}
              onInvoiceSelected={(orders, documentType, paymentMethodCode, _printAfterInvoice, idempotencyKey, onProgress) =>
                invoicePosOrders(
                  orders.map((order) => order.orderId),
                  paymentMethodCode,
                  documentType,
                  idempotencyKey,
                  onProgress,
                )
              }
              onConfigurePrinting={() => setPrinterOpen(true)}
              onCountChange={setOrdersCount}
            />
          </main>
        </div>
      )}

      {returnsOpen && serverConnected && (
        <div className="fixed inset-0 z-50 flex flex-col bg-slate-50">
          <header className="flex h-16 shrink-0 items-center justify-between border-b border-slate-200 bg-white px-6">
            <div><p className="text-xs font-semibold uppercase tracking-[0.18em] text-teal-700">Auraly</p><h2 className="text-xl font-bold text-slate-950">Devoluciones de venta</h2></div>
            <button type="button" onClick={() => { setReturnsOpen(false); focusScanner(); }}
              className="grid h-11 w-11 place-items-center rounded-xl border border-slate-200 text-slate-600 transition hover:bg-slate-100"
              aria-label="Cerrar devoluciones"><X className="h-5 w-5" /></button>
          </header>
          <main className="min-h-0 flex-1 overflow-auto p-5">
            <SalesReturnWorkspace embedded businessId={workstation.businessId} onCashRefundConfirmed={openCashDrawer} />
          </main>
        </div>
      )}

      {productSearchOpen && client && (
        <PosProductSearchDialog
          busy={busy}
          verifierMode={priceVerifierMode}
          focusRequest={productSearchFocusRequest}
          availabilityRequest={productAvailabilityRequest}
          onSearch={searchProducts}
          connected={serverConnected}
          canReadAvailability={canReadProductAvailability}
          onLoadAvailability={loadProductAvailability}
          onSelect={selectSearchProduct}
          onCancel={() => {
            setProductSearchOpen(false);
            setPriceVerifierMode(false);
            focusScanner();
          }}
        />
      )}

      {customerSearchOpen && client && (
        <PosCustomerSearchDialog
          busy={busy}
          connected={serverConnected}
          onCountries={customerCountries}
          onDivisions={customerDivisions}
          onCities={customerCities}
          onCreate={createCustomer}
          onSearch={searchCustomers}
          onSelect={selectCustomer}
          onCancel={() => {
            saveOrderAfterCustomerSelection.current = false;
            setCustomerSearchOpen(false);
            focusScanner();
          }}
        />
      )}

      {cashMovementDirection && client && (
        <PosCashMovementDialog
          client={client}
          initialDirection={cashMovementDirection}
          responsibleName={workstation.userDisplayName}
          onClose={() => {
            setCashMovementDirection(null);
            focusScanner();
          }}
          onCompleted={(text) => {
            setMessage(text);
            setCashMovementDirection(null);
            focusScanner();
          }}
        />
      )}

      {closurePreview && (
        <PosCashClosureDialog
          value={closurePreview}
          busy={busy}
          submitted={Boolean(closureAttempt)}
          onOpenDenominations={() => setDenominationCalculatorOpen(true)}
          onClose={() => {
            setClosurePreview(null);
            setClosureAttempt(null);
            closureOperationId.current = null;
            closureAuthorization.current = null;
            focusScanner();
          }}
          onConfirm={confirmSalesSessionClosure}
        />
      )}

      {denominationCalculatorOpen && client && (
        <PosCashDenominationDialog
          client={client}
          businessName={workstation.businessName}
          userName={workstation.userDisplayName}
          onClose={() => {
            setDenominationCalculatorOpen(false);
            if (!closurePreview) focusScanner();
          }}
        />
      )}

      {inventoryResolution && draft && (
        <PosInventoryResolutionDialog
          value={inventoryResolution}
          busy={busy}
          onChangeQuantity={(lineId, quantity) => changeQuantity(lineId, quantity, false)}
          onRemove={(lineId) => { void requestRemoveLine(lineId); }}
          onRetry={() => validateRecoveredInventory(draft.draftId.value).then(() => undefined)}
          onCancel={() => {
            setInventoryResolution(null);
            focusScanner();
          }}
        />
      )}

      {paymentOpen && draft && saleSettlement && (
        <PosPaymentDialog
          client={client}
          total={saleSettlement.netAmount}
          grossTotal={saleSettlement.grossAmount}
          withholdingTotal={saleSettlement.withholdingTotal}
          busy={busy}
          documentType={selectedCustomer?.requiresElectronicInvoice ? "SalesInvoice" : documentType}
          documentTypeLocked={selectedCustomer?.requiresElectronicInvoice ?? false}
          documentTypeReady={canIssuePosDocument(
            selectedCustomer?.requiresElectronicInvoice ? "SalesInvoice" : documentType,
            workstation.fiscalReady,
            workstation.dianQuotaAvailable !== false,
          )}
          customer={selectedCustomer}
          focusRequest={paymentFocusRequest}
          onChangeDocumentType={() => setDocumentTypeOpen(true)}
          onCancel={() => {
            setPaymentOpen(false);
            setSaleSettlement(null);
            focusScanner();
          }}
          onConfirm={completeSale}
        />
      )}

      {printerOpen && client && (
        <PosPrinterDialog client={client instanceof PosEdgeClient || (client instanceof OnlinePosClient && edgeEnrollmentToken) ? client : null}
          onClose={() => {
            setPrinterOpen(false);
            focusScanner();
          }} />
      )}

      {invoiceSearchOpen && client && (
        <PosInvoiceSearchDialog
          busy={busy}

          onSearch={searchIssuedSales}
          onSearchCustomers={searchHistoryCustomers}
          onSearchProducts={searchHistoryProducts}
          onDetail={loadIssuedSaleDetail}
          onReprint={reprintSale}
          onCancel={() => {
            setInvoiceSearchOpen(false);
            focusScanner();
          }}
        />
      )}

      {documentTypeOpen && (
        <PosDocumentTypeDialog
          client={client}
          value={documentType}
          invoiceRequired={selectedCustomer?.requiresElectronicInvoice ?? false}
          businessId={workstation.businessId}
          edgeMode={client.mode === "edge"}
          edgeFiscalReady={workstation.fiscalReady}
          onFiscalEnrollmentRequired={() => setError("La factura electrónica no está disponible en esta caja. Un administrador debe completar la configuración fiscal y la caja la recibirá automáticamente.")}
          busy={busy}
          onSelect={changeDocumentType}
          onCancel={() => {
            setDocumentTypeOpen(false);
            if (paymentOpen) setPaymentFocusRequest((current) => current + 1);
            else focusScanner();
          }}
        />
      )}

      {discountOpen && draft && <PosLineEditorDialog
        lines={draft.lines}
        busy={busy}
        canEditDescription={activePosPermissions.includes("sales.lines.change-description")}
        canReadCostAndMargin={activePosPermissions.includes("sales.lines.cost-margin.read")}
        canEditCommercialValues={activePosPermissions.includes("sales.change-price")}
        canApplyProratedDiscount={activePosPermissions.includes("sales.lines.prorated-discount")}
        onConfirm={applyLineEdits}
        onCancel={() => {
          setDiscountOpen(false);
          focusScanner();
        }}
      />}

      {genericProductLine && <PosGenericProductDialog
        line={genericProductLine}
        busy={busy}
        onConfirm={confirmGenericProduct}
        onCancel={cancelGenericProduct}
      />}

      {sensitiveApproval && (
        <PosSupervisorApprovalDialog
          approval={sensitiveApproval.approval}
          allowRemote={serverConnected && Boolean(sensitiveApproval.approval)}
          busy={busy}
          error={sensitiveApprovalError}
          loadApproval={loadSensitiveApproval}
          subscribeApprovals={subscribeSensitiveApprovals}
          onRemoteApproved={completeRemoteApproval}
          onLocalSecret={completeLocalApproval}
          onRetry={retrySensitiveApproval}
          onCancel={() => {
            setSensitiveApproval(null);
            setSensitiveApprovalError(null);
            focusScanner();
          }}
        />
      )}

      {confirmation && (
        <PosConfirmDialog
          title={
            confirmation.kind === "line"
              ? "¿Eliminar este producto?"
              : confirmation.kind === "temporary"
                ? "¿Eliminar esta venta en espera?"
                : confirmation.kind === "order-save"
                  ? "¿Actualizar el pedido recuperado?"
              : confirmation.sourceOrderNumber
                ? "¿Eliminar el pedido y reiniciar la venta?"
                : "¿Reiniciar toda la venta?"
          }
          description={
            confirmation.kind === "line"
              ? `${confirmation.productName} se retirará de la venta actual.`
              : confirmation.kind === "temporary"
                ? `${confirmation.name} se eliminará definitivamente de este dispositivo.`
                : confirmation.kind === "order-save"
                  ? `${confirmation.orderNumber} actualizará su cliente, productos, cantidades, precios y descuentos con los valores de esta venta. La reserva de inventario se ajustará automáticamente.`
              : confirmation.sourceOrderNumber
                ? `${confirmation.sourceOrderNumber} quedará cancelado, su inventario volverá a ventas y se abrirá una venta limpia.`
                : "Se eliminarán todos los productos capturados y se abrirá una venta limpia."
          }
          confirmLabel={
            confirmation.kind === "sale"
              ? confirmation.sourceOrderNumber ? "Sí, eliminar y reiniciar" : "Sí, reiniciar"
              : confirmation.kind === "order-save"
                ? "Sí, actualizar"
                : "Sí, eliminar"
          }
          tone={confirmation.kind === "order-save" ? "primary" : "danger"}
          busy={busy}
          onConfirm={confirmDestructiveAction}
          onCancel={() => {
            if (confirmation.kind === "line") lineRemovalAuthorization.current = null;
            if (confirmation.kind === "sale") restartAuthorization.current = null;
            if (confirmation.kind === "temporary") temporaryRemovalAuthorization.current = null;
            setConfirmation(null);
            focusScanner();
          }}
        />
      )}
      {synchronizationEventsOpen && client && <PosSynchronizationEventsDialog open client={client} serverConnected={serverConnected} pushConnected={pushConnected} canSynchronize inProgress={synchronization.inProgress} pendingCount={synchronization.pendingCount} failed={synchronization.failed} error={synchronization.error} onSynchronize={synchronizeNow} onClose={() => { setSynchronizationEventsOpen(false); focusScanner(); }} />}

      {quantityShortage && <PosQuantityAvailabilityDialog
        value={quantityShortage}
        busy={busy}
        onConfirm={async (quantity) => {
          const { lineId } = quantityShortage;
          const pendingCapture = pendingShortageCapture.current;
          pendingShortageCapture.current = null;
          setQuantityShortage(null);
          if (lineId) await changeQuantity(lineId, quantity, false);
          else if (pendingCapture?.kind === "product")
            await captureSelectedProduct(pendingCapture.product, quantity);
          else if (pendingCapture?.kind === "code")
            await captureValue(pendingCapture.code, quantity);
          if (productSearchOpen) focusProductSearch();
          else focusScanner();
        }}
        onCancel={() => {
          const lineId = quantityShortage.lineId;
          pendingShortageCapture.current = null;
          setQuantityShortage(null);
          if (!lineId) {
            if (productSearchOpen) focusProductSearch();
            else window.setTimeout(focusScanner, 0);
            return;
          }
          window.requestAnimationFrame(() => {
            quantityInputs.current.get(lineId)?.focus();
            quantityInputs.current.get(lineId)?.select();
          });
        }}
      />}

      {temporaryOpen && (
        <div className="fixed inset-0 z-50 grid place-items-center bg-slate-950/55 p-4" data-pos-focus-surface="modal">
          <form
            onSubmit={saveTemporary}
            role="dialog"
            aria-modal="true"
            onKeyDown={(event) => {
              if (event.key !== "Escape" || busy) return;
              event.preventDefault();
              setTemporaryOpen(false);
              focusScanner();
            }}
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
      {pricingTransition && <div className="fixed inset-0 z-[80] grid place-items-center bg-slate-950/45 p-4" role="status" aria-live="assertive"><div className="w-full max-w-sm rounded-2xl bg-white p-6 text-center shadow-2xl"><Loader2 className="mx-auto h-8 w-8 animate-spin text-teal-700"/><h2 className="mt-4 text-lg font-semibold">Actualizando precios</h2><p className="mt-1 text-sm text-slate-500">Aplicando la lista o el canal del cliente a todos los productos de la venta.</p></div></div>}
    </main>
  );
}

function InventoryPolicyChip({ allowsNegativeStock }: { allowsNegativeStock: boolean }) {
  const presentation = posInventoryPolicyPresentation(allowsNegativeStock);
  return <span
    title={presentation.detail}
    aria-label={presentation.label}
    className={`relative flex h-8 w-8 items-center justify-center rounded-full ${allowsNegativeStock ? "bg-amber-400/15 text-amber-100" : "bg-emerald-400/15 text-emerald-100"}`}
  >
    <Package className="h-4 w-4" aria-hidden="true" />
    {allowsNegativeStock && <span aria-hidden="true" className="absolute h-px w-5 rotate-45 rounded-full bg-current" />}
    <span className="sr-only">{presentation.label}</span>
  </span>;
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
      title={label}
      aria-label={label}
      className={`flex h-8 w-8 items-center justify-center rounded-full ${
        ok ? "bg-emerald-400/15 text-emerald-100" : "bg-amber-400/15 text-amber-100"
      }`}
    >
      <Icon className="h-4 w-4" aria-hidden="true" />
      <span className="sr-only">{label}</span>
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
  if (source === "PriceChannel") return "Canal de precio";
  if (source === "Promotion+PriceChannel") return "Promoción + canal";
  if (source === "Promotion") return "Promoción";
  if (source === "Manual") return "Precio manual";
  return "Precio del negocio";
}
