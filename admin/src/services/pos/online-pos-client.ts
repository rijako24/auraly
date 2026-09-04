import {
  invoiceCommerceOrders,
  loadCommerceOrder,
  loadCommerceOrders,
  recoverCommerceOrder,
  releaseCommerceOrderClaim,
  renewCommerceOrderClaim,
  type CommerceOrderFilters,
  type InvoiceOrdersResponse,
} from "@/services/orders/commerce-orders-client";
import type { SellerOrderResult } from "@/services/api/seller-orders";
import { fiscalConfigurationApi } from "@/services/api/fiscal-configuration";
import { savePosDraftAsOrder } from "@/services/orders/save-pos-order";

import {
  PosCaptureResult,
  PosCatalogProduct,
  PosCatalogSearchPage,
  PosProductWarehouseAvailability,
  PosClient,
  PosCompleteSaleResult,
  PosCustomer,
  PosCreateCustomerInput,
  PosCountry,
  PosAdministrativeDivision,
  PosCity,
  PosSaleDocumentType,
  PosCustomerSearchPage,
  PosCustomerSelection,
  PosDraft,
  PosDraftLine,
  PosDraftLineUpdate,
  PosEdgeError,
  PosEdgeClient,
  readEdgeUserSession,
  PosIssuedSaleSearchPage,
  PosIssuedSaleSummary,
  PosNextNumbers,
  PosPaymentInput,
  PosCreditTerms,
  PosPrintableReceipt,
  type PosCashMovementAcceptance,
  type PosCashMovementDirection,
  type PosCashMovementInput,
  type PosCashMovementTicket,
  type PosCashMovementReason,
  type PosCloseWorkSessionInput,
  type PosWorkSessionClosure,
  type PosAuthorizedClosurePreview,
  PosSensitiveAuthorization,
  PosApprovalCreateInput,
  PosApprovalSummary,
  type PosPrinterConfiguration,
  type PosPrintTemplateFormat,
  type PosSettlementConfiguration,
  loadBrowserPrinterConfiguration,
} from "./pos-edge-client";
import {
  orderReceiptsFromEmission,
  resolvePosOrderPrintRoute,
} from "./pos-order-print-routing";
import { resolvePosReceiptPrintRoute } from "./pos-receipt-print-routing";
import {
  posWorkspaceOptionsCacheKey,
  posWorkspaceStorageKey,
} from "./pos-operational-context";
import { fetchWithSessionRetry } from "@/services/api/client";
import { tenantsApi } from "@/services/api/tenants";
import { referenceOptionsApi } from "@/services/api/reference-options";
import { cashDenominationCountHtml, printCashDenominationCount, printWorkSessionClosure, workSessionCloseRequest, workSessionClosureHtml, workSessionClosurePreviewRequest } from "./pos-work-session-close";
import { cashMovementTicketHtml, printCashMovementTicket } from "./pos-cash-movement-print";
import { receiptBrandMarkup } from "./pos-receipt-brand";
import { posReceiptTypographyCss } from "./pos-receipt-style";
import { isWorkspacePolicySynchronizationMessage } from "./pos-workspace-synchronization";

export type SalesWorkspaceOption = {
  businessId: string;
  businessName: string;
  warehouseId: string;
  warehouseCode: string;
  warehouseName: string;
  warehouseAllowsNegativeStockSales: boolean;
  hasActiveEdgeEnrollment: boolean;
};

export type SalesWorkspaceContext = Omit<
  SalesWorkspaceOption,
  "hasActiveEdgeEnrollment"
> & { workSessionId: string };

type ReceiptRenderContext = Pick<
  SalesWorkspaceContext,
  "businessId" | "warehouseId" | "workSessionId"
> & Partial<Pick<SalesWorkspaceContext, "businessName" | "warehouseName">>;

type OnlineDraftLine = {
  lineId: string;
  productId: string;
  productCode: string;
  description: string;
  unitCode: string;
  taxCode: string;
  taxRate: number;
  quantity: number;
  baseUnitPrice: number;
  unitPrice: number;
  currencyCode: string;
  priceSource: string;
  discount: number;
  documentUnitCost: number;
  allowsDocumentCostOverride: boolean;
  allowsFractionalSale: boolean;
  net: number;
  tax: number;
  total: number;
};

type OnlineDraft = {
  draftId: string;
  businessId: string;
  warehouseId: string;
  workSessionId: string;
  userId: string;
  customerId: string | null;
  sellerId: string | null;
  status: string;
  name: string | null;
  reference: string | null;
  observation: string | null;
  version: number;
  updatedAt: string;
  lines: OnlineDraftLine[];
  untaxedAmount: number;
  taxAmount: number;
  payableAmount: number;
  sourceOrderId: string | null;
};

type OnlineProductPage = {
  items: Array<Omit<PosCatalogProduct, "productId"> & { productId: string }>;
  hasMore: boolean;
  nextOffset: number | null;
};

type OnlineCustomerPage = {
  items: Array<{
    customerId: string;
    identification: string;
    name: string;
    priceChannelId: string | null;
    requiresElectronicInvoice: boolean;
    isCreditEnabled: boolean;
    defaultCreditDueDays: number;
    availableCredit: number | null;
  }>;
  hasMore: boolean;
  nextOffset: number | null;
};

type OnlineCustomerSelection = {
  draft: OnlineDraft;
  customer: OnlineCustomerPage["items"][number] | null;
};

type OnlineIssuedSalePage = {
  items: Array<{
    documentId: string;
    documentType: PosSaleDocumentType;
    documentNumber: string;
    fiscalNumber: string;
    issuedAt: string;
    total: number;
    customerIdentification: string;
    customerName: string;
    fiscalStatus: string;
  }>;
  hasMore: boolean;
  nextOffset: number | null;
};

type OnlineCheckoutResponse = {
  receipt: PosPrintableReceipt;
  nextDraft: OnlineDraft;
  isDuplicate: boolean;
};

const WORKSPACE_OFFLINE_STORE = "seller-workspaces";

function currentPosStorageScope(): { tenantId: string | null; userId: string | null } {
  let userId: string | null = null;
  try {
    const auth = JSON.parse(window.localStorage.getItem("auth-state") ?? "null") as
      | { state?: { user?: { userId?: string } } }
      | null;
    userId = auth?.state?.user?.userId ?? null;
  } catch {
    userId = null;
  }
  return {
    tenantId: window.localStorage.getItem("selected_tenant_id"),
    userId,
  };
}

export async function loadSalesWorkspaceOptions(): Promise<SalesWorkspaceOption[]> {
  const scope = currentPosStorageScope();
  const cacheKey = posWorkspaceOptionsCacheKey(scope.tenantId, scope.userId);
  try {
    const values = await request<SalesWorkspaceOption[]>(
      "/api/commerce/v1/pos/workspace/options",
    );
    const { openSalesOfflineDatabase } = await import("@/lib/sales-offline-database");
    const database = await openSalesOfflineDatabase();
    try {
      await new Promise<void>((resolve, reject) => {
        if (!cacheKey) {
          resolve();
          return;
        }
        const operation = database.transaction(WORKSPACE_OFFLINE_STORE, "readwrite").objectStore(WORKSPACE_OFFLINE_STORE).put({ key: cacheKey, values, updatedAt: new Date().toISOString() });
        operation.onsuccess = () => resolve();
        operation.onerror = () => reject(operation.error);
      });
    } finally { database.close(); }
    return values;
  } catch (error) {
    if (!cacheKey) throw error;
    const { openSalesOfflineDatabase } = await import("@/lib/sales-offline-database");
    const database = await openSalesOfflineDatabase();
    try {
      const cached = await new Promise<{ values: SalesWorkspaceOption[] } | undefined>((resolve, reject) => {
        const operation = database.transaction(WORKSPACE_OFFLINE_STORE, "readonly").objectStore(WORKSPACE_OFFLINE_STORE).get(cacheKey);
        operation.onsuccess = () => resolve(operation.result);
        operation.onerror = () => reject(operation.error);
      });
      if (cached?.values.length) return cached.values;
    } finally { database.close(); }
    throw error;
  }
}

