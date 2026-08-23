import type {
  CommerceOrderDetail,
  CommerceOrderFilters,
  CommerceOrderPage,
  InvoiceOrdersResponse,
} from "@/services/orders/commerce-orders-client";
import { savePosDraftAsOrder } from "@/services/orders/save-pos-order";
import type { SellerOrderResult } from "@/services/api/seller-orders";

export type PosSaleDocumentType = "SalesInvoice" | "SalesReceipt";
const EDGE_BASE_URL =
  process.env.NEXT_PUBLIC_AURALY_POS_EDGE_URL ?? "http://127.0.0.1:47831";

export type DraftId = { value: string };
export type ProductId = { value: string };
export type PosCatalogProduct = {
  productId: string;
  productCode: string;
  reference: string | null;
  name: string;
  baseUnitCode: string;
  taxCode: string;
  taxRate: number;
  unitPrice: number;
  currencyCode: string;
  isActive: boolean;
  isWeighable: boolean;
  allowsFractionalSale: boolean;
  priceSource: "Public" | "Base" | "PriceChannel";
};
export type PosCatalogSearchPage = {
  items: PosCatalogProduct[];
  hasMore: boolean;
  nextOffset: number | null;
};

export type PosCustomer = {
  customerId: string;
  identification: string;
  name: string;
  priceChannelId: string | null;
  requiresElectronicInvoice: boolean;
  isActive: boolean;
};

export type PosCustomerSearchPage = {
  items: PosCustomer[];
  hasMore: boolean;
  nextOffset: number | null;
};

export type PosCountry = { countryId: string; code: string; name: string; isActive: boolean };
export type PosAdministrativeDivision = { administrativeDivisionId: string; countryId: string; code: string; name: string; divisionType: string; isActive: boolean };
export type PosCity = { cityId: string; administrativeDivisionId: string; code: string; name: string; isActive: boolean };
export type PosCreateCustomerInput = {
  partyType: "NaturalPerson" | "Organization";
  identificationCountryId: string;
  identificationTypeCode: string;
  identification: string;
  verificationDigit: string | null;
  displayName: string;
  legalName: string | null;
  firstName: string | null;
  lastName: string | null;
  email: string | null;
  phone: string | null;
  primarySite: {
    code: string; name: string; countryId: string; administrativeDivisionId: string;
    cityId: string; addressLine: string; neighborhood: string | null; postalCode: string | null;
    email: string | null; phone: string | null; isPrimary: boolean;
  };
};
export type PosCustomerSelection = { draft: PosDraft; customer: PosCustomer | null };

export type PosIssuedSaleSummary = {
  documentId: { value: string };
  documentType: PosSaleDocumentType;
  documentNumber: string;
  fiscalNumber: string;
  issuedAt: string;
  total: number;
  customerIdentification: string;
  customerName: string;
  fiscalStatus: string | null;
};

export type PosIssuedSaleSearchPage = {
  items: PosIssuedSaleSummary[];
  hasMore: boolean;
  nextOffset: number | null;
};



export type PosDraftLine = {
  lineId: string;
  productId: ProductId;
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
  allowsFractionalSale: boolean;
  net: number;
  tax: number;
  total: number;
};



function sensitiveHeaders(authorization?: PosSensitiveAuthorization): Record<string, string> | undefined {
  if (!authorization) return undefined;
  return {
    ...(authorization.supervisorSecret ? { "X-Auraly-Supervisor-Secret": authorization.supervisorSecret } : {}),
    ...(authorization.approvalRequestId ? { "X-Auraly-Approval-Id": authorization.approvalRequestId } : {}),
    ...(authorization.operationId ? { "X-Auraly-Operation-Id": authorization.operationId } : {}),
  };
}

export type PosApprovalCreateInput = {
  businessId: string;
  deviceId?: string | null;
  workSessionId?: string | null;
  draftId: string;
  lineId?: string | null;
  permissionResource: string;
  contextJson: string;
};

