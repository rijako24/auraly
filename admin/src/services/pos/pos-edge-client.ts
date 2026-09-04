import type {
  CommerceOrderDetail,
  CommerceOrderFilters,
  CommerceOrderPage,
  InvoiceOrdersResponse,
} from "@/services/orders/commerce-orders-client";
import { savePosDraftAsOrder } from "@/services/orders/save-pos-order";
import type { SellerOrderResult } from "@/services/api/seller-orders";
import type { TenantBranding } from "@/services/api/tenants";
import { printWorkSessionClosure, workSessionClosureHtml } from "./pos-work-session-close";
import { announceSessionReplacement } from "@/lib/auth-session";
import { buildLoginRedirect } from "@/lib/login-redirect";

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
  isCreditEnabled?: boolean;
  defaultCreditDueDays?: number;
  availableCredit?: number | null;
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
  documentUnitCost: number;
  allowsDocumentCostOverride: boolean;
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
  expiresAt?: string;
};

export type PosDraftLineUpdate = Pick<
  PosDraftLine,
  "lineId" | "description" | "unitPrice" | "discount" | "documentUnitCost"
>;

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
  status: "Added" | "NotFound" | "InsufficientInventory";
  draft: PosDraft | null;
  capturedProduct?: {
    product: {
      productId: string;
      name: string;
      productCode: string;
      allowsFractionalSale: boolean;
    };
    quantity: number;
  } | null;
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
  cardFranchiseCode?: string | null;
  approvalNumber?: string | null;
  bankAccountId?: string | null;
  notes?: string | null;
};

export type PosSaleSettlement = {
  grossAmount: number;
  withholdingTotal: number;
  netAmount: number;
  lines: Array<{
    ruleId: string;
    ruleVersion: number;
    ruleCode: string;
    name: string;
    kind: string;
    baseKind: string;
    taxableBase: number;
    rate: number;
    amount: number;
    jurisdictionCode: string | null;
  }>;
};
export type PosProductWarehouseAvailability = {
  businessId: string;
  businessName: string;
  warehouseId: string;
  warehouseCode: string;
  warehouseName: string;
  productId: string;
  productCode: string;
  quantityOnHand: number;
  isCurrentBusiness: boolean;
};

export type PosReceiptLine = {
  productCode: string;
  description: string;
  quantity: number;
  unitPrice: number;
  discount: number;
  tax: number;
  total: number;
  taxCode: string;
  taxRate: number;
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
  withholdingTotal: number;
  netPayableAmount: number;
  companyName?: string | null;
  companyLogoSource?: string | null;
  withholdings: Array<{
    ruleCode: string;
    name: string;
    kind: string;
    taxableBase: number;
    rate: number;
    amount: number;
  }> | null;
  businessName?: string | null;
  warehouseName?: string | null;
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
  printedDirectly?: boolean;
  printError?: string | null;
};

type PosEdgePrintableReceipt = Omit<PosPrintableReceipt,
  "documentId" | "customerName" | "fiscalStatus"> & {
  documentId: { value: string };
  customerName?: string | null;
  fiscalStatus?: string | null;
};

type PosEdgeCompleteSaleResult = Omit<PosCompleteSaleResult, "receipt"> & {
  receipt: PosEdgePrintableReceipt;
};

function announceEdgeLoginReplacement(status: number, code?: string): void {
  if (status !== 401 || code !== "LoginReplaced" || typeof window === "undefined") return;
  window.localStorage.removeItem("auraly.pos.user-session");
  announceSessionReplacement(buildLoginRedirect(
    window.location.pathname,
    window.location.search,
  ));
}

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

export type PosCashMovementTicket = {
  documentId: string;
  direction: PosCashMovementDirection;
  reasonName: string;
  amount: number;
  occurredAt: string;
  reference: string | null;
  notes: string | null;
  responsibleName: string;
};

export type PosWorkSessionPaymentTotal = {
  paymentMethodCode: string;
  salesAmount: number;
  refundAmount: number;
  otherAmount: number;
  netAmount: number;
  countedAmount: number | null;
  difference: number | null;
  requiresCount: boolean;
};

