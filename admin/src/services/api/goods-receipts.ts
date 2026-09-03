import { apiClient, withPagedDefaults } from "./client";

export type GoodsReceiptStatus = "Draft" | "Accepted" | "Processed";
export type PurchaseTaxTreatment = "DeductibleInputVat" | "CapitalizedCost" | "NotApplicable";
export type PurchaseEvidenceType =
  | "SupplierElectronicInvoice"
  | "BuyerElectronicSupportDocument"
  | "InternalReceiptVoucher"
  | "ForeignCommercialInvoice"
  | "ImportDeclaration";
export type PurchaseCostKind = "Freight" | "Insurance" | "CustomsDuty" |
  "CustomsBrokerage" | "Handling" | "OtherDirectCost" | "ImportVat";
export type PurchaseCostTreatment = "Capitalize" | "Expense";
export type PurchaseCostAllocationMethod = "Value" | "Quantity" | "Weight" |
  "Volume" | "Equal" | "Manual" | "None";

export interface GoodsReceiptCostDocument {
  costDocumentId: string; supplierId: string; purchaseEvidenceType: PurchaseEvidenceType;
  documentNumber: string; issuedAt: string; createsPayable: boolean; dueDate: string | null;
  currencyCode: string; exchangeRate: number; exchangeRateDate: string | null;
  exchangeRateSource: string; withholdingConceptCode?: string | null;
  withholdingJurisdictionCode?: string | null;
  lines: Array<{
    lineNumber: number; costKind: PurchaseCostKind; description: string; amount: number;
    taxableBaseAmount: number; taxCode: string; taxRate: number; taxAmount: number;
    taxTreatment: PurchaseTaxTreatment; costTreatment: PurchaseCostTreatment;
    allocationMethod: PurchaseCostAllocationMethod; eligibleReceiptLineNumbers?: number[] | null;
    manualAllocations?: Array<{ receiptLineNumber: number; functionalAmount: number }> | null;
    functionalAmount?: number; functionalTaxAmount?: number; functionalDocumentAmount?: number;
    allocations?: Array<{ receiptLineNumber: number; functionalAmount: number; allocationMethod: string }>;
  }>;
  netAmount?: number; taxAmount?: number; grandTotal?: number;
  functionalNetAmount?: number; functionalTaxAmount?: number; functionalGrandTotal?: number;
  withholding?: GoodsReceiptWithholdingCalculation;
}

export interface GoodsReceiptLine {
  lineNumber: number;
  productId: string;
  description: string;
  quantity: number;
  unitCost: number;
  discountAmount: number;
  taxCode: string;
  taxRate: number;
  taxTreatment: PurchaseTaxTreatment;
  presentationName: string;
  baseUnitCode?: string;
  preferredPresentationName?: string;
  preferredUnitsPerPresentation?: number;
  presentationQuantity: number;
  unitsPerPresentation: number;
  latestUnitCost?: number | null;
  averageUnitCost?: number | null;
  purchaseOrderLineId?: string | null;
  overReceiptReason?: string | null;
  orderedQuantity?: number | null;
  remainingQuantity?: number | null;
  totalGrossWeightKg?: number | null;
  totalVolumeM3?: number | null;
}

export interface GoodsReceiptLineSnapshot extends GoodsReceiptLine {
  netAmount: number;
  taxAmount: number;
  lineTotal: number;
  functionalNetAmount?: number;
  functionalTaxAmount?: number;
  functionalLineTotal?: number;
  allocatedLandedCostAmount?: number;
  recognizedInventoryCostAmount?: number;
}

export interface GoodsReceiptDraft {
  draftId: string;
  businessId: string;
  warehouseId: string | null;
  supplierId: string | null;
  supplierInvoiceNumber: string | null;
  supplierInvoiceDate: string | null;
  receivedAt: string;
  createsPayable: boolean;
  dueDate: string | null;
  currencyCode: string;
  notes: string | null;
  netAmount: number;
  taxAmount: number;
  grandTotal: number;
  lines: GoodsReceiptLineSnapshot[];
  updatedAt: string;
  concurrencyToken: string;
  purchaseEvidenceType: PurchaseEvidenceType | null;
  purchaseOrderId: string | null;
  exchangeRate: number; exchangeRateDate: string | null; exchangeRateSource: string;
  additionalCostDocuments: GoodsReceiptCostDocument[] | null;
}