export type PosApprovalSummary = {
  approvalRequestId: string;
  businessId: string;
  deviceId: string | null;
  workSessionId: string | null;
  draftId: string;
  lineId: string | null;
  permissionResource: string;
  contextJson: string;
  status: "Pending" | "Approved" | "Rejected" | "Expired" | "Reserved" | "Consumed";
  requestedByName: string;
  expiresAt: string;
  decidedByName: string | null;
};

export type PosSensitiveAuthorization = {
  approvalRequestId?: string;
  supervisorSecret?: string;
  operationId?: string;
};

export type PosDraft = {
  draftId: DraftId;
  customerId: string | null;
  sellerId: string | null;
  status: string;
  name: string | null;
  reference: string | null;
  observation: string | null;
  lines: PosDraftLine[];
  untaxedAmount: number;
  taxAmount: number;
  payableAmount: number;
  sourceOrderId?: string | null;
};

export type PosCaptureResult = {
  status: "Added" | "NotFound" | "InsufficientInventory" | "OfflineValidationRequired";
  draft: PosDraft | null;
  availability?: {
    requestedQuantity: number;
    availableQuantity: number;
    isAvailable: boolean;
  } | null;
};

export type PosFiscalNumberPreview = {
  seriesId: string;
  prefix: string;
  consecutive: number;
  fullNumber: string;
  isAvailable: boolean;
};

export type PosDocumentNumberPreview = {
  seriesId: string;
  documentType: string;
  prefix: string;
  seriesCode: string;
  consecutive: number;
  fullNumber: string;
  isAvailable: boolean;
};

export type PosNextNumbers = { document: PosDocumentNumberPreview; fiscal: PosFiscalNumberPreview };

export type PosPaymentInput = {
  methodCode: string;
  amount: number;
  reference: string | null;
};

export type PosReceiptLine = {
  productCode: string;
  description: string;
  quantity: number;
  unitPrice: number;
  discount: number;
  tax: number;
  total: number;
};

export type PosPrintableReceipt = {
  documentId: string;
  documentType: PosSaleDocumentType;
  documentNumber: string;
  fiscalNumber: string | null;
  issuedAt: string;
  customerIdentification: string;
  customerName: string;
  lines: PosReceiptLine[];
  payments: PosPaymentInput[];
  untaxedAmount: number;
  taxAmount: number;
  payableAmount: number;
  cufe: string | null;
  qrPayload: string | null;
  fiscalStatus: string;
};

export type PosCompleteSaleResult = {
  issuedSale: {
    documentId: { value: string };
    documentNumber: string;
    fiscalNumber: string | null;
    cufe: string | null;
    qrPayload: string | null;
    total: number;
    outboxMessageId: string;
    wasAlreadyIssued: boolean;
  };
  nextDraft: PosDraft;
  nextDocumentNumber: PosDocumentNumberPreview | null;
  nextFiscalNumber: PosFiscalNumberPreview | null;
  receipt?: PosPrintableReceipt;
  printPreviewOpened?: boolean;
};

export type PosCashMovementDirection = "In" | "Out";

export type PosCashMovementReason = {
  reasonId: string;
  businessId: string;
  code: string;
  name: string;
  direction: PosCashMovementDirection;
  counterpartAccountingCategory: string | null;
  defaultCostCenterId: string | null;
  defaultCostCenterName: string | null;
  accountCode: string | null;
  accountName: string | null;
  isAccountingConfigured: boolean;
  requiresReference: boolean;
  isActive: boolean;
};

export type PosCashMovementInput = {
  documentId: string;
  reasonId: string;
  amount: number;
  occurredAt: string;
  reference: string | null;
  notes: string | null;
  costCenterId: string | null;
};

export type PosCashMovementAcceptance = {
  documentId: string;
  status: string;
  idempotentReplay: boolean;
  documentNumber?: string;
};