export async function selectSalesWorkspace(
  option: SalesWorkspaceOption,
  change = false,
): Promise<SalesWorkspaceContext> {
  const selected = await request<Omit<SalesWorkspaceContext, "workSessionId">>(
    change ? "/api/commerce/v1/pos/workspace/change" : "/api/commerce/v1/pos/workspace/select",
    {
      method: "POST",
      body: JSON.stringify({
        businessId: option.businessId,
        warehouseId: option.warehouseId,
      }),
    },
  );
  const session = await request<{ workSessionId: string }>(
    "/api/commerce/v1/work-sessions/current",
    {
      method: "POST",
      body: JSON.stringify({
        businessId: selected.businessId,
        warehouseId: selected.warehouseId,
        deviceId: null,
      }),
    },
  );
  const scope = currentPosStorageScope();
  const storageKey = posWorkspaceStorageKey(scope.tenantId, scope.userId);
  if (storageKey)
    window.localStorage.setItem(
      storageKey,
      salesWorkspaceKey(selected.businessId, selected.warehouseId),
    );
  return { ...selected, workSessionId: session.workSessionId };
}
export function rememberSalesWorkspace(option: Pick<SalesWorkspaceOption,"businessId"|"warehouseId">): void {
  try {
    const scope = currentPosStorageScope();
    const storageKey = posWorkspaceStorageKey(scope.tenantId, scope.userId);
    if (storageKey)
      window.localStorage.setItem(
        storageKey,
        salesWorkspaceKey(option.businessId, option.warehouseId),
      );
  } catch { /* IndexedDB remains the durable offline source. */ }
}
export function rememberedSalesWorkspaceKey(): string | null {
  try {
    const scope = currentPosStorageScope();
    const storageKey = posWorkspaceStorageKey(scope.tenantId, scope.userId);
    return storageKey ? window.localStorage.getItem(storageKey) : null;
  } catch {
    return null;
  }
}

export function forgetSalesWorkspace(): void {
  try {
    const scope = currentPosStorageScope();
    const storageKey = posWorkspaceStorageKey(scope.tenantId, scope.userId);
    if (storageKey) window.localStorage.removeItem(storageKey);
  } catch {
    // Storage is only a convenience; the server remains authoritative.
  }
}

export class OnlinePosClient implements PosClient {
  readonly mode = "online" as const;
  private readonly versions = new Map<string, number>();
  private activeDraftId: string | null = null;

  constructor(
    private readonly context: SalesWorkspaceContext,
    private readonly userId: string,
    private readonly userDisplayName: string,
    private readonly edgeSessionToken: string | null = null,
  ) {}

  watchWarehousePolicy(onChanged: (allowsNegativeStock: boolean) => void) {
    let stopped = false;
    let socket: WebSocket | null = null;
    let reconnectTimer: number | null = null;
    const refresh = async () => {
      const options = await request<SalesWorkspaceOption[]>(
        "/api/commerce/v1/pos/workspace/options");
      const current = options.find((option) =>
        option.businessId === this.context.businessId &&
        option.warehouseId === this.context.warehouseId);
      if (!stopped && current)
        onChanged(current.warehouseAllowsNegativeStockSales);
    };
    const connect = async () => {
      try {
        const negotiation = await request<{ clientAccessUri: string }>(
          `/api/commerce/v1/pos/workspace/synchronization/negotiate?businessId=${encodeURIComponent(this.context.businessId)}`,
          { method: "POST" });
        if (stopped) return;
        const current = new WebSocket(
          negotiation.clientAccessUri, "json.webpubsub.azure.v1");
        socket = current;
        current.addEventListener("message", (event: MessageEvent<string>) => {
          if (isWorkspacePolicySynchronizationMessage(event.data))
            void refresh();
        });
        current.addEventListener("close", () => {
          if (stopped || socket !== current) return;
          reconnectTimer = window.setTimeout(() => void connect(), 1_000);
        });
      } catch {
        socket?.close();
        socket = null;
        if (!stopped)
          reconnectTimer = window.setTimeout(() => void connect(), 2_000);
      }
    };
    void connect();
    return () => {
      stopped = true;
      if (reconnectTimer !== null) window.clearTimeout(reconnectTimer);
      socket?.close();
    };
  }

  private localEdge() {
    if (!this.edgeSessionToken)
      throw new PosEdgeError("Esta operación requiere configurar este equipo como caja Auraly.", 409);
    return new PosEdgeClient(this.edgeSessionToken, readEdgeUserSession());
  }

  private async printDirect(
    receipts: PosPrintableReceipt[],
    openDrawer = false,
    workflow: "pos" | "orders" = "pos",
    browserPreview: Window | null = null,
  ) {
    if (this.edgeSessionToken) {
      const edge = this.localEdge();
      const branding = await tenantsApi.getBranding().catch(() => null);
      const jobs = receipts.map((receipt) =>
        edge.printReceipt({
          ...receipt,
          businessName: this.context.businessName,
          warehouseName: this.context.warehouseName,
        }, branding, workflow),
      );
      if (openDrawer) jobs.push(edge.openCashDrawer());
      await Promise.all(jobs);
      return;
    }
    const configuration = loadBrowserPrinterConfiguration();
    const format = workflow === "orders"
      ? configuration.ordersOutputFormat ?? "HalfLetter"
      : configuration.posOutputFormat ?? "Receipt";
    if (format !== "Receipt")
      await renderReceiptsHalfLetter(browserPreview, receipts, this.scope(), format);
    else
      await renderReceiptsReceipt(browserPreview, receipts, this.scope(),
        workflow === "orders"
          ? configuration.ordersReceiptPaperWidthMillimeters ?? 80
          : configuration.receiptPaperWidthMillimeters);
  }

  async health() {
    await request<{ status: string }>("/api/health");
    const [local, fiscal] = await Promise.all([
      this.edgeSessionToken
        ? this.localEdge().health().catch(() => null)
        : Promise.resolve(null),
      fiscalConfigurationApi.get(this.context.businessId).catch(() => null),
    ]);
    return {
      status: "ok",
      serverConnected: true,
      pushConnected: true,
      deviceSeriesCode: "00",
      businessId: this.context.businessId,
      warehouseId: this.context.warehouseId,
      businessName: this.context.businessName,
      warehouseName: this.context.warehouseName,
      warehouseAllowsNegativeStockSales: this.context.warehouseAllowsNegativeStockSales,
      userDisplayName: this.userDisplayName,
      userId: this.userId,
      workSessionId: this.context.workSessionId,
      deviceId: local?.deviceId ?? null,
      fiscalReady: fiscal?.isReadyForOnlineSales === true,
      fiscalWarnings: fiscal?.warningMessages ?? [],
      dianQuotaAvailable: fiscal?.hasDianDocumentQuota ?? false,
      identityReady: true,
      catalogStatus: "Ready",
      synchronizationInProgress: false,
      lastSynchronizationAt: null,
      lastSynchronizationFailed: false,
      pendingSynchronizationCount: 0,
      oldestPendingSynchronizationAt: null,
      lastSynchronizationError: null,
      catalogUpdatedAt: null,
    };
  }
  async synchronizeNow() {
    // Online mode reads authoritative server data and has no local catalog to synchronize.
  }
  synchronizationEvents(take = 100) {
    void take;
    // Online sales use authoritative server data and do not run the local
    // synchronization pipeline. Edge events become available after entering
    // the enrolled/offline workspace with its locally authenticated user.
    return Promise.resolve([]);
  }
  referenceOptions(catalogCode: string) {
    return referenceOptionsApi.list(catalogCode);
  }
  settlementConfiguration() {
    return request<PosSettlementConfiguration>("/api/commerce/v1/pos/settlement-configuration");
  }
  openCashDrawer() {
    return this.localEdge().openCashDrawer();
  }
  cashMovementReasons(direction: PosCashMovementDirection) {
    return request<PosCashMovementReason[]>(
      "/api/commerce/v1/work-sessions/cash-reasons?businessId=" +
      this.context.businessId + "&direction=" + direction,
    );
  }