export type PosInventoryIssue = {
  lineId: string;
  productId: string;
  productCode: string;
  description: string;
  requestedQuantity: number;
  availableQuantity: number;
};

export type PosInventoryValidation = {
  isValid: boolean;
  wasValidated: boolean;
  issues: PosInventoryIssue[];
};
export type PosCreditTerms = { amount: number; dueDate: string };

export type PosWorkSessionPaymentCount = {
  paymentMethodCode: string;
  countedAmount: number;
};

export type PosSynchronizationEvent = {
  sequence: number;
  occurredAt: string;
  level: "Info" | "Success" | "Warning" | "Error";
  category: string;
  title: string;
  detail: string | null;
  productId: string | null;
  previousPrice: number | null;
  newPrice: number | null;
};

export type PosWorkSessionClosure = {
  workSessionClosureId: string;
  workSessionId: string;
  businessId: string;
  businessName: string;
  warehouseId: string | null;
  warehouseName: string | null;
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
  salesCount: number;
  creditSalesCount: number;
  creditSalesAmount: number;
  returnCount: number;
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
  draftId: string;
  authorization?: PosSensitiveAuthorization;
  countedCash: number;
  paymentCounts: PosWorkSessionPaymentCount[];
  note: string | null;
};

export type PosReferenceOption = {
  id: string;
  code: string;
  label: string;
  description: string | null;
  sortOrder: number;
};

export type PosCashDenominationCount = {
  businessName: string;
  userName: string;
  countedAt: string;
  lines: Array<{
    label: string;
    value: number;
    quantity: number;
    subtotal: number;
  }>;
  total: number;
};
export type PosBankAccount = {
  bankAccountId:string;displayName:string;bankName:string;accountNumber:string;
  accountTypeName:string;isPrimary:boolean;rowVersion:string;
};
export type PosSettlementConfiguration = {
  isAccountingEnabled: boolean;
  bankAccounts: PosBankAccount[];
};