export type PosWorkSessionPaymentTotal = {
  paymentMethodCode: string;
  salesAmount: number;
  refundAmount: number;
  otherAmount: number;
  netAmount: number;
};

export type PosWorkSessionClosure = {
  workSessionClosureId: string;
  workSessionId: string;
  businessId: string;
  businessName: string;
  warehouseId: string;
  warehouseName: string;
  userId: string;
  userName: string;
  deviceId: string | null;
  openedAt: string;
  closedAt: string;
  totalSales: number;
  totalRefunds: number;
  totalOther: number;
  netAmount: number;
  expectedCash: number;
  countedCash: number | null;
  cashDifference: number | null;
  note: string | null;
  paymentTotals: PosWorkSessionPaymentTotal[];
};

export type PosWorkSessionClosurePreview = Omit<
  PosWorkSessionClosure,
  "workSessionClosureId" | "closedAt" | "countedCash" | "cashDifference" | "note"
> & { lastActivityAt: string };

export type PosAuthorizedClosurePreview = {
  authorizationToken: string;
  preview: PosWorkSessionClosurePreview;
};

export type PosCloseWorkSessionInput = {
  operationId: string;
  authorizationToken: string;
  countedCash: number;
  note: string | null;
};

export interface PosClient {
  readonly mode: "edge" | "online";
  health(): Promise<{
    status: string;
    serverConnected: boolean;
    deviceSeriesCode: string;
    businessId: string;
    businessName: string;
    warehouseName: string;
    userDisplayName: string;
    userId: string | null;
    workSessionId?: string | null;
    deviceId?: string | null;
    fiscalReady: boolean;
    synchronizationInProgress: boolean;
    lastSynchronizationAt: string | null;
    lastSynchronizationFailed: boolean;
    catalogUpdatedAt: string | null;
    permissions?: string[];
  }>;
  synchronizeNow(): Promise<void>;
  openCashDrawer(): Promise<void>;
  readScaleWeight(): Promise<{ weight: number; unit: string; portName: string }>;
  searchProducts(search?: string, skip?: number, take?: number, customerId?: string | null): Promise<PosCatalogSearchPage>;
  searchCustomers(search?: string, skip?: number, take?: number): Promise<PosCustomerSearchPage>;
  customer(customerId: string): Promise<PosCustomer>;
  customerCountries(): Promise<PosCountry[]>;
  customerDivisions(countryId: string): Promise<PosAdministrativeDivision[]>;
  customerCities(divisionId: string): Promise<PosCity[]>;
  createCustomer(input: PosCreateCustomerInput): Promise<PosCustomer>;
  createApproval(input: PosApprovalCreateInput): Promise<PosApprovalSummary>;
  activeDraft(): Promise<PosDraft>;
  nextNumbers(documentType?: PosSaleDocumentType): Promise<PosNextNumbers | null>;
  capture(value: string, customerId: string | null): Promise<PosCaptureResult>;
  captureSelectedProduct(
    product: PosCatalogProduct,
    customerId: string | null,
  ): Promise<PosCaptureResult>;
  changeQuantity(draftId: string, lineId: string, quantity: number): Promise<PosCaptureResult>;
  setDiscount(draftId: string, lineId: string, discount: number, authorization?: PosSensitiveAuthorization): Promise<PosDraft>;
  selectCustomer(draftId: string, customerId: string | null): Promise<PosCustomerSelection>;
  removeLine(draftId: string, lineId: string, authorization?: PosSensitiveAuthorization): Promise<PosDraft>;
  cancelDraft(draftId: string, authorization?: PosSensitiveAuthorization): Promise<PosDraft>;
  saveTemporary(
    draftId: string,
    name: string,
    reference: string,
    observation: string,
  ): Promise<PosDraft>;
  temporaries(search?: string): Promise<PosDraft[]>;
  deleteTemporary(draftId: string): Promise<void>;
  recoverTemporary(draftId: string): Promise<PosDraft>;
  completeSale(
    draftId: string,
    customerIdentification: string | null,
    payments: PosPaymentInput[],
    documentType: PosSaleDocumentType,
  ): Promise<PosCompleteSaleResult>;
  searchIssuedSales(search?: string, skip?: number, take?: number): Promise<PosIssuedSaleSearchPage>;
  reprint(documentId: string): Promise<void>;
  orders(filters: CommerceOrderFilters): Promise<CommerceOrderPage>;
  order(orderId: string): Promise<CommerceOrderDetail>;
  recoverOrder(orderId: string): Promise<PosDraft>;
  renewRecoveredOrder(orderId: string): Promise<unknown>;
  releaseRecoveredOrder(orderId: string): Promise<unknown>;
  saveOrder(draft: PosDraft): Promise<{ order: SellerOrderResult; nextDraft: PosDraft }>;
  invoiceOrders(
    orderIds: string[],
    paymentMethodCode: string,
    documentType: PosSaleDocumentType,
  ): Promise<InvoiceOrdersResponse>;
  cashMovementReasons(direction: PosCashMovementDirection): Promise<PosCashMovementReason[]>;
  confirmCashMovement(input: PosCashMovementInput): Promise<PosCashMovementAcceptance>;
  previewWorkSessionClosure(draftId: string, authorization?: PosSensitiveAuthorization): Promise<PosAuthorizedClosurePreview>;
  closeWorkSession(input: PosCloseWorkSessionInput): Promise<PosWorkSessionClosure>;
}