  confirmCashMovement(input: PosCashMovementInput) {
    return request<PosCashMovementAcceptance>(
      "/api/commerce/v1/work-sessions/" +
      this.context.workSessionId + "/cash-movements",
      {
        method: "POST",
        headers: { "Idempotency-Key": input.documentId },
        body: JSON.stringify({
          ...input,
          businessId: this.context.businessId,
          workSessionId: this.context.workSessionId,
        }),
      },
    );
  }
  async printCashMovement(ticket: PosCashMovementTicket) {
    if (this.edgeSessionToken) return this.localEdge().printCashMovement(ticket);
    const branding = await tenantsApi.getBranding().catch(() => null);
    return printCashMovementTicket(cashMovementTicketHtml(
      ticket,
      branding?.displayName ?? branding?.legalName ?? "Empresa",
      this.context.businessName,
      this.context.warehouseName,
    ));
  }
  async printCashDenominationCount(ticket: import("./pos-edge-client").PosCashDenominationCount) {
    if (this.edgeSessionToken) return this.localEdge().printCashDenominationCount(ticket);
    return printCashDenominationCount(cashDenominationCountHtml(ticket));
  }


  async searchProducts(search = "", skip = 0, take = 50, customerId: string | null = null) {
    const page = await request<OnlineProductPage>(
      "/api/commerce/v1/pos/drafts/products/search",
      this.post({ context: this.scope(), search, skip, take, customerId }),
    );
    return page satisfies PosCatalogSearchPage;
  }

  productWarehouseAvailability(productId: string) {
    return request<PosProductWarehouseAvailability[]>(
      `/api/commerce/v1/pos/catalog/products/${productId}/warehouse-availability`,
    );
  }

  async searchCustomers(search = "", skip = 0, take = 50) {
    const page = await request<OnlineCustomerPage>(
      "/api/commerce/v1/pos/drafts/customers/search",
      this.post({ context: this.scope(), search, skip, take }),
    );
    return {
      ...page,
      items: page.items.map(mapCustomer),
    } satisfies PosCustomerSearchPage;
  }

  async customerCountries() {
    return request<PosCountry[]>("/api/commerce/v1/masters/geography/countries");
  }

  async customerDivisions(countryId: string) {
    return request<PosAdministrativeDivision[]>(`/api/commerce/v1/masters/geography/countries/${countryId}/divisions`);
  }

  async customerCities(divisionId: string) {
    return request<PosCity[]>(`/api/commerce/v1/masters/geography/divisions/${divisionId}/cities`);
  }

  async createCustomer(input: PosCreateCustomerInput) {
    const created = await request<{
      customerId: string; identification: string; displayName: string;
      priceChannelId: string | null; requiresElectronicInvoice: boolean; isActive: boolean;
    }>("/api/commerce/v1/customers", this.post({
      operationId: crypto.randomUUID(),
      businessId: this.context.businessId,
      party: {
        partyType: input.partyType,
        identificationCountryId: input.identificationCountryId,
        identificationTypeCode: input.identificationTypeCode,
        identification: input.identification,
        verificationDigit: input.verificationDigit,
        displayName: input.displayName,
        legalName: input.legalName,
        firstName: input.firstName,
        lastName: input.lastName,
        email: input.email,
        phone: input.phone,
      },
      primarySite: input.primarySite,
      pricing: null,
    }));
    return {
      customerId: created.customerId,
      identification: created.identification,
      name: created.displayName,
      priceChannelId: created.priceChannelId,
      requiresElectronicInvoice: created.requiresElectronicInvoice,
      isActive: created.isActive,
    } satisfies PosCustomer;
  }
  async customer(customerId: string) {
    const customer = await request<OnlineCustomerPage["items"][number]>(
      "/api/commerce/v1/pos/drafts/customers/get",
      this.post({ context: this.scope(), customerId }),
    );
    return mapCustomer(customer);
  }

  createApproval(input: PosApprovalCreateInput): Promise<PosApprovalSummary> {
    return request<PosApprovalSummary>(
      "/api/commerce/v1/pos/approvals/",
      { method: "POST", body: JSON.stringify(input) },
    );
  }

  async activeDraft() {
    const draft = await request<OnlineDraft>(
      "/api/commerce/v1/pos/drafts/active",
      this.post({ context: this.scope() }),
    );
    return this.mapDraft(draft);
  }

  async nextNumbers(_documentType?: PosSaleDocumentType): Promise<PosNextNumbers | null> {
    void _documentType;
    return null;
  }

  async capture(value: string, _customerId: string | null) {
    void _customerId;
    return this.addProduct(value);
  }

  async captureSelectedProduct(
    product: PosCatalogProduct,
    _customerId: string | null,
  ) {
    void _customerId;
    return this.addProduct(product.productId);
  }

  private async addProduct(selector: string) {
    const draft = await this.ensureActive();
    try {
      const updated = await request<OnlineDraft>(
        `/api/commerce/v1/pos/drafts/${draft.draftId.value}/items`,
        this.mutation({
          selector,
          quantity: 1,
          expectedVersion: this.version(draft.draftId.value),
        }),
      );
      return {
        status: "Added",
        draft: this.mapDraft(updated),
      } satisfies PosCaptureResult;
    } catch (error) {
      if (
        error instanceof PosEdgeError &&
        error.status === 400 &&
        error.message.toLocaleLowerCase("es").includes("no se encontr")
      )
        return { status: "NotFound", draft } satisfies PosCaptureResult;
      const available = error instanceof PosEdgeError && error.status === 400
        ? inventoryAvailableFromProblem(error.message)
        : null;
      if (available !== null)
        return {
          status: "InsufficientInventory",
          draft,
          availability: {
            requestedQuantity: 1,
            availableQuantity: available,
            isAvailable: false,
          },
        } satisfies PosCaptureResult;
      throw error;
    }
  }

  async changeQuantity(draftId: string, lineId: string, quantity: number) {
    const current = await this.ensureActive();
    try {
      const updated = await request<OnlineDraft>(
        `/api/commerce/v1/pos/drafts/${draftId}/lines/${lineId}/quantity`,
        this.mutation(
          { quantity, expectedVersion: this.version(draftId) },
          "PUT",
        ),
      );
      return {
        status: "Added",
        draft: this.mapDraft(updated),
      } satisfies PosCaptureResult;
    } catch (error) {
      const available = error instanceof PosEdgeError && error.status === 400
        ? inventoryAvailableFromProblem(error.message)
        : null;
      if (available !== null)
        return {
          status: "InsufficientInventory",
          draft: current,
          availability: { requestedQuantity: quantity, availableQuantity: available, isAvailable: false },
        } satisfies PosCaptureResult;
      throw error;
    }
  }

