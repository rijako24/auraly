import type { AddInvoiceCharge, AppliedInvoiceCharge, InvoiceChargePage } from "@/services/api/invoice-charges";
import type { TenantBranding } from "@/services/api/tenants";
import type { InventoryReasonItem } from "@/services/api/inventory";
import type { ReferenceOption } from "@/services/api/reference-options";
import type { PartyRoleOptionPage } from "@/services/api/parties";
import type { ConfirmCustomerPaymentRequest,CustomerPaymentAcceptance,PaymentSettlementConfiguration,ReceivablePage } from "@/services/api/receivables";
import type { ConfirmSupplierPaymentRequest,PayablePage,SupplierPaymentAcceptance } from "@/services/api/payables";
import type { SellerOrderResult } from "@/services/api/seller-orders";
import { buildPosOrderUpdateLines } from "@/services/orders/pos-order-update-lines";
import type {
  CommerceOrderDetail,
  CommerceOrderClaim,
  CommerceOrderFilters,
  CommerceOrderPage,
  CommerceOrderPrintDocument,
  InvoiceOrdersResponse,
  OrderCreditValidationIssue,
  OrderInvoiceChargeSelection,
} from "@/services/orders/commerce-orders-client";
import {
  invoiceOrdersInSequence,
  orderReceiptsFromEmission,
  orderReceiptsForPrinting,
  type OrderInvoiceSequenceProgress,
} from "./pos-order-print-routing";
import { toPrintableOrder } from "./pos-order-print-document";
import type {
  ConfirmSalesReturnRequest,
  ReturnableSale,
  ReturnableSalePage,
  SalesSettlementConfiguration,
  SalesReturnAcceptance,
} from "@/services/api/sales-returns";
import {
  buildServerIssuedSalesSearchRequest,
  mapServerIssuedSalesPage,
  type ServerIssuedSalePage,
} from "./pos-server-history-request";
import { printWorkSessionClosure } from "./pos-work-session-close";
import {
  announceSessionReplacement,
  runLocalPosSessionReplacement,
} from "@/lib/auth-session";
import { buildLoginRedirect } from "@/lib/login-redirect";
import { isCurrentEdgeUserSession } from "./pos-edge-session";
import {
  createPosStateInvalidationNotifier,
  isChangedPosStateEvent,
  posStateStreamReconnectDelay,
} from "./pos-state-invalidation";
import { readPosEdgeProblem } from "./pos-printer-configuration";
import { resolveSalePrintEffect } from "./pos-print-routing";
import { buildCompatibleEdgeLineUpdates } from "./pos-edge-line-update";

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
  priceSource: "Public" | "Base" | "PriceChannel" | "Promotion" | "Promotion+PriceChannel";
  promotionDiscount?: number;
};
export type PosCatalogSearchPage = {
  items: PosCatalogProduct[];
  hasMore: boolean;
  nextOffset: number | null;
};