export class PosEdgeError extends Error {
  constructor(
    message: string,
    public readonly status: number,
    public readonly code?: string,
  ) {
    super(message);
  }
}

export type PosLocalUserSession = {
  sessionId: string;
  userId: string;
  username: string;
  displayName: string;
  permissions: string[];
  expiresAt: string;
  token: string | null;
};

export type PosPrinterConfiguration = {
  receiptMode: "BrowserPreview" | "WindowsRaw" | "File";
  receiptPrinterName: string | null;
  receiptPaperWidthMillimeters: 58 | 80;
  letterPrinterName: string | null;
  orderMode: "BrowserPreview" | "WindowsPrint";
  posOutputFormat: "Receipt" | "HalfLetter";
  ordersOutputFormat: "Receipt" | "HalfLetter";
  templateRoutes: Array<{
    documentType: "SalesInvoice" | "SalesReceipt";
    format: "Receipt" | "HalfLetter";
    printerName: string | null;
  }> | null;
  scale: PosScaleConfiguration | null;
  posPrinterName?: string | null;
  ordersPrinterName?: string | null;
  ordersReceiptPaperWidthMillimeters?: 58 | 80;
};

export type PosScaleConfiguration = {
  enabled: boolean;
  portName: string;
  baudRate: number;
  dataBits: number;
  parity: "None" | "Even" | "Odd";
  stopBits: "One" | "Two";
  sendsRequest: boolean;
  requestText: string;
  startIndex: number;
  length: number;
  reverse: boolean;
  divideBy1000: boolean;
  timeoutMilliseconds: number;
};

export type PosPrinterConfigurationView = {
  configuration: PosPrinterConfiguration;
  installedPrinters: string[];
  serialPorts: string[];
};

const BROWSER_PRINTER_CONFIGURATION_KEY = "auraly.printing.configuration.v1";

export function loadBrowserPrinterConfiguration(): PosPrinterConfiguration {
  const defaults: PosPrinterConfiguration = {
    receiptMode: "BrowserPreview",
    receiptPrinterName: null,
    receiptPaperWidthMillimeters: 80,
    letterPrinterName: null,
    orderMode: "BrowserPreview",
    posOutputFormat: "Receipt",
    ordersOutputFormat: "HalfLetter",
    templateRoutes: null,
    scale: null,
  };
  if (typeof window === "undefined") return defaults;
  try {
    return { ...defaults, ...JSON.parse(window.localStorage.getItem(
      BROWSER_PRINTER_CONFIGURATION_KEY) ?? "{}") };
  } catch {
    return defaults;
  }
}