  async setDiscount(draftId: string, lineId: string, discount: number, authorization?: PosSensitiveAuthorization) {
    return this.mapDraft(
      await request<OnlineDraft>(
        `/api/commerce/v1/pos/drafts/${draftId}/lines/${lineId}/discount`,
        this.mutation(
          { discount, expectedVersion: this.version(draftId) },
          "PUT",
          authorization?.operationId,
          authorization?.approvalRequestId,
        ),
      ),
    );
  }

  async selectCustomer(draftId: string, customerId: string | null) {
    const selection = await request<OnlineCustomerSelection>(
      `/api/commerce/v1/pos/drafts/${draftId}/customer`,
      this.mutation(
        { customerId, expectedVersion: this.version(draftId) },
        "PUT",
      ),
    );
    return {
      draft: this.mapDraft(selection.draft),
      customer: selection.customer ? mapCustomer(selection.customer) : null,
    } satisfies PosCustomerSelection;
  }

  async removeLine(draftId: string, lineId: string, authorization?: PosSensitiveAuthorization) {
    return this.mapDraft(
      await request<OnlineDraft>(
        `/api/commerce/v1/pos/drafts/${draftId}/lines/${lineId}/remove`,
        this.mutation(
          { expectedVersion: this.version(draftId) },
          "POST",
          authorization?.operationId,
          authorization?.approvalRequestId,
        ),
      ),
    );
  }

  async cancelDraft(draftId: string, authorization?: PosSensitiveAuthorization) {
    return this.mapDraft(
      await request<OnlineDraft>(
        `/api/commerce/v1/pos/drafts/${draftId}/reset`,
        this.mutation(
          { expectedVersion: this.version(draftId) },
          "POST",
          authorization?.operationId,
          authorization?.approvalRequestId,
        ),
      ),
    );
  }

  async saveTemporary(
    draftId: string,
    name: string,
    reference: string,
    observation: string,
  ) {
    return this.mapDraft(
      await request<OnlineDraft>(
        `/api/commerce/v1/pos/drafts/${draftId}/pause`,
        this.mutation({
          name,
          reference: reference || null,
          observation: observation || null,
          expectedVersion: this.version(draftId),
        }),
      ),
    );
  }

  async temporaries(search = "") {
    const drafts = await request<OnlineDraft[]>(
      "/api/commerce/v1/pos/drafts/temporaries/search",
      this.post({ context: this.scope(), search, skip: 0, take: 100 }),
    );
    return drafts.map((draft) => this.mapDraft(draft));
  }

  async deleteTemporary(draftId: string) {
    const active = await this.ensureActive();
    await request<OnlineDraft>(
      `/api/commerce/v1/pos/drafts/temporaries/${draftId}/remove`,
      this.mutation({ expectedVersion: this.version(draftId) }),
    );
    this.versions.delete(draftId);
    this.activeDraftId = active.draftId.value;
  }

  async recoverTemporary(draftId: string) {
    const active = await this.ensureActive();
    return this.mapDraft(
      await request<OnlineDraft>(
        `/api/commerce/v1/pos/drafts/temporaries/${draftId}/recover`,
        this.mutation({
          expectedTemporaryVersion: this.version(draftId),
          expectedActiveVersion: this.version(active.draftId.value),
        }),
      ),
    );
  }

  async completeSale(
    draftId: string,
    _customerIdentification: string | null,
    payments: PosPaymentInput[],
    documentType: PosSaleDocumentType,
    credit: PosCreditTerms | null = null,
    fiscalHabilitationOnly = false,
  ) {
    const printRoute = resolvePosReceiptPrintRoute(
      this.edgeSessionToken,
      fiscalHabilitationOnly,
    );
    const browserPreview = printRoute === "browser" ? openHalfLetterPrintPreview() : null;
    try {
      const result = await request<OnlineCheckoutResponse>(
        `/api/commerce/v1/pos/drafts/${draftId}/complete`,
        this.mutation({
          expectedVersion: this.version(draftId),
          payments, credit, documentType, fiscalHabilitationOnly,
        }, "POST", `online-sale-${draftId}`),
      );
      const nextDraft = this.mapDraft(result.nextDraft);
      if (!fiscalHabilitationOnly)
        await this.printDirect([result.receipt], !result.isDuplicate, "pos", browserPreview);
      return {
        issuedSale: {
          documentId: { value: result.receipt.documentId },
          documentNumber: result.receipt.documentNumber,
          fiscalNumber: result.receipt.fiscalNumber,
          cufe: result.receipt.cufe,
          qrPayload: result.receipt.qrPayload,
          total: result.receipt.payableAmount,
          outboxMessageId: "",
          wasAlreadyIssued: result.isDuplicate,
        },
        nextDraft,
        nextDocumentNumber: null,
        nextFiscalNumber: null,
        receipt: result.receipt,
        printPreviewOpened: printRoute === "browser",
        printedDirectly: printRoute === "installed-app",
      } satisfies PosCompleteSaleResult;
    } catch (error) {
      closePrintPreview(browserPreview);
      throw error;
    }
  }

  async searchIssuedSales(search = "", skip = 0, take = 50) {
    const page = await request<OnlineIssuedSalePage>(
      "/api/commerce/v1/pos/drafts/sales/search",
      this.post({ context: this.scope(), search, skip, take }),
    );
    return {
      items: page.items.map(
        (sale) =>
          ({
            documentId: { value: sale.documentId },
            documentType: sale.documentType,
            documentNumber: sale.documentNumber,
            fiscalNumber: sale.fiscalNumber,
            issuedAt: sale.issuedAt,
            total: sale.total,
            customerIdentification: sale.customerIdentification,
            customerName: sale.customerName,
            fiscalStatus: sale.fiscalStatus,
          }) satisfies PosIssuedSaleSummary,
      ),
      hasMore: page.hasMore,
      nextOffset: page.nextOffset,
    } satisfies PosIssuedSaleSearchPage;
  }

  async reprint(documentId: string) {
    const browserPreview = this.edgeSessionToken ? null : openHalfLetterPrintPreview();
    try {
      const receipt = await request<PosPrintableReceipt>(
        `/api/commerce/v1/pos/drafts/sales/${documentId}/receipt`,
        this.post(this.scope()),
      );
      await this.printDirect([receipt], false, "pos", browserPreview);
    } catch (error) {
      closePrintPreview(browserPreview);
      throw error;
    }
  }

  readScaleWeight() {
    return this.localEdge().readScaleWeight();
  }

  printerConfiguration() {
    return this.localEdge().printerConfiguration();
  }

  savePrinterConfiguration(configuration: PosPrinterConfiguration) {
    return this.localEdge().savePrinterConfiguration(configuration);
  }

  async previewWorkSessionClosure(
    draftId: string,
    authorization?: PosSensitiveAuthorization,
  ): Promise<PosAuthorizedClosurePreview> {
    const requestDefinition = workSessionClosurePreviewRequest(
      this.context.workSessionId,
      draftId,
      authorization?.approvalRequestId,
      authorization?.operationId,
    );
    const preview = await request<PosAuthorizedClosurePreview["preview"]>(
      requestDefinition.path,
      requestDefinition.init,
    );
    return {
      authorizationToken: authorization?.operationId ?? crypto.randomUUID(),
      preview,
    };
  }