export interface GoodsReceiptDetail {
  documentId: string;
  documentNumber: string;
  status: Exclude<GoodsReceiptStatus, "Draft">;
  warehouseId: string;
  warehouseName: string;
  supplierId: string;
  supplierName: string;
  supplierInvoiceNumber: string | null;
  supplierInvoiceDate: string | null;
  receivedAt: string;
  createsPayable: boolean;
  dueDate: string | null;
  currencyCode: string;
  notes: string | null;
  netAmount: number;
  taxAmount: number;
  grandTotal: number;
  acceptedAt: string;
  processedAt: string | null;
  lines: GoodsReceiptLineSnapshot[];
  purchaseEvidenceType: PurchaseEvidenceType;
  purchaseOrderId: string | null;
  withholding: {
    grossAmount: number;
    withholdingTotal: number;
    netAmount: number;
    lines: Array<{
      ruleId: string; ruleVersion: number; ruleCode: string; name: string;
      kind: string; baseKind: string; taxableBase: number; rate: number;
      amount: number; jurisdictionCode: string | null;
    }>;
  } | null;
  exchangeRate: number; exchangeRateDate: string | null; exchangeRateSource: string;
  functionalNetAmount: number; functionalTaxAmount: number; functionalGrandTotal: number;
  additionalCostDocuments: GoodsReceiptCostDocument[] | null;
  accountingStatuses: Array<{ sourceDocumentId: string; sourceDocumentType: string;
    status: string; errorCode: string | null; errorMessage: string | null }> | null;
}

export interface SaveGoodsReceiptDraftRequest {
  draftId: string;
  businessId: string;
  warehouseId: string | null;
  supplierId: string | null;
  supplierInvoiceNumber: string | null;
  supplierInvoiceDate: string | null;
  receivedAt: string;
  createsPayable: boolean;
  dueDate: string | null;
  currencyCode: string;
  notes: string | null;
  lines: GoodsReceiptLine[];
  concurrencyToken: string | null;
  purchaseEvidenceType: PurchaseEvidenceType | null;
  purchaseOrderId: string | null;
  exchangeRate: number; exchangeRateDate: string | null; exchangeRateSource: string;
  additionalCostDocuments: GoodsReceiptCostDocument[] | null;
}

export interface GoodsReceiptListItem {
  documentId: string;
  documentNumber: string | null;
  status: GoodsReceiptStatus;
  warehouseId: string | null;
  warehouseName: string | null;
  supplierId: string | null;
  supplierName: string | null;
  supplierInvoiceNumber: string | null;
  receivedAt: string;
  grandTotal: number;
  updatedAt: string;
  purchaseEvidenceType: PurchaseEvidenceType | null;
}