export function saveBrowserPrinterConfiguration(
  configuration: PosPrinterConfiguration,
) {
  window.localStorage.setItem(
    BROWSER_PRINTER_CONFIGURATION_KEY,
    JSON.stringify({
      ...configuration,
      receiptMode: "BrowserPreview",
      orderMode: "BrowserPreview",
    }),
  );
  return loadBrowserPrinterConfiguration();
}

export class PosEdgeClient implements PosClient {
  readonly mode = "edge" as const;

  constructor(
    private readonly sessionToken: string,
    private userSessionToken: string | null = null,
  ) {}

  health() {
    return this.request<{
      status: string;
      startupMode: "online" | "enrolled";
      serverConnected: boolean;
      deviceSeriesCode: string;
      businessId: string;
      warehouseId: string;
      businessName: string;
      warehouseName: string;
      userDisplayName: string;
      userId: string | null;
      workSessionId: string | null;
      deviceId: string;
      permissions: string[];
      fiscalReady: boolean;
      synchronizationInProgress: boolean;
      lastSynchronizationAt: string | null;
      lastSynchronizationFailed: boolean;
      catalogUpdatedAt: string | null;
    }>("/edge/v1/health");
  }


  synchronizeNow() {
    return this.requestVoid("/edge/v1/synchronization/refresh", { method: "POST" });
  }
  setStartupMode(mode: "online" | "enrolled") {
    return this.requestVoid("/edge/v1/configuration/startup-mode", {
      method: "PUT",
      body: JSON.stringify({ mode }),
    });
  }

  printerConfiguration() {
    return this.request<PosPrinterConfigurationView>(
      "/edge/v1/configuration/printers",
    );
  }

  savePrinterConfiguration(configuration: PosPrinterConfiguration) {
    return this.request<PosPrinterConfigurationView>(
      "/edge/v1/configuration/printers",
      { method: "PUT", body: JSON.stringify(configuration) },
    );
  }

  openCashDrawer() {
    return this.requestVoid("/edge/v1/cash-drawer/open", { method: "POST" });
  }

  printReceipt(receipt: PosPrintableReceipt) {
    return this.requestVoid("/edge/v1/print/receipt", {
      method: "POST",
      body: JSON.stringify(receipt),
    });
  }

  readScaleWeight() {
    return this.request<{ weight: number; unit: string; portName: string }>(
      "/edge/v1/scale/read", { method: "POST" },
    );
  }

  printWorkSessionClosure(closure: PosWorkSessionClosure) {
    return this.requestVoid("/edge/v1/print/work-session-closure", {
      method: "POST", body: JSON.stringify(closure),
    });
  }

  watchLocalState(onStateChanged: () => void): () => void {
    const controller = new AbortController();
    const listen = async () => {
      try {
        const response = await fetch(`${EDGE_BASE_URL}/edge/v1/events`, {
          headers: {
            "X-Auraly-Edge-Session": this.sessionToken,
            ...(this.userSessionToken
              ? { "X-Auraly-User-Session": this.userSessionToken }
              : {}),
          },
          signal: controller.signal,
        });
        if (!response.ok || !response.body) return;

        const reader = response.body.getReader();
        const decoder = new TextDecoder();
        let pending = "";
        while (!controller.signal.aborted) {
          const next = await reader.read();
          if (next.done) break;
          pending += decoder.decode(next.value, { stream: true });
          let boundary = pending.indexOf("\n\n");
          while (boundary >= 0) {
            pending = pending.slice(boundary + 2);
            onStateChanged();
            boundary = pending.indexOf("\n\n");
          }
        }
      } catch {
        // The loopback host can be stopped while the application stays open.
      }
    };
    void listen();
    return () => controller.abort();
  }