  validateDraftInventory(draftId: string) {
    return request<import("./pos-edge-client").PosInventoryValidation>(
      `/api/commerce/v1/pos/drafts/${draftId}/inventory-validation`,
    );
  }

  previewSettlement(draftId: string) {
    return request<import("./pos-edge-client").PosSaleSettlement>(
      `/api/commerce/v1/pos/drafts/${draftId}/settlement`,
    );
  }

  async updateLines(draftId: string, lines: PosDraftLineUpdate[], authorization?: PosSensitiveAuthorization) {
    return this.mapDraft(
      await request<OnlineDraft>(
        `/api/commerce/v1/pos/drafts/${draftId}/lines`,
        this.mutation(
          { lines, expectedVersion: this.version(draftId) },
          "PUT",
          authorization?.operationId,
          authorization?.approvalRequestId,
        ),
      ),
    );
  }

  approval(approvalRequestId: string): Promise<PosApprovalSummary> {
    return request<PosApprovalSummary>(
      `/api/commerce/v1/pos/approvals/${encodeURIComponent(approvalRequestId)}`,
    );
  }

  async closeWorkSession(input: PosCloseWorkSessionInput): Promise<PosWorkSessionClosure> {
    const requestDefinition = workSessionCloseRequest(
      this.context.workSessionId, input.operationId, input.draftId,
      input.authorization?.approvalRequestId, input.countedCash,
      input.paymentCounts, input.note);
    const [closure, branding] = await Promise.all([
      request<PosWorkSessionClosure>(requestDefinition.path, requestDefinition.init),
      tenantsApi.getBranding().catch(() => null),
    ]);
    const printableClosure = {
      ...closure,
      companyName: branding?.displayName ?? branding?.legalName ?? closure.businessName,
      logoUrl: branding?.logoUrl ?? null,
    };
    if (this.edgeSessionToken) {
      try {
        await this.localEdge().printWorkSessionClosure(printableClosure);
      } catch {
        await printWorkSessionClosure(workSessionClosureHtml(printableClosure))
          .catch(() => undefined);
      }
    } else {
      await printWorkSessionClosure(workSessionClosureHtml(printableClosure))
        .catch(() => undefined);
    }
    return closure;
  }


  orders(filters: CommerceOrderFilters) {
    return loadCommerceOrders(filters);
  }

  order(orderId: string) {
    return loadCommerceOrder(orderId);
  }

  async recoverOrder(orderId: string) {
    const draft = await this.ensureActive();
    await recoverCommerceOrder(orderId, {
      workSessionId: this.context.workSessionId,
      userId: this.userId,
      draftId: draft.draftId.value,
      expectedDraftVersion: this.version(draft.draftId.value),
    });
    return this.activeDraft();
  }

  renewRecoveredOrder(orderId: string) {
    return renewCommerceOrderClaim(
      orderId,
      this.context.workSessionId,
      this.userId,
    );
  }

  releaseRecoveredOrder(orderId: string) {
    return releaseCommerceOrderClaim(
      orderId,
      this.context.workSessionId,
      this.userId,
    );
  }

  async saveOrder(draft: PosDraft): Promise<{
    order: SellerOrderResult;
    nextDraft: PosDraft;
  }> {
    const idempotencyKey = `pos-order-${draft.draftId.value}-${this.version(draft.draftId.value)}`;
    const order = await savePosDraftAsOrder(this.context, draft, idempotencyKey);

    try {
      return { order, nextDraft: await this.cancelDraft(draft.draftId.value) };
    } catch (cleanupError) {
      if (draft.sourceOrderId)
        await this.releaseRecoveredOrder(draft.sourceOrderId).catch(() => undefined);
      throw new Error(
        `El pedido ${order.orderNumber} se guardó, pero no fue posible limpiar la venta activa: ${
          cleanupError instanceof Error ? cleanupError.message : "error desconocido"
        }`,
      );
    }
  }

  async invoiceOrders(
    orderIds: string[],
    paymentMethodCode: string,
    documentType: "SalesInvoice" | "SalesReceipt",
    paymentReference?: string | null,
    bankAccountId?: string | null,
    paymentNotes?: string | null,
  ): Promise<InvoiceOrdersResponse> {
    const printRoute = resolvePosOrderPrintRoute(this.edgeSessionToken);
    const browserPreview = printRoute === "browser"
      ? openHalfLetterPrintPreview()
      : null;
    const response = await invoiceCommerceOrders({
      workSessionId: this.context.workSessionId,
      warehouseId: this.context.warehouseId,
      userId: this.userId,
      orderIds,
      paymentMethodCode,
      paymentReference: paymentReference ?? null,
      bankAccountId: bankAccountId ?? null,
      paymentNotes: paymentNotes ?? null,
      documentType,
    });
    try {
      const receipts = orderReceiptsFromEmission(response.results);
      await this.printDirect(receipts, receipts.length > 0, "orders", browserPreview);
      response.printStatus = response.completedCount ? "Sent" : "NotRequired";
    } catch (error) {
      closePrintPreview(browserPreview);
      response.printStatus = "Failed";
      response.printError = `Los pedidos se facturaron, pero no fue posible imprimir: ${
        error instanceof Error ? error.message : "error desconocido"
      }`;
    }
    await this.activeDraft();
    return response;
  }
  private scope() {
    return {
      businessId: this.context.businessId,
      warehouseId: this.context.warehouseId,
      workSessionId: this.context.workSessionId,
    };
  }

  private post(body: unknown): RequestInit {
    return { method: "POST", body: JSON.stringify(body) };
  }

  private mutation(
    body: unknown,
    method = "POST",
    idempotencyKey = crypto.randomUUID(),
    approvalRequestId?: string,
  ): RequestInit {
    return {
      method,
      body: JSON.stringify(body),
      headers: {
        "Idempotency-Key": idempotencyKey,
        ...(approvalRequestId
          ? { "X-Auraly-Approval-Id": approvalRequestId }
          : {}),
      },
    };
  }

  private async ensureActive() {
    if (!this.activeDraftId) return this.activeDraft();
    return {
      draftId: { value: this.activeDraftId },
    } as PosDraft;
  }

  private version(draftId: string) {
    const version = this.versions.get(draftId);
    if (version === undefined)
      throw new PosEdgeError(
        "La versión de la venta no está disponible. Recarga el módulo de facturación.",
        409,
      );
    return version;
  }

  private mapDraft(draft: OnlineDraft): PosDraft {
    this.versions.set(draft.draftId, draft.version);
    if (draft.status === "Active")
      this.activeDraftId = draft.draftId;
    return {
      draftId: { value: draft.draftId },
      customerId: draft.customerId,
      sellerId: draft.sellerId,
      status: draft.status,
      name: draft.name,
      reference: draft.reference,
      observation: draft.observation,
      lines: draft.lines.map(mapLine),
      untaxedAmount: draft.untaxedAmount,
      taxAmount: draft.taxAmount,
      payableAmount: draft.payableAmount,
      sourceOrderId: draft.sourceOrderId,
    };
  }

  private qrImageUrl(documentId: string) {
    const query = new URLSearchParams(this.scope());
    return `/api/commerce/v1/pos/drafts/sales/${documentId}/qr?${query}`;
  }
}

function mapLine(line: OnlineDraftLine): PosDraftLine {
  return {
    ...line,
    productId: { value: line.productId },
  };
}

function mapCustomer(
  customer: OnlineCustomerPage["items"][number],
): PosCustomer {
  return { ...customer, isActive: true };
}