export interface GoodsReceiptPage {
  items: GoodsReceiptListItem[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

export interface GoodsReceiptOptions {
  warehouses: Array<{ warehouseId: string; code: string; name: string }>;
  suppliers: Array<{
    supplierId: string; identification: string; name: string;
    purchaseEvidencePolicy: PurchaseEvidenceType | null;
    allowedPurchaseEvidenceTypes: PurchaseEvidenceType[];
  }>;
  purchaseEvidenceTypes: Array<{ code: PurchaseEvidenceType; label: string; description: string | null }>;
  withholdingConcepts: Array<{ code: string; label: string }>;
  withholdingJurisdictions: Array<{ code: string; label: string }>;
  purchaseCostEvidenceTypes: Array<{ code: PurchaseEvidenceType; label: string; description: string | null }>;
  purchaseCostKinds: Array<{ code: PurchaseCostKind; label: string; description: string | null }>;
  purchaseCostTreatments: Array<{ code: PurchaseCostTreatment; label: string; description: string | null }>;
  purchaseCostAllocationMethods: Array<{ code: PurchaseCostAllocationMethod; label: string; description: string | null }>;
  purchaseTaxRates: Array<{ code: string; label: string; description: string | null }>;
  purchaseTaxTreatments: Array<{ code: PurchaseTaxTreatment; label: string; description: string | null }>;
  purchaseCurrencies: Array<{ code: string; label: string; description: string | null }>;
  exchangeRateSources: Array<{ code: string; label: string; description: string | null }>;
}

export interface GoodsReceiptProduct {
  productId: string;
  productCode: string;
  reference: string | null;
  name: string;
  supplierProductCode: string | null;
  latestUnitCost: number | null;
  averageUnitCost: number | null;
  taxCode: string;
  taxRate: number;
  taxTreatment: PurchaseTaxTreatment;
  barcodes: string[];
  baseUnitCode: string;
  isAssociated: boolean;
  purchasePresentationName: string;
  unitsPerPresentation: number;
  isPrimary: boolean;
}

export interface GoodsReceiptProductPage {
  items: GoodsReceiptProduct[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

export interface GoodsReceiptAcceptance {
  documentId: string;
  movementId: string;
  documentNumber: string;
  status: string;
  processingSequence: number;
  idempotentReplay: boolean;
}

export interface GoodsReceiptWithholdingCalculation {
  grossAmount: number;
  withholdingTotal: number;
  netAmount: number;
  lines: Array<{
    ruleId: string; ruleVersion: number; ruleCode: string; name: string;
    kind: string; baseKind: string; taxableBase: number; rate: number;
    amount: number; jurisdictionCode: string | null;
  }>;
}

export const goodsReceiptsApi = {
  options: () => apiClient.get<GoodsReceiptOptions>("/commerce/v1/goods-receipts/options"),
  products: (supplierId: string, search?: string, includeUnassociated = false, page = 1, pageSize = 50) =>
    apiClient.get<GoodsReceiptProductPage>("/commerce/v1/goods-receipts/products", {
      supplierId, search, includeUnassociated, page, pageSize,
    }),
  associateProduct: (request: {
    supplierId: string; productId: string; supplierProductCode: string | null; isPrimary: boolean;
    purchasePresentationName: string; unitsPerPresentation: number;
  }) => apiClient.post<GoodsReceiptProduct>(
    "/commerce/v1/goods-receipts/supplier-products", request,
  ),
  list: (params: { search?: string; status?: GoodsReceiptStatus; page: number; pageSize: number }) =>
    apiClient.get<GoodsReceiptPage>("/commerce/v1/goods-receipts", withPagedDefaults(params)),
  getDraft: (draftId: string) =>
    apiClient.get<GoodsReceiptDraft>(`/commerce/v1/goods-receipts/drafts/${draftId}`),
  getDetail: (documentId: string) =>
    apiClient.get<GoodsReceiptDetail>(`/commerce/v1/goods-receipts/${documentId}`),
  saveDraft: (request: SaveGoodsReceiptDraftRequest) =>
    apiClient.put<GoodsReceiptDraft>(`/commerce/v1/goods-receipts/drafts/${request.draftId}`, request),
  deleteDraft: (draftId: string, concurrencyToken: string) =>
    apiClient.delete<{ deleted: boolean }>(
      `/commerce/v1/goods-receipts/drafts/${draftId}?concurrencyToken=${encodeURIComponent(concurrencyToken)}`,
    ),
  previewWithholding: (request: {
    businessId: string; supplierId: string; supplierInvoiceDate: string;
    lines: GoodsReceiptLine[]; withholdingConceptCode: string | null;
    withholdingJurisdictionCode: string | null; purchaseEvidenceType: PurchaseEvidenceType;
    exchangeRate: number;
  }) => apiClient.post<GoodsReceiptWithholdingCalculation>(
    "/commerce/v1/goods-receipts/withholding-preview", request,
  ),
  previewCostWithholding: (request: {
    businessId: string; document: GoodsReceiptCostDocument;
  }) => apiClient.post<GoodsReceiptWithholdingCalculation>(
    "/commerce/v1/goods-receipts/cost-withholding-preview", request,
  ),
  confirm: (request: {
    documentId: string; businessId: string; warehouseId: string; supplierId: string;
    supplierInvoiceNumber: string | null; supplierInvoiceDate: string | null;
    receivedAt: string; createsPayable: boolean; dueDate: string | null;
    currencyCode: string; notes: string | null; lines: GoodsReceiptLine[];
    draftConcurrencyToken: string | null;
    withholdingConceptCode: string | null; withholdingJurisdictionCode: string | null;
    purchaseEvidenceType: PurchaseEvidenceType;
    purchaseOrderId: string | null;
    exchangeRate: number; exchangeRateDate: string | null; exchangeRateSource: string;
    additionalCostDocuments: GoodsReceiptCostDocument[] | null;
  }) => apiClient.postIdempotent<GoodsReceiptAcceptance>(
    "/commerce/v1/goods-receipts/confirm", request, `goods-receipt-${request.documentId}`,
  ),
};