  cashMovementReasons(direction: PosCashMovementDirection) {
    return this.request<PosCashMovementReason[]>(
      "/edge/v1/cash-movement-reasons?direction=" + direction,
    );
  }

  confirmCashMovement(input: PosCashMovementInput) {
    return this.request<PosCashMovementAcceptance>("/edge/v1/cash-movements", {
      method: "POST",
      body: JSON.stringify(input),
    });
  }

  closeWorkSession(input: PosCloseWorkSessionInput) {
    return this.request<PosWorkSessionClosure>("/edge/v1/work-sessions/current/close", {
      method: "POST", body: JSON.stringify(input),
    });
  }

  previewWorkSessionClosure(draftId: string, authorization?: PosSensitiveAuthorization) {
    return this.request<PosAuthorizedClosurePreview>(
      "/edge/v1/work-sessions/current/closure-preview",
      { method: "POST", headers: sensitiveHeaders(authorization), body: JSON.stringify({ draftId }) },
    );
  }

  async login(username: string, password: string) {
    const session = await this.request<PosLocalUserSession>("/edge/v1/auth/login", {
      method: "POST",
      body: JSON.stringify({ username, password }),
    });
    if (!session.token) throw new PosEdgeError("El servicio local no devolvió una sesión de usuario.", 500);
    this.userSessionToken = session.token;
    window.sessionStorage.setItem("auraly.pos.user-session", session.token);
    return session;
  }

  async logout() {
    if (this.userSessionToken) {
      await this.requestVoid("/edge/v1/auth/logout", { method: "POST" });
    }
    this.userSessionToken = null;
    window.sessionStorage.removeItem("auraly.pos.user-session");
  }

  searchProducts(search = "", skip = 0, take = 50, customerId: string | null = null) {
    const query = new URLSearchParams({
      search,
      skip: String(skip),
      take: String(take),
    });
    if (customerId) query.set("customerId", customerId);
    return this.request<PosCatalogSearchPage>(
      `/edge/v1/catalog/products?${query}`,
    );
  }

  searchCustomers(search = "", skip = 0, take = 50) {
    const query = new URLSearchParams({
      search,
      skip: String(skip),
      take: String(take),
    });
    return this.request<PosCustomerSearchPage>(`/edge/v1/customers?${query}`);
  }

  customerCountries() {
    return this.request<PosCountry[]>("/edge/v1/customers/geography/countries");
  }

  customerDivisions(countryId: string) {
    return this.request<PosAdministrativeDivision[]>(`/edge/v1/customers/geography/countries/${countryId}/divisions`);
  }

  customerCities(divisionId: string) {
    return this.request<PosCity[]>(`/edge/v1/customers/geography/divisions/${divisionId}/cities`);
  }

  createCustomer(input: PosCreateCustomerInput) {
    return this.request<PosCustomer>("/edge/v1/customers", {
      method: "POST",
      body: JSON.stringify(input),
    });
  }
  customer(customerId: string) {
    return this.request<PosCustomer>(`/edge/v1/customers/${customerId}`);
  }

  createApproval(input: PosApprovalCreateInput) {
    return this.request<PosApprovalSummary>("/edge/v1/approvals", {
      method: "POST",
      body: JSON.stringify(input),
    });
  }

  activeDraft() {
    return this.request<PosDraft>("/edge/v1/drafts/active");
  }

  nextNumbers(documentType: PosSaleDocumentType = "SalesInvoice") {
    return this.request<PosNextNumbers>(
      `/edge/v1/sales/next-number?documentType=${encodeURIComponent(documentType)}`,
    );
  }

  capture(value: string, customerId: string | null) {
    return this.request<PosCaptureResult>("/edge/v1/capture", {
      method: "POST",
      body: JSON.stringify({ value, customerId }),
    });
  }

  captureSelectedProduct(
    product: PosCatalogProduct,
    customerId: string | null,
  ) {
    return this.capture(product.productCode, customerId);
  }