export interface PosClient {
  readonly mode: "edge" | "online";
  health(): Promise<{
    status: string;
    serverConnected: boolean;
    pushConnected: boolean;
    deviceSeriesCode: string;
    businessId: string;
    warehouseId: string;
    businessName: string;
    warehouseName: string;
    warehouseAllowsNegativeStockSales: boolean;
    userDisplayName: string;
    userId: string | null;
    workSessionId?: string | null;
    deviceId?: string | null;
    fiscalReady: boolean;
    fiscalWarnings: string[];
    dianQuotaAvailable: boolean | null;
    identityReady: boolean;
    catalogStatus: string;
    synchronizationInProgress: boolean;
    lastSynchronizationAt: string | null;
    lastSynchronizationFailed: boolean;
    pendingSynchronizationCount: number;
    oldestPendingSynchronizationAt: string | null;
    lastSynchronizationError: string | null;
    catalogUpdatedAt: string | null;
    permissions?: string[];
  }>;
  synchronizeNow(): Promise<void>;
  synchronizationEvents(take?: number): Promise<PosSynchronizationEvent[]>;
  referenceOptions(catalogCode: string): Promise<PosReferenceOption[]>;
  settlementConfiguration(): Promise<PosSettlementConfiguration>;
  openCashDrawer(): Promise<void>;
  readScaleWeight(): Promise<{ weight: number; unit: string; portName: string }>;
  searchProducts(search?: string, skip?: number, take?: number, customerId?: string | null): Promise<PosCatalogSearchPage>;
  productWarehouseAvailability(productId: string): Promise<PosProductWarehouseAvailability[]>;
  searchCustomers(search?: string, skip?: number, take?: number): Promise<PosCustomerSearchPage>;
  customer(customerId: string): Promise<PosCustomer>;
  customerCountries(): Promise<PosCountry[]>;
  customerDivisions(countryId: string): Promise<PosAdministrativeDivision[]>;
  customerCities(divisionId: string): Promise<PosCity[]>;
  createCustomer(input: PosCreateCustomerInput): Promise<PosCustomer>;
  createApproval(input: PosApprovalCreateInput): Promise<PosApprovalSummary>;
  approval(approvalRequestId: string): Promise<PosApprovalSummary>;
  activeDraft(): Promise<PosDraft>;
  nextNumbers(documentType?: PosSaleDocumentType): Promise<PosNextNumbers | null>;
  capture(value: string, customerId: string | null): Promise<PosCaptureResult>;
  captureSelectedProduct(
    product: PosCatalogProduct,
    customerId: string | null,
  ): Promise<PosCaptureResult>;
  changeQuantity(draftId: string, lineId: string, quantity: number): Promise<PosCaptureResult>;
  setDiscount(draftId: string, lineId: string, discount: number, authorization?: PosSensitiveAuthorization): Promise<PosDraft>;
  updateLines(draftId: string, lines: PosDraftLineUpdate[], authorization?: PosSensitiveAuthorization): Promise<PosDraft>;
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
  validateDraftInventory(draftId: string): Promise<PosInventoryValidation>;
  previewSettlement(draftId: string): Promise<PosSaleSettlement>;
  completeSale(
    draftId: string,
    customerIdentification: string | null,
    payments: PosPaymentInput[],
    documentType: PosSaleDocumentType,
    credit?: PosCreditTerms | null,
    fiscalHabilitationOnly?: boolean,
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
    paymentReference?: string | null,
    bankAccountId?: string | null,
    paymentNotes?: string | null,
  ): Promise<InvoiceOrdersResponse>;
  cashMovementReasons(direction: PosCashMovementDirection): Promise<PosCashMovementReason[]>;
  confirmCashMovement(input: PosCashMovementInput): Promise<PosCashMovementAcceptance>;
  printCashMovement(ticket: PosCashMovementTicket): Promise<void>;
  printCashDenominationCount(ticket: PosCashDenominationCount): Promise<void>;
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
  workSessionId: string;
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
  posOutputFormat: PosPrintTemplateFormat;
  ordersOutputFormat: PosPrintTemplateFormat;
  templateRoutes: Array<{
    documentType: "SalesInvoice" | "SalesReceipt";
    format: PosPrintTemplateFormat;
    printerName: string | null;
  }> | null;
  scale: PosScaleConfiguration | null;
  posPrinterName?: string | null;
  ordersPrinterName?: string | null;
  ordersReceiptPaperWidthMillimeters?: 58 | 80;
};

export type PosPrintTemplateFormat =
  | "Receipt"
  | "HalfLetter"
  | "HalfLegal"
  | "Letter";