export type PosCustomer = {
  customerId: string;
  partySiteId?: string | null;
  siteName?: string | null;
  siteAddress?: string | null;
  identification: string;
  name: string;
  priceChannelId: string | null;
  requiresElectronicInvoice: boolean;
  isCreditEnabled?: boolean;
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
  fiscalNumber: string | null;
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
  promotionDiscount?: number;
  documentUnitCost: number;
  allowsDocumentCostOverride: boolean;
  allowsFractionalSale: boolean;
  net: number;
  tax: number;
  total: number;
  publicUnitPrice: number;
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
  "lineId" | "description" | "publicUnitPrice" | "discount" | "documentUnitCost"
>;

export type PosDraft = {
  draftId: DraftId;
  scope?: { businessId: string; warehouseId: string; deviceId: string; workSessionId: string; userId: string };
  customerId: string | null;
  customerPartySiteId?: string | null;
  sellerId: string | null;
  status: string;
  name: string | null;
  reference: string | null;
  observation: string | null;
  lines: PosDraftLine[];
  charges?: AppliedInvoiceCharge[];
  untaxedAmount: number;
  taxAmount: number;
  payableAmount: number;
  sourceOrderId?: string | null;
  updatedAt?: string;
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
  maximumQuantity?: number | null;
};

export type PosIssuedSaleFilters = {
  search: string;
  customerId: string | null;
  partySiteId: string | null;
  from: string;
  to: string;
  productId: string | null;
  minimumTotal: number | null;
  maximumTotal: number | null;
};

export type PosServerHistoryScope = {
  businessId: string;
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
  roundingAdjustment?: number;
  reference: string | null;
  cardFranchiseCode?: string | null;
  approvalNumber?: string | null;
  bankAccountId?: string | null;
  notes?: string | null;
  tenderedAmount?: number | null;
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
  unitCode?: string;
};

export type PosPrintableReceipt = {
  documentId: string;
  documentType: PosSaleDocumentType | "Order";
  documentNumber: string;
  fiscalNumber: string | null;
  issuedAt: string;
  customerIdentification: string;
  customerName: string;
  customerPhone?: string | null;
  customerAddress?: string | null;
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
  invoicePrintDetails?: {
    supplierName: string;
    supplierIdentification: string;
    supplierTaxResponsibility: string;
    supplierAddress: string;
    customerAddress: string;
    authorizationNumber: string;
    authorizationValidFrom: string;
    authorizationValidUntil: string;
    authorizationPrefix: string;
    authorizationRangeStart: number;
    authorizationRangeEnd: number;
    paymentFormCode: string;
    paymentMeansCode: string;
    paymentDueDate: string;
    softwareProviderIdentification: string;
    softwareName: string;
  } | null;
  creditAcknowledgement?: {
    documentId: string;
    documentNumber: string;
    issuedAt: string;
    customerName: string;
    customerIdentification: string;
    creditAmount: number;
    remainingCredit: number | null;
    soldByName: string;
    companyName?: string | null;
    companyLogoSource?: string | null;
    businessName?: string | null;
    warehouseName?: string | null;
  } | null;
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
  printCompletion?: Promise<void>;
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

function announceEdgeLoginReplacement(
  status: number,
  code: string | undefined,
  requestSessionToken: string | null,
): void {
  if (status !== 401 || code !== "LoginReplaced" || typeof window === "undefined") return;
  if (!isCurrentEdgeUserSession(
    requestSessionToken,
    window.localStorage.getItem("auraly.pos.user-session"),
  )) return;
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
  cashEntryAmount?: number;
  cashExitAmount?: number;
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
export type PosCreditTerms = { amount: number };

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

export type WorkSessionInvoiceCharge = {
  documentId: string; documentNumber: string; appliedChargeId: string; chargeId: string;
  code: string; name: string; supplierName: string; amount: number; invoicedAmount: number;
  expenseAmount: number; withholdingAmount: number;
  payments: Array<{ paymentNumber: number; paymentMethodCode: string; amount: number }>;
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
  creditSales?: Array<{ customerName: string; documentNumber: string; amount: number }> | null;
  returnCount: number;
  note: string | null;
  paymentTotals: PosWorkSessionPaymentTotal[];
  receiptTemplateVersion?: number;
  invoiceCharges?: WorkSessionInvoiceCharge[] | null;
  cashMovements?: Array<{
    documentId: string;
    direction: "In" | "Out";
    documentNumber: string;
    reasonName: string;
    amount: number;
    occurredAt: string;
    responsibleName: string;
    reference: string | null;
    notes: string | null;
  }> | null;
};

export type PosWorkSessionClosurePreview = Omit<
  PosWorkSessionClosure,
  "workSessionClosureId" | "closedAt" | "countedCash" | "cashDifference" | "note" | "receiptTemplateVersion"
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
    initialEnrollmentSessionAvailable?: boolean;
    catalogStatus: string;
    synchronizationInProgress: boolean;
    automaticRetryScheduled?: boolean;
    automaticRetryAttempt?: number;
    lastSynchronizationAt: string | null;
    lastSynchronizationFailed: boolean;
    pendingSynchronizationCount: number;
    oldestPendingSynchronizationAt: string | null;
    lastSynchronizationError: string | null;
    catalogUpdatedAt: string | null;
    permissions?: string[];
    catalogProcessedProducts?: number;
    catalogTotalProducts?: number;
    catalogProgressPercent?: number | null;
    preparationStage?: "Identity" | "CatalogStarting" | "Catalog" | "Finalizing";
    preparationCompletedSteps?: number;
    preparationTotalSteps?: number;
    preparationCanResume?: boolean;
    synchronizationStages?: string[];
    failedSynchronizationStage?: string | null;
  }>;
  synchronizeNow(): Promise<void>;
  synchronizationEvents(take?: number): Promise<PosSynchronizationEvent[]>;
  referenceOptions(catalogCode: string): Promise<PosReferenceOption[]>;
  settlementConfiguration(): Promise<PosSettlementConfiguration>;
  openCashDrawer(): Promise<void>;
  readScaleWeight(): Promise<{ weight: number; unit: string; portName: string }>;
  searchProducts(search?: string, skip?: number, take?: number, customerId?: string | null, publicPriceOnly?: boolean): Promise<PosCatalogSearchPage>;
  productWarehouseAvailability(
    productId: string,
    signal?: AbortSignal,
  ): Promise<PosProductWarehouseAvailability[]>;
  searchCustomers(search?: string, skip?: number, take?: number): Promise<PosCustomerSearchPage>;
  customer(customerId: string, partySiteId?: string | null): Promise<PosCustomer>;
  customerCountries(): Promise<PosCountry[]>;
  customerDivisions(countryId: string): Promise<PosAdministrativeDivision[]>;
  customerCities(divisionId: string): Promise<PosCity[]>;
  createCustomer(input: PosCreateCustomerInput): Promise<PosCustomer>;
  createApproval(input: PosApprovalCreateInput): Promise<PosApprovalSummary>;
  approval(approvalRequestId: string): Promise<PosApprovalSummary>;
  invoiceCharges(page?: number): Promise<InvoiceChargePage>;
  orders(filters: CommerceOrderFilters): Promise<CommerceOrderPage>;
  order(orderId: string): Promise<CommerceOrderDetail>;
  recoverOrder(orderId: string): Promise<PosDraft>;
  renewRecoveredOrder(orderId: string): Promise<CommerceOrderClaim>;
  releaseRecoveredOrder(orderId: string): Promise<{ released: boolean }>;
  saveOrder(draft: PosDraft): Promise<{ order: SellerOrderResult; nextDraft: PosDraft }>;
  printOrders(orderIds: string[]): Promise<{ printedCount: number }>;
  invoiceOrders(
    orderIds: string[], paymentMethodCode: string,
    documentType: "SalesInvoice" | "SalesReceipt", paymentReference?: string | null,
    bankAccountId?: string | null, paymentNotes?: string | null, printAfterInvoice?: boolean,
    idempotencyKey?: string, onProgress?: (progress: OrderInvoiceSequenceProgress) => void,
    includeCreditAcknowledgement?: boolean, charge?: OrderInvoiceChargeSelection | null,
  ): Promise<InvoiceOrdersResponse>;
  saveCharge(draftId: string, input: AddInvoiceCharge): Promise<PosDraft>;
  removeCharge(draftId: string, appliedChargeId: string): Promise<PosDraft>;
  activeDraft(): Promise<PosDraft>;
  nextNumbers(documentType?: PosSaleDocumentType): Promise<PosNextNumbers | null>;
  capture(value: string, customerId: string | null, quantity?: number): Promise<PosCaptureResult>;
  captureSelectedProduct(
    product: PosCatalogProduct,
    customerId: string | null,
    quantity?: number,
  ): Promise<PosCaptureResult>;
  changeQuantity(draftId: string, lineId: string, quantity: number): Promise<PosCaptureResult>;
  setDiscount(draftId: string, lineId: string, discount: number, authorization?: PosSensitiveAuthorization): Promise<PosDraft>;
  updateLines(draftId: string, lines: PosDraftLineUpdate[], includesProratedDiscount?: boolean): Promise<PosDraft>;
  selectCustomer(draftId: string, customerId: string | null, partySiteId?: string | null): Promise<PosCustomerSelection>;
  removeLine(draftId: string, lineId: string, authorization?: PosSensitiveAuthorization): Promise<PosDraft>;
  discardUnpricedGenericLine(draftId: string, lineId: string): Promise<PosDraft>;
  cancelDraft(draftId: string, authorization?: PosSensitiveAuthorization): Promise<PosDraft>;
  saveTemporary(
    draftId: string,
    name: string,
    reference: string,
    observation: string,
  ): Promise<PosDraft>;
  temporaries(search?: string): Promise<PosDraft[]>;
  deleteTemporary(draftId: string, authorization?: PosSensitiveAuthorization): Promise<void>;
  recoverTemporary(draftId: string): Promise<PosDraft>;
  validateDraftInventory(draftId: string): Promise<PosInventoryValidation>;
  previewSettlement(draftId: string): Promise<PosSaleSettlement>;
  completeSale(
    draftId: string,
    customerIdentification: string | null,
    payments: PosPaymentInput[],
    documentType: PosSaleDocumentType,
    credit?: PosCreditTerms | null,
    authorization?: PosSensitiveAuthorization,
  ): Promise<PosCompleteSaleResult>;
  searchIssuedSales(search?: string, skip?: number, take?: number): Promise<PosIssuedSaleSearchPage>;
  searchServerIssuedSales(
    context: PosServerHistoryScope,
    filters: PosIssuedSaleFilters,
    skip?: number,
    take?: number,
  ): Promise<PosIssuedSaleSearchPage>;
  searchServerHistoryCustomers(
    context: PosServerHistoryScope,
    search: string,
    skip?: number,
    take?: number,
  ): Promise<PosCustomerSearchPage>;
  searchServerHistoryProducts(
    context: PosServerHistoryScope,
    search: string,
    skip?: number,
    take?: number,
  ): Promise<PosCatalogSearchPage>;
  loadServerIssuedSaleReceipt(
    context: PosServerHistoryScope,
    documentId: string,
  ): Promise<PosPrintableReceipt>;
  recordServerIssuedSaleReprint(
    context: PosServerHistoryScope,
    documentId: string,
  ): Promise<void>;
  searchServerReturnableSales(
    context: PosSalesReturnContext,
    query: PosSalesReturnQuery,
  ): Promise<ReturnableSalePage>;
  loadServerReturnableSale(
    context: PosSalesReturnContext,
    documentId: string,
  ): Promise<ReturnableSale>;
  loadServerSalesReturnBootstrap(
    context: PosSalesReturnContext,
  ): Promise<PosSalesReturnBootstrap>;
  confirmServerSalesReturn(
    request: ConfirmSalesReturnRequest,
  ): Promise<SalesReturnAcceptance>;
  reprint(documentId: string): Promise<void>;
  printHistoricalReceipt(receipt: PosPrintableReceipt): Promise<void>;
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
  templateRoutes: Array<{
    documentType: "SalesInvoice" | "SalesReceipt";
    format: PosPrintTemplateFormat;
    printerName: string | null;
  }> | null;
  scale: PosScaleConfiguration | null;
  posPrinterName?: string | null;
  orderOutputFormat: PosPrintTemplateFormat;
  orderPrinterName?: string | null;
  orderReceiptPaperWidthMillimeters?: 58 | 80;
};

export type PosSalesReturnContext = {
  businessId: string;
  workSessionId: string | null;
};

export type PosSalesReturnQuery = {
  page: number;
  pageSize: number;
  search?: string;
  customer?: string;
  from?: string;
  to?: string;
  withAvailableQuantity?: boolean;
};

export type PosSalesReturnBootstrap = {
  reasons: InventoryReasonItem[];
  resolutionMethods: ReferenceOption[];
  scopes: ReferenceOption[];
  settlementConfiguration: SalesSettlementConfiguration;
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
  printingReady?: boolean;
  validationErrors?: string[];
  peripheralWarnings?: string[];
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
    orderOutputFormat: "HalfLetter",
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
  private latestHealth: Awaited<ReturnType<PosClient["health"]>> | null = null;

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
      initialEnrollmentSessionAvailable: boolean;
      catalogStatus: string;
      synchronizationInProgress: boolean;
      automaticRetryScheduled?: boolean;
      automaticRetryAttempt?: number;
      lastSynchronizationAt: string | null;
      lastSynchronizationFailed: boolean;
      pendingSynchronizationCount: number;
      oldestPendingSynchronizationAt: string | null;
      lastSynchronizationError: string | null;
      catalogUpdatedAt: string | null;
      catalogProcessedProducts: number;
      catalogTotalProducts: number;
      catalogProgressPercent: number | null;
      preparationStage: "Identity" | "CatalogStarting" | "Catalog" | "Finalizing";
      preparationCompletedSteps: number;
      preparationTotalSteps: number;
      preparationCanResume: boolean;
      synchronizationStages: string[];
      failedSynchronizationStage: string | null;
    }>("/edge/v1/health").then((health) => {
      this.latestHealth = health;
      return health;
    });
  }


  synchronizeNow() {
    return this.requestVoid("/edge/v1/synchronization/refresh", { method: "POST" });
  }

  restartEnrollment() {
    return this.requestVoid("/edge/v1/enrollment/restart", { method: "POST" });
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
    workflow: "pos" | "order-tickets" = "pos",
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

  renderWorkSessionClosure(closure: PosWorkSessionClosure) {
    return this.request<{ html: string }>("/edge/v1/render/work-session-closure", {
      method: "POST", body: JSON.stringify(closure),
    });
  }

  watchLocalState(onStateChanged: () => void): () => void {
    const controller = new AbortController();
    let reconnectTimer: ReturnType<typeof setTimeout> | null = null;
    let reconnectAttempt = 0;
    let connectedOnce = false;
    let listening = false;
    const invalidations = createPosStateInvalidationNotifier(
      () => {
        if (!controller.signal.aborted) onStateChanged();
      },
      (notify) => {
        const timer = setTimeout(notify, 250);
        return () => clearTimeout(timer);
      },
    );
    const scheduleReconnect = () => {
      if (controller.signal.aborted || reconnectTimer !== null) return;
      invalidations.notify();
      const delay = posStateStreamReconnectDelay(reconnectAttempt);
      reconnectAttempt += 1;
      if (delay === null) return;
      reconnectTimer = setTimeout(() => {
        reconnectTimer = null;
        void listen();
      }, delay);
    };
    const listen = async () => {
      if (controller.signal.aborted || listening) return;
      listening = true;
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
        if (!response.ok || !response.body) {
          scheduleReconnect();
          return;
        }

        const recoveredConnection = connectedOnce;
        connectedOnce = true;
        reconnectAttempt = 0;
        if (recoveredConnection) invalidations.notify();

        const reader = response.body.getReader();
        const decoder = new TextDecoder();
        let pending = "";
        while (!controller.signal.aborted) {
          const next = await reader.read();
          if (next.done) break;
          pending += decoder.decode(next.value, { stream: true });
          let boundary = pending.indexOf("\n\n");
          while (boundary >= 0) {
            const event = pending.slice(0, boundary);
            pending = pending.slice(boundary + 2);
            if (isChangedPosStateEvent(event)) invalidations.notify();
            boundary = pending.indexOf("\n\n");
          }
        }
        scheduleReconnect();
      } catch {
        // The loopback host can restart while the application stays open.
        scheduleReconnect();
      } finally {
        listening = false;
      }
    };
    const restart = () => {
      if (document.visibilityState !== "visible" || controller.signal.aborted ||
          listening || reconnectTimer !== null) return;
      reconnectAttempt = 0;
      void listen();
    };
    window.addEventListener("online", restart);
    document.addEventListener("visibilitychange", restart);
    void listen();
    return () => {
      controller.abort();
      if (reconnectTimer !== null) clearTimeout(reconnectTimer);
      window.removeEventListener("online", restart);
      document.removeEventListener("visibilitychange", restart);
      invalidations.dispose();
    };
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
    if (!result.printedDirectly) {
      const rendered = await this.renderWorkSessionClosure(result.closure);
      await printWorkSessionClosure(rendered.html).catch(() => undefined);
    }
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
    const session = await runLocalPosSessionReplacement(
      () => {
        const previousSession = this.userSessionToken;
        this.userSessionToken = null;
        if (isCurrentEdgeUserSession(
          previousSession,
          window.localStorage.getItem("auraly.pos.user-session"),
        )) window.localStorage.removeItem("auraly.pos.user-session");
      },
      () => this.request<PosLocalUserSession>("/edge/v1/auth/login", {
        method: "POST",
        body: JSON.stringify({ username, password }),
      }),
    );
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

  async openWorkSession() {
    const session = await this.request<PosLocalUserSession>("/edge/v1/work-sessions/current", {
      method: "POST",
    });
    if (this.latestHealth) {
      this.latestHealth = {
        ...this.latestHealth,
        userId: session.userId,
        userDisplayName: session.displayName,
        workSessionId: session.workSessionId,
        permissions: session.permissions,
      };
    }
    return session;
  }

  async logout() {
    const sessionToClose = this.userSessionToken;
    try {
      if (sessionToClose) {
        await this.requestVoid("/edge/v1/auth/logout", { method: "POST" });
      }
    } catch {
      // Closing the browser-held session must remain possible while Edge restarts
      // or the server is unavailable. The opaque local token is removed below.
    } finally {
      if (this.userSessionToken === sessionToClose) this.userSessionToken = null;
      if (isCurrentEdgeUserSession(
        sessionToClose,
        window.localStorage.getItem("auraly.pos.user-session"),
      )) window.localStorage.removeItem("auraly.pos.user-session");
    }
  }

  searchProducts(search = "", skip = 0, take = 50, customerId: string | null = null, publicPriceOnly = false) {
    const query = new URLSearchParams({
      search,
      skip: String(skip),
      take: String(take),
    });
    if (customerId) query.set("customerId", customerId);
    if (publicPriceOnly) query.set("publicPriceOnly", "true");
    return this.request<PosCatalogSearchPage>(
      `/edge/v1/catalog/products?${query}`,
    );
  }

  productWarehouseAvailability(productId: string, signal?: AbortSignal) {
    return this.request<PosProductWarehouseAvailability[]>(
      `/edge/v1/catalog/products/${productId}/warehouse-availability`,
      { signal },
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
  customer(customerId: string, partySiteId: string | null = null) {
    const query = partySiteId ? `?partySiteId=${encodeURIComponent(partySiteId)}` : "";
    return this.request<PosCustomer>(`/edge/v1/customers/${customerId}${query}`);
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

  capture(value: string, customerId: string | null, quantity?: number) {
    return this.requestDomainResult<PosCaptureResult>("/edge/v1/capture", {
      method: "POST",
      body: JSON.stringify({ value, customerId, quantity }),
    }, [404, 409]);
  }

  captureSelectedProduct(
    product: PosCatalogProduct,
    customerId: string | null,
    quantity?: number,
  ) {
    return this.capture(product.productCode, customerId, quantity);
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

  selectCustomer(draftId: string, customerId: string | null, partySiteId: string | null = null) {
    return this.request<PosCustomerSelection>(
      `/edge/v1/drafts/${draftId}/customer`,
      {
        method: "PUT",
        body: JSON.stringify({ customerId, partySiteId }),
      },
    );
  }

  invoiceCharges(page = 1) {
    return this.request<InvoiceChargePage>(`/edge/v1/invoice-charges?page=${page}`);
  }

  portfolioParties(role:"Customer"|"Supplier",search:string,page:number,pageSize:number){
    const query=new URLSearchParams({role,search,page:String(page),pageSize:String(pageSize)});
    return this.request<PartyRoleOptionPage>(`/edge/v1/portfolio/parties?${query}`);
  }
  portfolioReceivables(customerId:string){
    const query=new URLSearchParams({customerId,page:"1",pageSize:"100"});
    return this.request<ReceivablePage>(`/edge/v1/portfolio/receivables?${query}`);
  }
  portfolioPayables(supplierId:string){
    const query=new URLSearchParams({supplierId,page:"1",pageSize:"100"});
    return this.request<PayablePage>(`/edge/v1/portfolio/payables?${query}`);
  }
  portfolioSettlementConfiguration(){return this.request<PaymentSettlementConfiguration>("/edge/v1/portfolio/settlement-configuration");}
  confirmPortfolioReceivable(request:ConfirmCustomerPaymentRequest,idempotencyKey:string){return this.request<CustomerPaymentAcceptance>("/edge/v1/portfolio/receivable-payments",{method:"POST",headers:{"Content-Type":"application/json","Idempotency-Key":idempotencyKey},body:JSON.stringify(request)});}
  confirmPortfolioPayable(request:ConfirmSupplierPaymentRequest,idempotencyKey:string){return this.request<SupplierPaymentAcceptance>("/edge/v1/portfolio/payable-payments",{method:"POST",headers:{"Content-Type":"application/json","Idempotency-Key":idempotencyKey},body:JSON.stringify(request)});}

  orders(filters: CommerceOrderFilters) {
    const query = new URLSearchParams();
    Object.entries(filters).forEach(([key, value]) => {
      if (value !== undefined && value !== "") query.set(key, String(value));
    });
    return this.request<CommerceOrderPage>(`/edge/v1/orders?${query.toString()}`);
  }

  order(orderId: string) {
    return this.request<CommerceOrderDetail>(`/edge/v1/orders/${orderId}`);
  }

  recoverOrder(orderId: string) {
    const scope = this.requiredOrderScope();
    return this.request<PosDraft>(`/edge/v1/orders/${orderId}/recover`, {
      method: "POST",
      body: JSON.stringify({ workSessionId: scope.workSessionId, userId: scope.userId }),
    });
  }

  renewRecoveredOrder(orderId: string) {
    const scope = this.requiredOrderScope();
    return this.request<CommerceOrderClaim>(`/edge/v1/orders/${orderId}/claim`, {
      method: "POST",
      body: JSON.stringify({ workSessionId: scope.workSessionId, userId: scope.userId, leaseMinutes: 10 }),
    });
  }

  releaseRecoveredOrder(orderId: string) {
    const scope = this.requiredOrderScope();
    return this.request<{ released: boolean }>(`/edge/v1/orders/${orderId}/claim/release`, {
      method: "POST",
      body: JSON.stringify({ workSessionId: scope.workSessionId, userId: scope.userId }),
    });
  }

  async saveOrder(draft: PosDraft): Promise<{ order: SellerOrderResult; nextDraft: PosDraft }> {
    if (draft.charges?.length)
      throw new Error("Los cargos se guardan al facturar. Quita los cargos antes de guardar el pedido para evitar perderlos.");
    if (!draft.customerId || !draft.customerPartySiteId || !draft.lines.length)
      throw new Error("El pedido requiere cliente, sede y al menos un producto.");
    const lines = buildPosOrderUpdateLines(draft.lines);
    const scope = this.requiredOrderScope();
    const idempotencyKey = `pos-order-${draft.draftId.value}-${draft.updatedAt ?? crypto.randomUUID()}`;
    const order = draft.sourceOrderId
      ? await this.request<SellerOrderResult>(`/edge/v1/orders/${draft.sourceOrderId}`, {
          method: "PUT",
          body: JSON.stringify({
            customerId: draft.customerId,
            partySiteId: draft.customerPartySiteId,
            notes: draft.observation ?? null,
            idempotencyKey,
            lines,
            workSessionId: scope.workSessionId,
          }),
        })
      : await this.request<SellerOrderResult>("/edge/v1/orders", {
          method: "POST",
          body: JSON.stringify({
            businessId: scope.businessId,
            warehouseId: scope.warehouseId,
            customerId: draft.customerId,
            partySiteId: draft.customerPartySiteId,
            routeId: null,
            routeStopId: null,
            capturedOffline: false,
            notes: draft.observation ?? null,
            idempotencyKey,
            lines,
          }),
        });
    return { order, nextDraft: await this.clearAfterOnlineCommit(draft.draftId.value) };
  }

  async printOrders(orderIds: string[]) {
    const documents = await this.request<CommerceOrderPrintDocument[]>(
      "/edge/v1/orders/print-batch",
      { method: "POST", body: JSON.stringify({ orderIds }) },
    );
    for (const document of documents)
      await this.printReceipt(toPrintableOrder(document, {
        businessName: this.latestHealth?.businessName ?? "Empresa",
        warehouseName: this.latestHealth?.warehouseName ?? "Sede",
      }), null, "order-tickets");
    return { printedCount: documents.length };
  }

  async invoiceOrders(
    orderIds: string[], paymentMethodCode: string,
    documentType: "SalesInvoice" | "SalesReceipt", paymentReference?: string | null,
    bankAccountId?: string | null, paymentNotes?: string | null, printAfterInvoice = true,
    idempotencyKey = crypto.randomUUID(), onProgress?: (progress: OrderInvoiceSequenceProgress) => void,
    includeCreditAcknowledgement = false, charge?: OrderInvoiceChargeSelection | null,
  ): Promise<InvoiceOrdersResponse> {
    const scope = this.requiredOrderScope();
    const invoiceRequest = (requestedOrderIds: string[]) => ({
      workSessionId: scope.workSessionId, warehouseId: scope.warehouseId,
      userId: scope.userId, orderIds: requestedOrderIds, paymentMethodCode,
      paymentReference: paymentReference ?? null, bankAccountId: bankAccountId ?? null,
      paymentNotes: paymentNotes ?? null, documentType, charge: charge ?? null,
    });
    if (paymentMethodCode === "Credit") {
      const issues = await this.request<OrderCreditValidationIssue[]>(
        "/edge/v1/orders/invoice/credit-validation",
        { method: "POST", body: JSON.stringify(invoiceRequest(orderIds)) },
      );
      if (issues.length) return {
        operationId: "00000000-0000-0000-0000-000000000000", status: "CreditRejected",
        requestedCount: orderIds.length, completedCount: 0, failedCount: 0,
        isReplay: false, results: [], printStatus: "NotRequired", printError: null,
        creditValidationIssues: issues,
      };
    }
    const response = await invoiceOrdersInSequence(orderIds, idempotencyKey, {
      invoiceOne: (orderId, orderIdempotencyKey) =>
        this.request<InvoiceOrdersResponse>("/edge/v1/orders/invoice", {
          method: "POST",
          headers: { "Idempotency-Key": orderIdempotencyKey },
          body: JSON.stringify(invoiceRequest([orderId])),
        }),
      printOne: printAfterInvoice
        ? async (receipts) => {
            for (const receipt of orderReceiptsForPrinting(
              receipts,
              includeCreditAcknowledgement,
            )) await this.printReceipt(receipt, null, "pos");
          }
        : undefined,
      onProgress,
    });
    if (!printAfterInvoice) response.printStatus = "NotRequired";
    else if (orderReceiptsFromEmission(response.results).length > 0) {
      try {
        await this.openCashDrawer();
      } catch (error) {
        const drawerError = error instanceof Error
          ? error.message
          : "No fue posible abrir el cajón.";
        response.printStatus = "Failed";
        response.printError = [response.printError, drawerError].filter(Boolean).join(" · ");
      }
    }
    return response;
  }

  private requiredOrderScope() {
    const value = this.latestHealth;
    if (!value?.userId || !value.workSessionId)
      throw new PosEdgeError("Abre un turno antes de trabajar con pedidos.", 409);
    return { userId: value.userId, workSessionId: value.workSessionId,
      businessId: value.businessId, warehouseId: value.warehouseId };
  }

  saveCharge(draftId: string, input: AddInvoiceCharge) {
    return this.request<PosDraft>(`/edge/v1/drafts/${draftId}/charges/${input.appliedChargeId}`, {
      method: "PUT", body: JSON.stringify({ ...input, expectedVersion: 0 }),
    });
  }

  removeCharge(draftId: string, appliedChargeId: string) {
    return this.request<PosDraft>(`/edge/v1/drafts/${draftId}/charges/${appliedChargeId}`, { method: "DELETE" });
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

  deleteTemporary(draftId: string, authorization?: PosSensitiveAuthorization) {
    return this.requestVoid(`/edge/v1/temporaries/${draftId}`, {
      method: "DELETE",
      headers: sensitiveHeaders(authorization),
    });
  }

  recoverTemporary(draftId: string) {
    return this.request<PosDraft>(
      `/edge/v1/temporaries/${draftId}/recover`,
      { method: "POST" },
    );
  }

  async completeSale(
    draftId: string,
    customerIdentification: string | null,
    payments: PosPaymentInput[],
    documentType: PosSaleDocumentType,
    credit: PosCreditTerms | null = null,
    authorization?: PosSensitiveAuthorization,
  ) {
    const result = await this.request<PosEdgeCompleteSaleResult>(
      `/edge/v1/drafts/${draftId}/complete`,
      {
        method: "POST",
        headers: sensitiveHeaders(authorization),
        body: JSON.stringify({ customerIdentification, payments, documentType, credit }),
      },
    );
    const receipt: PosPrintableReceipt = {
      ...result.receipt,
      documentId: result.receipt.documentId.value,
      customerName: result.receipt.customerName || result.receipt.customerIdentification,
      fiscalStatus: result.receipt.fiscalStatus || "LocallyIssuedPendingSync",
    };
    const printEffect = resolveSalePrintEffect(result.issuedSale.wasAlreadyIssued);
    const printCompletion = result.printedDirectly || !printEffect.dispatchCopy
      ? undefined
      : new Promise<void>((resolve, reject) => {
          window.setTimeout(() => {
            void this.printReceipt(receipt, null, "pos").then(resolve, reject);
          }, 0);
        });
    return {
      ...result,
      receipt,
      printPreviewOpened: false,
      printCompletion,
    } satisfies PosCompleteSaleResult;
  }

  discardUnpricedGenericLine(draftId: string, lineId: string) {
    return this.request<PosDraft>(
      `/edge/v1/drafts/${draftId}/lines/${lineId}/discard-unpriced-generic`,
      { method: "POST" },
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

  searchServerIssuedSales(
    context: PosServerHistoryScope,
    filters: PosIssuedSaleFilters,
    skip = 0,
    take = 20,
  ) {
    return this.request<ServerIssuedSalePage>(
      "/edge/v1/server-history/sales/search",
      {
        method: "POST",
        body: JSON.stringify(buildServerIssuedSalesSearchRequest(
          context,
          filters,
          skip,
          take,
        )),
      },
    ).then(mapServerIssuedSalesPage);
  }

  searchServerHistoryCustomers(
    context: PosServerHistoryScope,
    search: string,
    skip = 0,
    take = 10,
  ) {
    return this.request<PosCustomerSearchPage>(
      "/edge/v1/server-history/customers/search",
      { method: "POST", body: JSON.stringify({ context, search, skip, take }) },
    );
  }

  searchServerHistoryProducts(
    context: PosServerHistoryScope,
    search: string,
    skip = 0,
    take = 10,
  ) {
    return this.request<PosCatalogSearchPage>(
      "/edge/v1/server-history/products/search",
      { method: "POST", body: JSON.stringify({ context, search, skip, take }) },
    );
  }

  loadServerIssuedSaleReceipt(
    context: PosServerHistoryScope,
    documentId: string,
  ) {
    return this.request<PosPrintableReceipt>(
      `/edge/v1/server-history/sales/${documentId}/receipt`,
      { method: "POST", body: JSON.stringify(context) },
    );
  }

  recordServerIssuedSaleReprint(
    context: PosServerHistoryScope,
    documentId: string,
  ) {
    return this.requestVoid(
      `/edge/v1/server-history/sales/${documentId}/reprint-audit`,
      { method: "POST", body: JSON.stringify({ businessId: context.businessId }) },
    );
  }

  searchServerReturnableSales(
    context: PosSalesReturnContext,
    query: PosSalesReturnQuery,
  ) {
    return this.request<ReturnableSalePage>("/edge/v1/server-returns/search", {
      method: "POST",
      body: JSON.stringify({ context, query }),
    });
  }

  loadServerReturnableSale(context: PosSalesReturnContext, documentId: string) {
    return this.request<ReturnableSale>(
      `/edge/v1/server-returns/sales/${documentId}`,
      { method: "POST", body: JSON.stringify(context) },
    );
  }

  loadServerSalesReturnBootstrap(context: PosSalesReturnContext) {
    return this.request<PosSalesReturnBootstrap>(
      "/edge/v1/server-returns/bootstrap",
      { method: "POST", body: JSON.stringify(context) },
    );
  }

  confirmServerSalesReturn(request: ConfirmSalesReturnRequest) {
    return this.request<SalesReturnAcceptance>(
      "/edge/v1/server-returns/confirm",
      { method: "POST", body: JSON.stringify(request) },
    );
  }

  reprint(documentId: string) {
    return this.requestVoid(`/edge/v1/sales/${documentId}/reprint`, { method: "POST" });
  }

  printHistoricalReceipt(receipt: PosPrintableReceipt) {
    return this.printReceipt(receipt, null, "pos");
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

  updateLines(draftId: string, lines: PosDraftLineUpdate[], includesProratedDiscount = false) {
    return this.request<PosDraft>(`/edge/v1/drafts/${draftId}/lines`, {
      method: "PUT",
      body: JSON.stringify({
        lines: buildCompatibleEdgeLineUpdates(lines),
        includesProratedDiscount,
      }),
    });
  }

  approval(approvalRequestId: string) {
    return this.request<PosApprovalSummary>(
      `/edge/v1/approvals/${encodeURIComponent(approvalRequestId)}`,
    );
  }

  clearAfterOnlineCommit(draftId: string) {
    return this.request<PosDraft>(`/edge/v1/drafts/${draftId}/clear-after-online-commit`, {
      method: "POST",
    });
  }

  private async request<T>(path: string, init: RequestInit = {}): Promise<T> {
    const requestSessionToken = this.userSessionToken;
    const response = await fetch(`${EDGE_BASE_URL}${path}`, {
      ...init,
      cache: "no-store",
      headers: {
        "Content-Type": "application/json",
        "X-Auraly-Edge-Session": this.sessionToken,
        ...(requestSessionToken
          ? { "X-Auraly-User-Session": requestSessionToken }
          : {}),
        ...init.headers,
      },
    });
    if (!response.ok) {
      const raw = await response.text();
      const problem = readPosEdgeProblem(raw, response.statusText);
      announceEdgeLoginReplacement(response.status, problem.code, requestSessionToken);
      throw new PosEdgeError(problem.detail, response.status, problem.code);
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
    const requestSessionToken = this.userSessionToken;
    const response = await fetch(`${EDGE_BASE_URL}${path}`, {
      ...init,
      cache: "no-store",
      headers: {
        "Content-Type": "application/json",
        "X-Auraly-Edge-Session": this.sessionToken,
        ...(requestSessionToken
          ? { "X-Auraly-User-Session": requestSessionToken }
          : {}),
        ...init.headers,
      },
    });
    if (response.ok || acceptedStatuses.includes(response.status))
      return (await response.json()) as T;
    const raw = await response.text();
    const problem = readPosEdgeProblem(raw, response.statusText);
    announceEdgeLoginReplacement(response.status, problem.code, requestSessionToken);
    throw new PosEdgeError(problem.detail, response.status, problem.code);
  }

  private async requestVoid(path: string, init: RequestInit = {}): Promise<void> {
    const requestSessionToken = this.userSessionToken;
    const response = await fetch(`${EDGE_BASE_URL}${path}`, {
      ...init,
      cache: "no-store",
      headers: {
        "Content-Type": "application/json",
        "X-Auraly-Edge-Session": this.sessionToken,
        ...(requestSessionToken
          ? { "X-Auraly-User-Session": requestSessionToken }
          : {}),
        ...init.headers,
      },
    });
    if (!response.ok) {
      const raw = await response.text();
      const problem = readPosEdgeProblem(raw, response.statusText);
      announceEdgeLoginReplacement(response.status, problem.code, requestSessionToken);
      throw new PosEdgeError(problem.detail, response.status, problem.code);
    }
  }
}

export function readEdgeUserSession(): string | null {
  return window.localStorage.getItem("auraly.pos.user-session");
}

export function clearEdgeUserSession(): void {
  window.localStorage.removeItem("auraly.pos.user-session");
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