  changeQuantity(draftId: string, lineId: string, quantity: number) {
    return this.request<PosCaptureResult>(
      `/edge/v1/drafts/${draftId}/lines/${lineId}/quantity`,
      {
        method: "PUT",
        body: JSON.stringify({ quantity }),
      },
    );
  }

  setDiscount(draftId: string, lineId: string, discount: number, authorization?: PosSensitiveAuthorization) {
    return this.request<PosDraft>(
      `/edge/v1/drafts/${draftId}/lines/${lineId}/discount`,
      {
        method: "PUT",
        body: JSON.stringify({ discount }),
        headers: sensitiveHeaders(authorization),
      },
    );
  }

  selectCustomer(draftId: string, customerId: string | null) {
    return this.request<PosCustomerSelection>(
      `/edge/v1/drafts/${draftId}/customer`,
      {
        method: "PUT",
        body: JSON.stringify({ customerId }),
      },
    );
  }

  removeLine(draftId: string, lineId: string, authorization?: PosSensitiveAuthorization) {
    return this.request<PosDraft>(
      `/edge/v1/drafts/${draftId}/lines/${lineId}`,
      {
        method: "DELETE",
        headers: sensitiveHeaders(authorization),
      },
    );
  }

  cancelDraft(draftId: string, authorization?: PosSensitiveAuthorization) {
    return this.request<PosDraft>(
      `/edge/v1/drafts/${draftId}`,
      {
        method: "DELETE",
        headers: sensitiveHeaders(authorization),
      },
    );
  }

  saveTemporary(
    draftId: string,
    name: string,
    reference: string,
    observation: string,
  ) {
    return this.request<PosDraft>(`/edge/v1/drafts/${draftId}/temporary`, {
      method: "POST",
      body: JSON.stringify({ name, reference, observation }),
    });
  }

  temporaries(search = "") {
    const query = search ? `?search=${encodeURIComponent(search)}` : "";
    return this.request<PosDraft[]>(`/edge/v1/temporaries${query}`);
  }

  deleteTemporary(draftId: string) {
    return this.requestVoid(`/edge/v1/temporaries/${draftId}`, { method: "DELETE" });
  }

  recoverTemporary(draftId: string) {
    return this.request<PosDraft>(
      `/edge/v1/temporaries/${draftId}/recover`,
      { method: "POST" },
    );
  }

  completeSale(
    draftId: string,
    customerIdentification: string | null,
    payments: PosPaymentInput[],
    documentType: PosSaleDocumentType,
  ) {
    return this.request<PosCompleteSaleResult>(
      `/edge/v1/drafts/${draftId}/complete`,
      {
        method: "POST",
        body: JSON.stringify({ customerIdentification, payments, documentType }),
      },
    );
  }

  searchIssuedSales(search = "", skip = 0, take = 50) {
    const query = new URLSearchParams({
      search,
      skip: String(skip),
      take: String(take),
    });
    return this.request<PosIssuedSaleSearchPage>(`/edge/v1/sales?${query}`);
  }

  reprint(documentId: string) {
    return this.requestVoid(`/edge/v1/sales/${documentId}/reprint`, { method: "POST" });
  }

  orders(filters: CommerceOrderFilters) {
    const query = new URLSearchParams();
    Object.entries(filters).forEach(([key, value]) => {
      if (value !== undefined && value !== "") query.set(key, String(value));
    });
    return this.request<CommerceOrderPage>(`/edge/v1/orders?${query}`);
  }

  order(orderId: string) {
    return this.request<CommerceOrderDetail>(`/edge/v1/orders/${orderId}`);
  }

  recoverOrder(orderId: string) {
    return this.request<PosDraft>(`/edge/v1/orders/${orderId}/recover`, {
      method: "POST",
    });
  }

  renewRecoveredOrder(orderId: string) {
    return this.request(`/edge/v1/orders/${orderId}/claim`, { method: "POST" });
  }