export type PosScaleConfiguration = {
  enabled: boolean;
  portName: string;
  baudRate: number;
  dataBits: number;
  parity: "None" | "Even" | "Odd" | "Mark" | "Space";
  stopBits: "One" | "OnePointFive" | "Two";
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
      serverConnected: boolean;
      pushConnected: boolean;
      deviceSeriesCode: string;
      businessId: string;
      warehouseId: string;
      businessName: string;
      warehouseName: string;
      warehouseAllowsNegativeStockSales: boolean;
      userDisplayName: string;
      userId: string | null;
      workSessionId: string | null;
      deviceId: string;
      permissions: string[];
      fiscalReady: boolean;
      fiscalWarnings: string[];
      dianQuotaAvailable: boolean | null;
      identityReady: boolean;
      catalogStatus: string;
      synchronizationInProgress: boolean;
      lastSynchronizationAt: string | null;
      lastSynchronizationFailed: boolean;
      pendingSynchronizationCount: number;
      oldestPendingSynchronizationAt: string | null;
      lastSynchronizationError: string | null;
      catalogUpdatedAt: string | null;
    }>("/edge/v1/health");
  }


  synchronizeNow() {
    return this.requestVoid("/edge/v1/synchronization/refresh", { method: "POST" });
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

  printReceipt(
    receipt: PosPrintableReceipt,
    branding?: TenantBranding | null,
    workflow: "pos" | "orders" = "pos",
  ) {
    return this.requestVoid(`/edge/v1/print/receipt?workflow=${workflow}`, {
      method: "POST",
      body: JSON.stringify({
        ...receipt,
        companyName: branding?.displayName ?? branding?.legalName ?? receipt.companyName ?? null,
        companyLogoSource: branding?.logoUrl ?? receipt.companyLogoSource ?? null,
      }),
    });
  }
  synchronizationEvents(take = 100) {
    return this.request<PosSynchronizationEvent[]>(
      `/edge/v1/synchronization/events?take=${take}`,
    );
  }

  referenceOptions(catalogCode: string) {
    return this.request<PosReferenceOption[]>(
      `/edge/v1/reference-options/${encodeURIComponent(catalogCode)}`,
    );
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

  async closeWorkSession(input: PosCloseWorkSessionInput) {
    const result = await this.request<{
      closure: PosWorkSessionClosure;
      printedDirectly: boolean;
      printError: string | null;
    }>("/edge/v1/work-sessions/current/close", {
      method: "POST", body: JSON.stringify(input),
    });
    if (!result.printedDirectly)
      await printWorkSessionClosure(workSessionClosureHtml(result.closure))
        .catch(() => undefined);
    return result.closure;
  }

  printCashDenominationCount(ticket: PosCashDenominationCount) {
    return this.requestVoid("/edge/v1/print/cash-denomination-count", {
      method: "POST", body: JSON.stringify(ticket),
    });
  }
  settlementConfiguration() {
    return this.request<PosSettlementConfiguration>("/edge/v1/settlement-configuration");
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
    window.localStorage.setItem("auraly.pos.user-session", session.token);
    return session;
  }

  async completeEnrollment() {
    const session = await this.request<PosLocalUserSession>(
      "/edge/v1/auth/complete-enrollment",
      { method: "POST" },
    );
    if (!session.token) throw new PosEdgeError("El servicio local no devolvió la sesión inicial.", 500);
    this.userSessionToken = session.token;
    window.localStorage.setItem("auraly.pos.user-session", session.token);
    return session;
  }

  openWorkSession() {
    return this.request<PosLocalUserSession>("/edge/v1/work-sessions/current", {
      method: "POST",
    });
  }

  async logout() {
    try {
      if (this.userSessionToken) {
        await this.requestVoid("/edge/v1/auth/logout", { method: "POST" });
      }
    } catch {
      // Closing the browser-held session must remain possible while Edge restarts
      // or the server is unavailable. The opaque local token is removed below.
    } finally {
      this.userSessionToken = null;
      window.localStorage.removeItem("auraly.pos.user-session");
    }
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

  productWarehouseAvailability(productId: string) {
    return this.request<PosProductWarehouseAvailability[]>(
      `/edge/v1/catalog/products/${productId}/warehouse-availability`,
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
    return this.requestDomainResult<PosCaptureResult>("/edge/v1/capture", {
      method: "POST",
      body: JSON.stringify({ value, customerId }),
    }, [404, 409]);
  }

  captureSelectedProduct(
    product: PosCatalogProduct,
    customerId: string | null,
  ) {
    return this.capture(product.productCode, customerId);
  }

  changeQuantity(draftId: string, lineId: string, quantity: number) {
    return this.requestDomainResult<PosCaptureResult>(
      `/edge/v1/drafts/${draftId}/lines/${lineId}/quantity`,
      {
        method: "PUT",
        body: JSON.stringify({ quantity }),
      },
      [409],
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
    credit: PosCreditTerms | null = null,
    fiscalHabilitationOnly = false,
  ) {
    if (fiscalHabilitationOnly)
      return Promise.reject(new PosEdgeError(
        "La habilitación DIAN requiere conexión con Auraly Server.", 409));
    if (credit)
      return Promise.reject(new PosEdgeError(
        "La venta a crédito requiere conexión para validar el cupo actual del cliente.", 409));
    return this.request<PosEdgeCompleteSaleResult>(
      `/edge/v1/drafts/${draftId}/complete`,
      {
        method: "POST",
        body: JSON.stringify({ customerIdentification, payments, documentType }),
      },
    ).then((result) => ({
      ...result,
      receipt: {
        ...result.receipt,
        documentId: result.receipt.documentId.value,
        customerName: result.receipt.customerName || result.receipt.customerIdentification,
        fiscalStatus: result.receipt.fiscalStatus || "LocallyIssuedPendingSync",
      },
      printPreviewOpened: result.printedDirectly === false,
    } satisfies PosCompleteSaleResult));
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

  validateDraftInventory(draftId: string) {
    return this.request<PosInventoryValidation>(
      `/edge/v1/drafts/${draftId}/inventory-validation`,
    );
  }

  previewSettlement(draftId: string) {
    return this.request<PosSaleSettlement>(
      `/edge/v1/drafts/${draftId}/settlement`,
    );
  }

  updateLines(draftId: string, lines: PosDraftLineUpdate[], authorization?: PosSensitiveAuthorization) {
    return this.request<PosDraft>(`/edge/v1/drafts/${draftId}/lines`, {
      method: "PUT",
      body: JSON.stringify({ lines }),
      headers: sensitiveHeaders(authorization),
    });
  }

  approval(approvalRequestId: string) {
    return this.request<PosApprovalSummary>(
      `/edge/v1/approvals/${encodeURIComponent(approvalRequestId)}`,
    );
  }

  renewRecoveredOrder(orderId: string) {
    return this.request(`/edge/v1/orders/${orderId}/claim`, { method: "POST" });
  }

  releaseRecoveredOrder(orderId: string) {
    return this.request(`/edge/v1/orders/${orderId}/claim/release`, {
      method: "POST",
      keepalive: true,
    });
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
    paymentReference?: string | null,
    bankAccountId?: string | null,
    paymentNotes?: string | null,
  ) {
    return this.request<InvoiceOrdersResponse>("/edge/v1/orders/invoice", {
      method: "POST",
      body: JSON.stringify({
        orderIds,
        paymentMethodCode,
        paymentReference: paymentReference ?? null,
        bankAccountId: bankAccountId ?? null,
        paymentNotes: paymentNotes ?? null,
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
        announceEdgeLoginReplacement(response.status, problem.code || problem.title);
        throw new PosEdgeError(detail, response.status, problem.code || problem.title);
      } catch (parsed) {
        if (parsed instanceof PosEdgeError) throw parsed;
        // The local host may intentionally return plain text for simple failures.
      }
      throw new PosEdgeError(detail, response.status);
    }
    return (await response.json()) as T;
  }

  printCashMovement(ticket: PosCashMovementTicket) {
    return this.requestVoid("/edge/v1/print/cash-movement", {
      method: "POST", body: JSON.stringify(ticket),
    });
  }

  private async requestDomainResult<T>(
    path: string,
    init: RequestInit,
    acceptedStatuses: number[],
  ): Promise<T> {
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
    if (response.ok || acceptedStatuses.includes(response.status))
      return (await response.json()) as T;
    const raw = await response.text();
    let detail = raw || response.statusText;
    try {
      const problem = JSON.parse(raw) as { detail?: string; title?: string; code?: string };
      detail = problem.detail || problem.title || detail;
      announceEdgeLoginReplacement(response.status, problem.code || problem.title);
      throw new PosEdgeError(detail, response.status, problem.code || problem.title);
    } catch (parsed) {
      if (parsed instanceof PosEdgeError) throw parsed;
    }
    throw new PosEdgeError(detail, response.status);
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
        const problem = JSON.parse(raw) as { detail?: string; title?: string; code?: string };
        detail = problem.detail || problem.title || detail;
        announceEdgeLoginReplacement(response.status, problem.code || problem.title);
      } catch {
        // The local host may intentionally return plain text for simple failures.
      }
      throw new PosEdgeError(detail, response.status);
    }
  }
}

export function readEdgeUserSession(): string | null {
  return window.localStorage.getItem("auraly.pos.user-session");
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