export function salesWorkspaceKey(businessId: string, warehouseId: string) {
  return businessId + ":" + warehouseId;
}

async function request<T>(
  path: string,
  init: RequestInit = {},
): Promise<T> {
  const tenantId =
    typeof window === "undefined"
      ? null
      : window.localStorage.getItem("selected_tenant_id");
  const businessId =
    typeof window === "undefined"
      ? null
      : window.localStorage.getItem("selected_business_id");
  const response = await fetchWithSessionRetry(path, {
    ...init,
    cache: "no-store",
    credentials: "include",
    headers: {
      "Content-Type": "application/json",
      ...(tenantId ? { "X-Tenant-Id": tenantId } : {}),
      ...(businessId ? { "X-Business-Id": businessId } : {}),
      ...init.headers,
    },
  });
  if (!response.ok) {
    const raw = await response.text();
    let detail = raw || response.statusText;
    try {
      const problem = JSON.parse(raw) as {
        detail?: string;
        message?: string;
        title?: string;
        code?: string;
      };
      detail =
        problem.detail || problem.message || problem.title || detail;
      throw new PosEdgeError(detail, response.status, problem.code || problem.title);
    } catch (parsed) {
      if (parsed instanceof PosEdgeError) throw parsed;
      // Preserve the server text when the response is not JSON.
    }
    throw new PosEdgeError(detail, response.status);
  }
  return (await response.json()) as T;
}

const browserPrintFrames = new WeakMap<Window, HTMLIFrameElement>();

export function openHalfLetterPrintPreview(): Window | null {
  const frame = document.createElement("iframe");
  frame.setAttribute("aria-hidden", "true");
  frame.style.position = "fixed";
  frame.style.left = "-10000px";
  frame.style.top = "0";
  frame.style.width = "1px";
  frame.style.height = "1px";
  frame.style.border = "0";
  frame.style.opacity = "0";
  document.body.appendChild(frame);
  const target = frame.contentWindow;
  if (!target) {
    frame.remove();
    return null;
  }
  browserPrintFrames.set(target, frame);
  target.addEventListener("afterprint", () => closePrintPreview(target), { once: true });
  return target;
}

export function closePrintPreview(preview: Window | null): void {
  if (!preview) return;
  const frame = browserPrintFrames.get(preview);
  if (frame) {
    browserPrintFrames.delete(preview);
    frame.remove();
    return;
  }
  preview.close();
}

export async function renderInvoiceOrdersReceipt(
  preview: Window | null,
  response: InvoiceOrdersResponse,
  context: ReceiptRenderContext,
) {
  const documentIds = response.results.flatMap((result) =>
    result.documentId && !result.error ? [result.documentId] : []);
  if (!documentIds.length) { closePrintPreview(preview); return; }
  if (!preview) throw new Error("El navegador bloqueó la vista previa de impresión.");
  const receipts = await Promise.all(documentIds.map((documentId) =>
    request<PosPrintableReceipt>(
      `/api/commerce/v1/pos/drafts/sales/${documentId}/receipt`,
      { method: "POST", body: JSON.stringify(context) },
    )));
  await renderReceiptsReceipt(preview, receipts, context);
}

export async function renderReceiptsReceipt(
  preview: Window | null,
  receipts: PosPrintableReceipt[],
  context: ReceiptRenderContext,
  paperWidthMillimeters = 80,
) {
  if (!receipts.length) { closePrintPreview(preview); return; }
  if (!preview) throw new Error("El navegador bloqueó la vista previa de impresión.");
  const paperWidth = paperWidthMillimeters === 58 ? 58 : 80;
  const bodyWidth = paperWidth - 8;
  const currency = new Intl.NumberFormat("es-CO", {
    style: "currency", currency: "COP", maximumFractionDigits: 0,
  });
  const branding = await tenantsApi.getBranding().catch(() => null);
  const location = [
    context.businessName ? `Sede: ${context.businessName}` : "",
    context.warehouseName ?? "",
  ].filter(Boolean).join(" · ");
  const documents = receipts.map((receipt) => {
    const presentation = salesPrintPresentation(receipt);
    const brand = receiptBrandMarkup(branding ?? {
      displayName: receipt.companyName ?? "Empresa",
      legalName: null,
      logoUrl: receipt.companyLogoSource ?? null,
    });
    const lines = receipt.lines.map((line) => `<div class="line"><b>${escapeHtml(line.description)}</b><div><span>${line.quantity} × ${currency.format(line.unitPrice)}</span><b>${currency.format(line.total)}</b></div></div>`).join("");
    const taxes = receiptTaxTableRows(receipt, currency);
    const payments = receiptPaymentRows(receipt, currency, "div");
    const withholdings = receiptWithholdingRows(receipt, currency, "div");
    const withholdingTotals = receipt.withholdingTotal > 0
      ? `<div><span>Total bruto</span><b>${currency.format(receipt.payableAmount)}</b></div><h3>Retenciones</h3>${withholdings}<div><span>Total retenciones</span><b>-${currency.format(receipt.withholdingTotal)}</b></div>`
      : "";
    const netPayable = presentation.netPayable;
    const qr = receipt.documentType === "SalesInvoice"
      ? `<img class="qr" src="${window.location.origin}/api/commerce/v1/pos/drafts/sales/${receipt.documentId}/qr?businessId=${context.businessId}&warehouseId=${context.warehouseId}&workSessionId=${context.workSessionId}" alt="QR DIAN">` : "";
    return `<article><header>${brand}<h2>${presentation.title}</h2><b>${escapeHtml(presentation.displayNumber)}</b><br>${presentation.issuedAt}${location ? `<p class="scope">${escapeHtml(location)}</p>` : ""}</header><section class="meta"><div><span>Cliente</span><b>${escapeHtml(receipt.customerName)}</b></div><div><span>Identificación</span><b>${escapeHtml(receipt.customerIdentification)}</b></div></section>${lines}<section class="totals"><div><span>Subtotal</span><b>${currency.format(receipt.untaxedAmount)}</b></div><h3>Impuestos por tarifa</h3><table class="tax-table"><thead><tr><th>Impuesto</th><th>Base</th><th>Valor</th></tr></thead><tbody>${taxes}</tbody></table><div><span>Total impuestos</span><b>${currency.format(receipt.taxAmount)}</b></div>${withholdingTotals}<div class="total"><span>Total</span><b>${currency.format(netPayable)}</b></div><h3>Medios de pago</h3>${payments}</section>${presentation.isInvoice && receipt.cufe ? `<p class="cufe"><b>CUFE</b><br>${escapeHtml(receipt.cufe)}</p>` : ""}${qr}<footer>${presentation.issuedBy}<br><b>www.auralyapp.co</b></footer></article>`;
  }).join("");
  preview.document.open();
  preview.document.write(`<!doctype html><html lang="es"><head><meta charset="utf-8"><title>Comprobantes de venta</title><style>@page{size:${paperWidth}mm auto;margin:4mm}*{box-sizing:border-box}${posReceiptTypographyCss}body{width:${bodyWidth}mm;margin:0 auto;color:#111;font:12px/1.35 ui-monospace,Consolas,monospace}article{page-break-after:always}article:last-child{page-break-after:auto}header{border-bottom:1px dashed #555;padding-bottom:8px}.brand-logo{display:block;max-width:48mm;max-height:18mm;margin:0 auto 3mm;object-fit:contain}.brand-name{margin:0;font:800 20px/1.2 Arial,sans-serif;text-transform:uppercase}h2{margin:6px 0 3px;font-size:13px;text-transform:uppercase}.scope{margin:3px 0 0;color:#444}.meta,.totals{padding:8px 0;border-bottom:1px dashed #555}.meta div,.totals div,.line div{display:flex;justify-content:space-between;gap:10px}.meta b,.totals b,.line b{font-variant-numeric:tabular-nums;text-align:right}.line{padding:8px 0;border-bottom:1px dashed #aaa}.line>b{display:block;text-align:left}.tax-table{width:100%;border-collapse:collapse;margin:4px 0}.tax-table th{padding:3px 0;border-bottom:1px solid #777;text-align:right;font-size:10px}.tax-table th:first-child,.tax-table td:first-child{text-align:left}.tax-table td{padding:3px 0;text-align:right;font-variant-numeric:tabular-nums}.total{margin-top:7px;padding:5px 0;font-size:18px;font-weight:900}.cufe{overflow-wrap:anywhere;font-size:9px}.qr{display:block;width:42mm;height:42mm;margin:9px auto 4px}footer{padding-top:7px;text-align:center}h3{margin:9px 0 4px;font-size:11px;text-transform:uppercase}</style></head><body>${documents}<script>addEventListener('load',()=>setTimeout(()=>window.print(),150));</script></body></html>`);
  preview.document.close();
}