  releaseRecoveredOrder(orderId: string) {
    return this.request(`/edge/v1/orders/${orderId}/claim/release`, { method: "POST" });
  }

  async saveOrder(draft: PosDraft) {
    const health = await this.health();
    if (!health.serverConnected || !health.workSessionId)
      throw new Error("Guardar el pedido requiere conexión con Auraly.");
    const order = await savePosDraftAsOrder(
      {
        businessId: health.businessId,
        warehouseId: health.warehouseId,
        workSessionId: health.workSessionId,
      },
      draft,
      `pos-order-${draft.draftId.value}`,
    );
    try {
      return { order, nextDraft: await this.cancelDraft(draft.draftId.value) };
    } catch (cleanupError) {
      if (draft.sourceOrderId)
        await this.releaseRecoveredOrder(draft.sourceOrderId).catch(() => undefined);
      throw new Error(`El pedido ${order.orderNumber} se guardó, pero no fue posible limpiar la venta activa: ${cleanupError instanceof Error ? cleanupError.message : "error desconocido"}`);
    }
  }

  invoiceOrders(
    orderIds: string[],
    paymentMethodCode: string,
    documentType: PosSaleDocumentType,
  ) {
    return this.request<InvoiceOrdersResponse>("/edge/v1/orders/invoice", {
      method: "POST",
      body: JSON.stringify({
        orderIds,
        paymentMethodCode,
        paymentReference: null,
        documentType,
        idempotencyKey: crypto.randomUUID(),
      }),
    });
  }

  private async request<T>(path: string, init: RequestInit = {}): Promise<T> {
    const response = await fetch(`${EDGE_BASE_URL}${path}`, {
      ...init,
      cache: "no-store",
      headers: {
        "Content-Type": "application/json",
        "X-Auraly-Edge-Session": this.sessionToken,
        ...(this.userSessionToken
          ? { "X-Auraly-User-Session": this.userSessionToken }
          : {}),
        ...init.headers,
      },
    });
    if (!response.ok) {
      const raw = await response.text();
      let detail = raw || response.statusText;
      try {
        const problem = JSON.parse(raw) as { detail?: string; title?: string; code?: string };
        detail = problem.detail || problem.title || detail;
        throw new PosEdgeError(detail, response.status, problem.code || problem.title);
      } catch (parsed) {
        if (parsed instanceof PosEdgeError) throw parsed;
        // The local host may intentionally return plain text for simple failures.
      }
      throw new PosEdgeError(detail, response.status);
    }
    return (await response.json()) as T;
  }

  private async requestVoid(path: string, init: RequestInit = {}): Promise<void> {
    const response = await fetch(`${EDGE_BASE_URL}${path}`, {
      ...init,
      cache: "no-store",
      headers: {
        "Content-Type": "application/json",
        "X-Auraly-Edge-Session": this.sessionToken,
        ...(this.userSessionToken
          ? { "X-Auraly-User-Session": this.userSessionToken }
          : {}),
        ...init.headers,
      },
    });
    if (!response.ok) {
      const raw = await response.text();
      let detail = raw || response.statusText;
      try {
        const problem = JSON.parse(raw) as { detail?: string; title?: string };
        detail = problem.detail || problem.title || detail;
      } catch {
        // The local host may intentionally return plain text for simple failures.
      }
      throw new PosEdgeError(detail, response.status);
    }
  }
}

export function readEdgeUserSession(): string | null {
  return window.sessionStorage.getItem("auraly.pos.user-session");
}

export function readEdgeTokenFromLaunch(): string | null {
  const fragment = new URLSearchParams(window.location.hash.slice(1));
  const launched = fragment.get("edgeToken");
  if (launched) {
    window.sessionStorage.setItem("auraly.pos.edge-token", launched);
    window.history.replaceState(null, "", window.location.pathname + window.location.search);
    return launched;
  }
  return window.sessionStorage.getItem("auraly.pos.edge-token");
}
