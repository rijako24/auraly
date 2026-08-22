import {
  invoiceCommerceOrders,
  loadCommerceOrder,
  loadCommerceOrders,
  recoverCommerceOrder,
  type CommerceOrderFilters,
  type InvoiceOrdersResponse,
} from "@/services/orders/commerce-orders-client";

import {
  PosCaptureResult,
  PosCatalogProduct,
  PosCatalogSearchPage,
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
  PosEdgeError,
  PosEdgeClient,
  readEdgeUserSession,
  loadBrowserPrinterConfiguration,
  PosIssuedSaleSearchPage,
  PosIssuedSaleSummary,
  PosNextNumbers,
  PosPaymentInput,
  PosPrintableReceipt,
  type PosCashMovementAcceptance,
  type PosCashMovementDirection,
  type PosCashMovementInput,
  type PosCashMovementReason,
  type PosCloseWorkSessionInput,
  type PosWorkSessionClosure,
  type PosAuthorizedClosurePreview,
  PosSensitiveAuthorization,
  PosApprovalCreateInput,
  PosApprovalSummary,
} from "./pos-edge-client";
import { posApprovalClient } from "./pos-approval-client";
import { calculateReceiptRetailUnitPrice } from "@/app/(pos)/pos/pos-retail-price";

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
    priceListId: string | null;
    priceChannelId: string | null;
    requiresElectronicInvoice: boolean;
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

const WORKSPACE_STORAGE_KEY = "auraly.pos.sales-workspace";
const WORKSPACE_OFFLINE_STORE = "seller-workspaces";

// Survives client-side navigation only. The server remains authoritative for
// authorization, workspace and every POS operation.
let activeOnlinePosClient: OnlinePosClient | null = null;

export function rememberOnlinePosClient(client: OnlinePosClient): void {
  activeOnlinePosClient = client;
}

export function recalledOnlinePosClient(): OnlinePosClient | null {
  return activeOnlinePosClient;
}