export async function renderInvoiceOrdersHalfLetter(
  preview: Window | null,
  response: InvoiceOrdersResponse,
  context: ReceiptRenderContext,
) {
  const documentIds = response.results.flatMap((result) =>
    result.documentId && !result.error ? [result.documentId] : []);
  if (!documentIds.length) {
    closePrintPreview(preview);
    return;
  }
  if (!preview)
    throw new Error("El navegador bloqueó la vista previa de impresión.");
  const receipts = await Promise.all(documentIds.map((documentId) =>
    request<PosPrintableReceipt>(
      `/api/commerce/v1/pos/drafts/sales/${documentId}/receipt`,
      {
        method: "POST",
        body: JSON.stringify(context),
      },
    )));
  await renderReceiptsHalfLetter(preview, receipts, context);
}

export async function renderReceiptsHalfLetter(
  preview: Window | null,
  receipts: PosPrintableReceipt[],
  context: ReceiptRenderContext,
  format: Exclude<PosPrintTemplateFormat, "Receipt"> = "HalfLetter",
) {
  if (!receipts.length) { closePrintPreview(preview); return; }
  if (!preview) throw new Error("El navegador bloqueó la vista previa de impresión.");
  const currency = new Intl.NumberFormat("es-CO", {
    style: "currency", currency: "COP", maximumFractionDigits: 0,
  });
  const branding = await tenantsApi.getBranding().catch(() => null);
  const pages = receipts.map((receipt) => {
    const presentation = salesPrintPresentation(receipt);
    const brand = receiptBrandMarkup(branding ?? {
      displayName: receipt.companyName ?? "Empresa",
      legalName: null,
      logoUrl: receipt.companyLogoSource ?? null,
    });
    const isInvoice = presentation.isInvoice;
    const rows = receipt.lines.map((line) => `<tr><td>${escapeHtml(line.description)}</td><td class="n">${line.quantity}</td><td class="n">${currency.format(line.unitPrice)}</td><td class="n">${currency.format(line.total)}</td></tr>`).join("");
    const qr = receipt.documentType === "SalesInvoice"
      ? `<img class="qr" src="${window.location.origin}/api/commerce/v1/pos/drafts/sales/${receipt.documentId}/qr?businessId=${context.businessId}&warehouseId=${context.warehouseId}&workSessionId=${context.workSessionId}" alt="QR DIAN">`
      : "";
    const fiscal = isInvoice && receipt.fiscalNumber
      ? `<div><span>Número DIAN</span><b>${escapeHtml(receipt.fiscalNumber)}</b></div>`
      : "";
    const cufe = isInvoice && receipt.cufe
      ? `<p class="cufe"><b>CUFE</b><br>${escapeHtml(receipt.cufe)}</p>`
      : "";
    const taxes = receiptTaxRows(receipt, currency, "div");
    const payments = receiptPaymentRows(receipt, currency, "div");
    const withholdings = receiptWithholdingRows(receipt, currency, "div");
    const withholdingTotals = receipt.withholdingTotal > 0
      ? `${withholdings}<div><span>Total retenciones</span><b>-${currency.format(receipt.withholdingTotal)}</b></div>`
      : "";
    const netPayable = presentation.netPayable;
    const issuedAt = presentation.issuedAt;
    const copy = `<article class="document"><div class="document-content"><header><div>${brand}<h2>${presentation.title}</h2></div><div class="right"><span>N.º de ticket</span><br><b>${escapeHtml(receipt.documentNumber)}</b><br>${issuedAt}</div></header><section class="meta"><div><span>Cliente</span><b>${escapeHtml(receipt.customerName)}</b></div><div><span>Identificación</span><b>${escapeHtml(receipt.customerIdentification)}</b></div>${fiscal}</section><table><thead><tr><th>Producto</th><th class="n">Cant.</th><th class="n">Precio</th><th class="n">Total</th></tr></thead><tbody>${rows}</tbody></table><section class="bottom"><div>${cufe}<section class="breakdowns"><div class="breakdown"><b>Impuestos por tarifa</b>${taxes}</div><div class="breakdown"><b>Medios de pago</b>${payments}</div></section><small>Representación gráfica · copia cliente / control</small></div><div class="totals"><div><span>Subtotal</span><b>${currency.format(receipt.untaxedAmount)}</b></div><div><span>Total impuestos</span><b>${currency.format(receipt.taxAmount)}</b></div><div><span>Total bruto</span><b>${currency.format(receipt.payableAmount)}</b></div>${withholdingTotals}<div class="total"><span>Total a pagar</span><b>${currency.format(netPayable)}</b></div>${qr}</div></section><footer><span>${presentation.representationName}</span><span class="platform">${presentation.issuedBy} · <b>www.auralyapp.co</b><br>Emitido: ${issuedAt}</span><span class="page">Página 1 de 1</span></footer></div></article>`;
    const sheetClass = format === "Letter"
      ? "letter"
      : format === "HalfLegal" ? "half half-oficio" : "half half-letter";
    const copies = format === "Letter"
      ? `<div class="copy">${copy}</div>`
      : `<div class="copy">${copy}</div><div class="copy">${copy}</div>`;
    return `<section class="sheet ${sheetClass}">${copies}</section>`;
  }).join("");
  const pageSize = format === "HalfLegal" ? "215.9mm 330.2mm" : "Letter portrait";
  preview.document.open();
  preview.document.write(`<!doctype html><html lang="es"><head><meta charset="utf-8"><title>Comprobantes de venta</title><style>
    @page{size:${pageSize};margin:0}*{box-sizing:border-box}html,body{margin:0;color:#07111f;font-family:Arial,sans-serif}.sheet{width:215.9mm;page-break-after:always;position:relative;overflow:hidden;background:#fff}.sheet:last-child{page-break-after:auto}.half-letter{height:279.4mm;--copy-height:139.7mm;--document-width:129.7mm}.half-oficio{height:330.2mm;--copy-height:165.1mm;--document-width:155.1mm}.letter{height:279.4mm}.copy{position:relative;overflow:hidden}.half .copy{width:215.9mm;height:var(--copy-height)}.half .document{position:absolute;left:50%;top:50%;width:var(--document-width);height:203.9mm;transform:translate(-50%,-50%) rotate(90deg);transform-origin:center;padding:5mm 6mm 4mm}.letter .copy{width:100%;height:100%;padding:12mm 13mm 10mm}.letter .document{width:100%;height:100%}.document{font-size:8pt;line-height:1.22}.document-content{min-height:100%;display:flex;flex-direction:column;transform-origin:top left}header{display:grid;grid-template-columns:1fr auto;gap:5mm;border-bottom:.25mm solid #0f766e;padding-bottom:1.7mm}.brand-logo{display:block;max-width:28mm;max-height:13mm;object-fit:contain}.brand-name{margin:0;font-size:13pt;font-weight:500;color:#065f5b}h2{margin:.8mm 0 0;font-size:8.5pt}.right{text-align:right;white-space:nowrap}.meta{display:grid;grid-template-columns:1.2fr 1fr;gap:.8mm 4mm;margin:1.5mm 0 1mm}.meta div,.totals div,.breakdown div{display:flex;justify-content:space-between;gap:2.5mm}.meta span,.totals span,.breakdown span{color:#475569}table{width:100%;border-collapse:collapse;margin-top:1mm}th{padding:1.1mm;background:#eef8f7;text-align:left;font-size:7pt}td{padding:1mm 1.1mm;border-bottom:.2mm solid #e2e8f0}.n{text-align:right;white-space:nowrap}.bottom{display:grid;grid-template-columns:minmax(0,1fr) 41mm;gap:4mm;margin-top:2mm}.breakdowns{display:grid;grid-template-columns:1fr 1fr;gap:3mm;margin-top:1.5mm}.breakdown{min-width:0}.breakdown>b{display:block;margin-bottom:.7mm;color:#065f5b}.breakdown div{padding:.25mm 0;font-size:6.7pt}.totals{border:.2mm solid #cbd5e1;border-radius:2mm;padding:2mm}.total{font-size:10pt;color:#065f5b}.cufe{overflow-wrap:anywhere;font-size:6.2pt}.qr{display:block;width:25mm;height:25mm;margin:1mm auto 0}small{color:#64748b;font-size:6.2pt}footer{display:grid;grid-template-columns:1fr auto auto;align-items:end;gap:3mm;margin-top:auto;padding-top:1.5mm;border-top:.2mm solid #94a3b8;color:#64748b;font-size:6.2pt}.platform{text-align:center;color:#334155}.platform b{color:#065f5b}.page{white-space:nowrap;text-align:right}.letter .document{font-size:9pt}.letter .brand-name{font-size:16pt}.letter h2{font-size:10pt}.letter .meta{margin-top:2.5mm}.letter th{font-size:8pt}.letter td{padding-top:1.6mm;padding-bottom:1.6mm}.letter .bottom{margin-top:4mm;grid-template-columns:minmax(0,1fr) 49mm}.letter .breakdown div{font-size:7.5pt}.letter .cufe,.letter small,.letter footer{font-size:7pt}.letter .qr{width:33mm;height:33mm}@media screen{body{background:#e2e8f0}.sheet{margin:8mm auto;box-shadow:0 4px 24px #0f172a33}}
  </style></head><body>${pages}<script>addEventListener('load',()=>{for(const documentElement of document.querySelectorAll('.document')){const content=documentElement.querySelector('.document-content');const available=documentElement.clientHeight;if(content.scrollHeight>available){const scale=Math.max(.58,available/content.scrollHeight);content.style.transform='scale('+scale+')';content.style.width=(100/scale)+'%'}}setTimeout(()=>window.print(),150)});</script></body></html>`);
  preview.document.close();
}

function escapeHtml(value: string | null) {
  return (value ?? "")
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#39;");
}

function inventoryAvailableFromProblem(message: string) {
  if (!message.toLocaleLowerCase("es-CO").includes("inventario insuficiente")) return null;
  const match = message.match(/Disponible:\s*(-?\d+(?:\.\d+)?)/i);
  if (!match) return null;
  const value = Number(match[1]);
  return Number.isFinite(value) ? value : null;
}

function receiptTaxRows(receipt: PosPrintableReceipt, currency: Intl.NumberFormat, element: "div") {
  return receiptTaxGroups(receipt)
    .map(item => `<${element}><span>${escapeHtml(taxName(item.code))} ${item.rate.toLocaleString("es-CO", { maximumFractionDigits: 2 })}% · base ${currency.format(item.base)}</span><b>${currency.format(item.tax)}</b></${element}>`)
    .join("");
}

function receiptTaxTableRows(receipt: PosPrintableReceipt, currency: Intl.NumberFormat) {
  return receiptTaxGroups(receipt)
    .map(item => `<tr><td>${escapeHtml(taxName(item.code))} ${item.rate.toLocaleString("es-CO", { maximumFractionDigits: 2 })}%</td><td>${currency.format(item.base)}</td><td>${currency.format(item.tax)}</td></tr>`)
    .join("");
}

function receiptTaxGroups(receipt: PosPrintableReceipt) {
  const groups = new Map<string, { code: string; rate: number; base: number; tax: number }>();
  for (const line of receipt.lines) {
    const key = `${line.taxCode}:${line.taxRate}`;
    const current = groups.get(key) ?? { code: line.taxCode, rate: line.taxRate, base: 0, tax: 0 };
    current.base += line.total - line.tax;
    current.tax += line.tax;
    groups.set(key, current);
  }

  return [...groups.values()]
    .sort((left, right) => left.code.localeCompare(right.code) || left.rate - right.rate);
}

function receiptPaymentRows(receipt: PosPrintableReceipt, currency: Intl.NumberFormat, element: "div") {
  return receipt.payments.map(payment => `<${element}><span>${escapeHtml(paymentMethodName(payment.methodCode))}</span><b>${currency.format(payment.amount)}</b></${element}>`).join("");
}

function receiptWithholdingRows(receipt: PosPrintableReceipt, currency: Intl.NumberFormat, element: "div") {
  return (receipt.withholdings ?? []).map(withholding =>
    `<${element}><span>Ret. ${escapeHtml(withholding.name)} (${withholding.rate.toLocaleString("es-CO", { maximumFractionDigits: 4 })}%)</span><b>-${currency.format(withholding.amount)}</b></${element}>`,
  ).join("");
}

function taxName(code: string) {
  return ({ "01": "IVA", "02": "IC", "03": "ICA", "04": "INC" } as Record<string, string>)[code] ?? code;
}

function paymentMethodName(code: string) {
  return ({ Cash: "Efectivo", Card: "Tarjeta", DebitCard: "Tarjeta débito", CreditCard: "Tarjeta crédito", Transfer: "Transferencia", Credit: "Crédito / cartera", Voucher: "Bono / vale", Check: "Cheque", Withholding: "Retención" } as Record<string, string>)[code] ?? code;
}

/** One sales-document definition; receipt and sheet sizes only arrange it. */
function salesPrintPresentation(receipt: PosPrintableReceipt) {
  const isInvoice = receipt.documentType === "SalesInvoice";
  return {
    isInvoice,
    title: isInvoice ? "Factura electrónica de venta" : "Comprobante de venta",
    displayNumber: isInvoice && receipt.fiscalNumber
      ? receipt.fiscalNumber
      : receipt.documentNumber,
    representationName: isInvoice
      ? "Representación gráfica de factura electrónica"
      : "Representación gráfica del comprobante de venta",
    issuedBy: isInvoice ? "Factura emitida por Auraly" : "Comprobante emitido por Auraly",
    issuedAt: new Date(receipt.issuedAt).toLocaleString("es-CO"),
    netPayable: receipt.withholdingTotal > 0
      ? receipt.netPayableAmount
      : receipt.payableAmount,
  };
}