export async function loadSalesWorkspaceOptions(): Promise<SalesWorkspaceOption[]> {
  try {
    const values = await request<SalesWorkspaceOption[]>(
      "/api/commerce/v1/pos/workspace/options",
    );
    const { openSalesOfflineDatabase } = await import("@/lib/sales-offline-database");
    const database = await openSalesOfflineDatabase();
    try {
      await new Promise<void>((resolve, reject) => {
        const operation = database.transaction(WORKSPACE_OFFLINE_STORE, "readwrite").objectStore(WORKSPACE_OFFLINE_STORE).put({ key: "available", values, updatedAt: new Date().toISOString() });
        operation.onsuccess = () => resolve();
        operation.onerror = () => reject(operation.error);
      });
    } finally { database.close(); }
    return values;
  } catch (error) {
    const { openSalesOfflineDatabase } = await import("@/lib/sales-offline-database");
    const database = await openSalesOfflineDatabase();
    try {
      const cached = await new Promise<{ values: SalesWorkspaceOption[] } | undefined>((resolve, reject) => {
        const operation = database.transaction(WORKSPACE_OFFLINE_STORE, "readonly").objectStore(WORKSPACE_OFFLINE_STORE).get("available");
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
): Promise<SalesWorkspaceContext> {
  const selected = await request<Omit<SalesWorkspaceContext, "workSessionId">>(
    "/api/commerce/v1/pos/workspace/select",
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
  window.localStorage.setItem(
    WORKSPACE_STORAGE_KEY,
    salesWorkspaceKey(selected.businessId, selected.warehouseId),
  );
  return { ...selected, workSessionId: session.workSessionId };
}
export function rememberSalesWorkspace(option: Pick<SalesWorkspaceOption,"businessId"|"warehouseId">): void {
  try {
    window.localStorage.setItem(
      WORKSPACE_STORAGE_KEY,
      salesWorkspaceKey(option.businessId, option.warehouseId),
    );
  } catch { /* IndexedDB remains the durable offline source. */ }
}
export function rememberedSalesWorkspaceKey(): string | null {
  try {
    return window.localStorage.getItem(WORKSPACE_STORAGE_KEY);
  } catch {
    return null;
  }
}

export function forgetSalesWorkspace(): void {
  activeOnlinePosClient = null;
  try {
    window.localStorage.removeItem(WORKSPACE_STORAGE_KEY);
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

  private localEdge() {
    if (!this.edgeSessionToken)
      throw new PosEdgeError("Esta operación requiere configurar este equipo como caja Auraly.", 409);
    return new PosEdgeClient(this.edgeSessionToken, readEdgeUserSession());
  }

  private async printDirect(
    receipts: PosPrintableReceipt[],
    openDrawer = false,
  ) {
    const edge = this.localEdge();
    const jobs = receipts.map((receipt) => edge.printReceipt(receipt));
    if (openDrawer) jobs.push(edge.openCashDrawer());
    await Promise.all(jobs);
  }

  async health() {
    await request<{ status: string }>("/api/health");
    return {
      status: "ok",
      serverConnected: true,
      deviceSeriesCode: "00",
      businessId: this.context.businessId,
      businessName: this.context.businessName,
      warehouseName: this.context.warehouseName,
      userDisplayName: this.userDisplayName,
      userId: this.userId,
      workSessionId: this.context.workSessionId,
      fiscalReady: true,
      synchronizationInProgress: false,
      lastSynchronizationAt: null,
      lastSynchronizationFailed: false,
      catalogUpdatedAt: null,
    };
  }
  async synchronizeNow() {
    // Online mode reads authoritative server data and has no local catalog to synchronize.
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


  async searchProducts(search = "", skip = 0, take = 50, customerId: string | null = null) {
    const page = await request<OnlineProductPage>(
      "/api/commerce/v1/pos/drafts/products/search",
      this.post({ context: this.scope(), search, skip, take, customerId }),
    );
    return page satisfies PosCatalogSearchPage;
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
      priceListId: string | null; priceChannelId: string | null; requiresElectronicInvoice: boolean; isActive: boolean;
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
      priceListId: created.priceListId,
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
    return posApprovalClient.create(input);
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
      throw error;
    }
  }

  async changeQuantity(draftId: string, lineId: string, quantity: number) {
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
  ) {
    const result = await request<OnlineCheckoutResponse>(
        `/api/commerce/v1/pos/drafts/${draftId}/complete`,
        this.mutation({
          expectedVersion: this.version(draftId),
          payments, documentType,
        }, "POST", `online-sale-${draftId}`),
      );
      const nextDraft = this.mapDraft(result.nextDraft);
      await this.printDirect([result.receipt], !result.isDuplicate);
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
        printPreviewOpened: false,
      } satisfies PosCompleteSaleResult;
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
    const receipt = await request<PosPrintableReceipt>(
        `/api/commerce/v1/pos/drafts/sales/${documentId}/receipt`,
        this.post(this.scope()),
      );
    await this.printDirect([receipt]);
  }

  readScaleWeight() {
    return this.localEdge().readScaleWeight();
  }

  previewWorkSessionClosure(
    draftId: string,
    authorization?: PosSensitiveAuthorization,
  ): Promise<PosAuthorizedClosurePreview> {
    return this.localEdge().previewWorkSessionClosure(draftId, authorization);
  }

  closeWorkSession(input: PosCloseWorkSessionInput): Promise<PosWorkSessionClosure> {
    return this.localEdge().closeWorkSession(input);
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

  async invoiceOrders(
    orderIds: string[],
    paymentMethodCode: string,
    documentType: "SalesInvoice" | "SalesReceipt",
  ): Promise<InvoiceOrdersResponse> {
    const response = await invoiceCommerceOrders({
      workSessionId: this.context.workSessionId,
      warehouseId: this.context.warehouseId,
      userId: this.userId,
      orderIds,
      paymentMethodCode,
      paymentReference: null,
      documentType,
    });
    try {
      const documentIds = response.results
        .map((result) => result.documentId)
        .filter((documentId): documentId is string => Boolean(documentId));
      const receipts = await Promise.all(documentIds.map((documentId) =>
        request<PosPrintableReceipt>(
          `/api/commerce/v1/pos/drafts/sales/${documentId}/receipt`,
          this.post(this.scope()),
        ),
      ));
      await this.printDirect(receipts, receipts.length > 0);
      response.printStatus = response.completedCount ? "Sent" : "NotRequired";
    } catch (error) {
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
  const response = await fetch(path, {
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

function openPrintPreview(): Window | null {
  return window.open(
    "",
    "_blank",
    "popup=yes,width=460,height=760,resizable=yes,scrollbars=yes",
  );
}

export function openHalfLetterPrintPreview(): Window | null {
  return window.open(
    "",
    "_blank",
    "popup=yes,width=920,height=820,resizable=yes,scrollbars=yes",
  );
}

export async function renderInvoiceOrdersReceipt(
  preview: Window | null,
  response: InvoiceOrdersResponse,
  context: Pick<SalesWorkspaceContext, "businessId" | "warehouseId" | "workSessionId">,
) {
  const documentIds = response.results.flatMap((result) =>
    result.documentId && !result.error ? [result.documentId] : []);
  if (!documentIds.length) { preview?.close(); return; }
  if (!preview) throw new Error("El navegador bloqueó la vista previa de impresión.");
  const receipts = await Promise.all(documentIds.map((documentId) =>
    request<PosPrintableReceipt>(
      `/api/commerce/v1/pos/drafts/sales/${documentId}/receipt`,
      { method: "POST", body: JSON.stringify(context) },
    )));
  const currency = new Intl.NumberFormat("es-CO", {
    style: "currency", currency: "COP", maximumFractionDigits: 0,
  });
  const documents = receipts.map((receipt) => {
    const title = receipt.documentType === "SalesInvoice"
      ? "FACTURA ELECTRÓNICA DE VENTA" : "COMPROBANTE DE VENTA";
    const lines = receipt.lines.map((line) => `<div class="line"><b>${escapeHtml(line.description)}</b><div><span>${line.quantity} × ${currency.format(line.unitPrice)}</span><b>${currency.format(line.total)}</b></div></div>`).join("");
    const qr = receipt.documentType === "SalesInvoice"
      ? `<img src="${window.location.origin}/api/commerce/v1/pos/drafts/sales/${receipt.documentId}/qr?businessId=${context.businessId}&warehouseId=${context.warehouseId}&workSessionId=${context.workSessionId}" alt="QR DIAN">` : "";
    return `<article><header><h1>Auraly</h1><h2>${title}</h2><b>${escapeHtml(receipt.documentNumber)}</b><br>${new Date(receipt.issuedAt).toLocaleString("es-CO")}</header><section class="meta"><div><span>Cliente</span><b>${escapeHtml(receipt.customerName)}</b></div><div><span>Identificación</span><b>${escapeHtml(receipt.customerIdentification)}</b></div></section>${lines}<section class="totals"><div><span>Subtotal</span><b>${currency.format(receipt.untaxedAmount)}</b></div><div><span>Impuestos</span><b>${currency.format(receipt.taxAmount)}</b></div><div class="total"><span>Total</span><b>${currency.format(receipt.payableAmount)}</b></div></section>${receipt.cufe ? `<p class="cufe"><b>CUFE</b><br>${escapeHtml(receipt.cufe)}</p>` : ""}${qr}<footer>${title}</footer></article>`;
  }).join("");
  preview.document.open();
  preview.document.write(`<!doctype html><html lang="es"><head><meta charset="utf-8"><title>Tirillas Auraly</title><style>@page{size:80mm auto;margin:4mm}*{box-sizing:border-box}body{width:72mm;margin:0 auto;color:#111;font:12px/1.35 ui-monospace,Consolas,monospace}article{page-break-after:always}article:last-child{page-break-after:auto}header{text-align:center;border-bottom:1px dashed #555;padding-bottom:8px}h1{margin:0;font:800 19px/1.2 Arial,sans-serif}h2{margin:4px 0;font-size:12px}.meta,.totals{padding:8px 0;border-bottom:1px dashed #555}.meta div,.totals div,.line div{display:flex;justify-content:space-between;gap:10px}.line{padding:8px 0;border-bottom:1px dashed #aaa}.total{margin-top:5px;font-size:16px}.cufe{overflow-wrap:anywhere;font-size:9px}img{display:block;width:42mm;height:42mm;margin:9px auto 4px}footer{text-align:center;padding-top:6px}</style></head><body>${documents}<script>addEventListener('load',()=>setTimeout(()=>window.print(),150));</script></body></html>`);
  preview.document.close();
}

export async function renderInvoiceOrdersHalfLetter(
  preview: Window | null,
  response: InvoiceOrdersResponse,
  context: Pick<SalesWorkspaceContext, "businessId" | "warehouseId" | "workSessionId">,
) {
  const documentIds = response.results.flatMap((result) =>
    result.documentId && !result.error ? [result.documentId] : []);
  if (!documentIds.length) {
    preview?.close();
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
  context: Pick<SalesWorkspaceContext, "businessId" | "warehouseId" | "workSessionId">,
) {
  if (!receipts.length) { preview?.close(); return; }
  if (!preview) throw new Error("El navegador bloqueó la vista previa de impresión.");
  const currency = new Intl.NumberFormat("es-CO", {
    style: "currency", currency: "COP", maximumFractionDigits: 0,
  });
  const pages = receipts.map((receipt) => {
    const title = receipt.documentType === "SalesInvoice"
      ? "FACTURA ELECTRÓNICA DE VENTA"
      : "COMPROBANTE DE VENTA";
    const rows = receipt.lines.map((line) => `<tr><td>${escapeHtml(line.description)}</td><td class="n">${line.quantity}</td><td class="n">${currency.format(line.unitPrice)}</td><td class="n">${currency.format(line.total)}</td></tr>`).join("");
    const qr = receipt.documentType === "SalesInvoice"
      ? `<img class="qr" src="${window.location.origin}/api/commerce/v1/pos/drafts/sales/${receipt.documentId}/qr?businessId=${context.businessId}&warehouseId=${context.warehouseId}&workSessionId=${context.workSessionId}" alt="QR DIAN">`
      : "";
    const fiscal = receipt.fiscalNumber
      ? `<div><span>Número DIAN</span><b>${escapeHtml(receipt.fiscalNumber)}</b></div>`
      : "";
    const cufe = receipt.cufe
      ? `<p class="cufe"><b>CUFE</b><br>${escapeHtml(receipt.cufe)}</p>`
      : "";
    const copy = `<article class="document"><header><div><h1>Auraly</h1><h2>${title}</h2></div><div class="right"><b>${escapeHtml(receipt.documentNumber)}</b><br>${new Date(receipt.issuedAt).toLocaleString("es-CO")}</div></header><section class="meta"><div><span>Cliente</span><b>${escapeHtml(receipt.customerName)}</b></div><div><span>Identificación</span><b>${escapeHtml(receipt.customerIdentification)}</b></div>${fiscal}</section><table><thead><tr><th>Producto</th><th class="n">Cant.</th><th class="n">Precio</th><th class="n">Total</th></tr></thead><tbody>${rows}</tbody></table><section class="bottom"><div>${cufe}<small>Representación gráfica · copia cliente / control</small></div><div class="totals"><div><span>Subtotal</span><b>${currency.format(receipt.untaxedAmount)}</b></div><div><span>Impuestos</span><b>${currency.format(receipt.taxAmount)}</b></div><div class="total"><span>Total</span><b>${currency.format(receipt.payableAmount)}</b></div>${qr}</div></section></article>`;
    return `<section class="sheet"><div class="copy">${copy}</div><span class="cut">CORTE MEDIA CARTA</span><div class="copy">${copy}</div></section>`;
  }).join("");
  preview.document.open();
  preview.document.write(`<!doctype html><html lang="es"><head><meta charset="utf-8"><title>Media carta Auraly</title><style>
    @page{size:Letter portrait;margin:0}*{box-sizing:border-box}html,body{margin:0;color:#07111f;font-family:Arial,sans-serif}.sheet{width:215.9mm;height:279.4mm;page-break-after:always;position:relative;overflow:hidden}.sheet:last-child{page-break-after:auto}.copy{height:50%;padding:8mm 10mm 6mm;overflow:hidden}.copy:first-child{border-bottom:1px dashed #64748b}.cut{position:absolute;left:50%;top:calc(50% - 2.5mm);z-index:2;padding:0 2mm;transform:translateX(-50%);background:#fff;color:#64748b;font-size:7pt}.document{transform-origin:top left;font-size:8pt;line-height:1.2}header{display:grid;grid-template-columns:1fr auto;gap:6mm;border-bottom:1px solid #0f766e;padding-bottom:2mm}h1{margin:0;font-size:14pt;color:#065f5b}h2{margin:1mm 0 0;font-size:9pt}.right{text-align:right}.meta{display:grid;grid-template-columns:1fr 1fr;gap:1mm 5mm;margin:2mm 0}.meta div,.totals div{display:flex;justify-content:space-between;gap:3mm}.meta span,.totals span{color:#475569}table{width:100%;border-collapse:collapse;margin-top:1.5mm}th{padding:1.2mm;background:#e9f7f5;text-align:left;font-size:7pt}td{padding:1.1mm;border-bottom:1px solid #e2e8f0}.n{text-align:right;white-space:nowrap}.bottom{display:grid;grid-template-columns:1fr 44mm;gap:4mm;margin-top:2mm}.totals{border:1px solid #cbd5e1;border-radius:2mm;padding:2mm}.total{font-size:10pt;color:#065f5b}.cufe{overflow-wrap:anywhere;font-size:6.5pt}.qr{display:block;width:27mm;height:27mm;margin:1mm auto 0}small{color:#64748b;font-size:6.5pt}@media screen{body{background:#e2e8f0}.sheet{margin:8mm auto;background:#fff;box-shadow:0 4px 24px #0f172a33}}
  </style></head><body>${pages}<script>addEventListener('load',()=>{for(const copy of document.querySelectorAll('.copy')){const content=copy.querySelector('.document');const available=copy.clientHeight-2;if(content.scrollHeight>available){const scale=Math.max(.62,available/content.scrollHeight);content.style.transform='scale('+scale+')';content.style.width=(100/scale)+'%'}}setTimeout(()=>window.print(),150)});</script></body></html>`);
  preview.document.close();
}

async function renderReceipt(
  preview: Window,
  receipt: PosPrintableReceipt,
  qrImageUrl: string | null,
) {
  const currency = new Intl.NumberFormat("es-CO", {
    style: "currency",
    currency: "COP",
    maximumFractionDigits: 0,
  });
  const lines = receipt.lines
    .map(
      (line) => `
        <section class="line">
          <strong>${escapeHtml(line.description)}</strong>
          <div><span>${line.quantity} × ${currency.format(calculateReceiptRetailUnitPrice(line.unitPrice, line.quantity, line.discount, line.tax))}</span><b>${currency.format(line.total)}</b></div>
          ${line.discount > 0 ? `<small>Descuento: ${currency.format(line.discount)}</small>` : ""}
          ${line.tax > 0 ? `<small>IVA: ${currency.format(line.tax)}</small>` : ""}
        </section>`,
    )
    .join("");
  const payments = receipt.payments
    .map(
      (payment) =>
        `<div><span>${escapeHtml(payment.methodCode)}</span><b>${currency.format(payment.amount)}</b></div>`,
    )
    .join("");
  const isFiscal = receipt.documentType === "SalesInvoice";
  const fiscalMeta = isFiscal
    ? `<div><span>Número DIAN</span><b>${escapeHtml(receipt.fiscalNumber)}</b></div>`
    : "";
  const fiscalArtifacts = isFiscal
    ? `<div class="cufe"><strong>CUFE</strong><br>${escapeHtml(receipt.cufe)}</div>
       <img class="qr" src="${escapeHtml(qrImageUrl)}" alt="Código QR DIAN">
       <footer>Representación gráfica</footer>`
    : `<footer>Comprobante de venta</footer>`;
  const documentTitle = isFiscal
    ? "FACTURA ELECTRÓNICA DE VENTA"
    : "COMPROBANTE DE VENTA";
  preview.document.open();
  preview.document.write(`<!doctype html>
<html lang="es">
<head>
  <meta charset="utf-8">
  <title>${escapeHtml(receipt.documentNumber)}</title>
  <style>
    @page { size: 80mm auto; margin: 4mm; }
    * { box-sizing: border-box; }
    body { width: 72mm; margin: 0 auto; color: #111; font: 12px/1.35 ui-monospace, Consolas, monospace; }
    header { text-align: center; border-bottom: 1px dashed #555; padding-bottom: 8px; }
    h1 { margin: 0; font: 800 19px/1.2 Arial, sans-serif; }
    h2 { margin: 4px 0; font-size: 12px; }
    .meta, .totals, .payments { padding: 8px 0; border-bottom: 1px dashed #555; }
    .meta div, .totals div, .payments div, .line div { display: flex; justify-content: space-between; gap: 10px; }
    .line { padding: 8px 0; border-bottom: 1px dashed #aaa; }
    .line strong { display: block; margin-bottom: 2px; }
    .line small { display: block; color: #444; }
    .total { margin-top: 5px; font-size: 16px; }
    .cufe { margin-top: 9px; overflow-wrap: anywhere; font-size: 9px; }
    .qr { display: block; width: 42mm; height: 42mm; margin: 9px auto 4px; }
    footer { text-align: center; padding-top: 6px; }
  </style>
</head>
<body>
  <header>
    <h1>Auraly</h1>
    <h2>${documentTitle}</h2>
    <div>${new Date(receipt.issuedAt).toLocaleString("es-CO")}</div>
  </header>
  <section class="meta">
    <div><span>Documento Auraly</span><b>${escapeHtml(receipt.documentNumber)}</b></div>
    ${fiscalMeta}
    <div><span>Adquirente</span><b>${escapeHtml(receipt.customerName)}</b></div>
    <div><span>Identificación</span><b>${escapeHtml(receipt.customerIdentification)}</b></div>
  </section>
  ${lines}
  <section class="totals">
    <div><span>Subtotal</span><b>${currency.format(receipt.untaxedAmount)}</b></div>
    <div><span>Impuestos</span><b>${currency.format(receipt.taxAmount)}</b></div>
    <div class="total"><strong>Total</strong><strong>${currency.format(receipt.payableAmount)}</strong></div>
  </section>
  <section class="payments">${payments}</section>
  ${fiscalArtifacts}
</body>
</html>`);
  preview.document.close();
  const image = preview.document.querySelector("img.qr") as HTMLImageElement | null;
  if (image && !image.complete)
    await new Promise<void>((resolve) => {
      const done = () => resolve();
      image.addEventListener("load", done, { once: true });
      image.addEventListener("error", done, { once: true });
      window.setTimeout(done, 3_000);
    });
  preview.focus();
  preview.print();
}

function escapeHtml(value: string | null) {
  return (value ?? "")
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#39;");
}
