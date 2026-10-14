import { apiClient } from "./client";
import type { PosPaymentInput, PosPrintableReceipt,
  PosSaleDocumentType } from "@/services/pos/pos-edge-client";

export type RestaurantAccount = {
  accountId: string;
  businessId: string;
  tableId: string;
  warehouseId: string;
  responsibleUserId: string;
  responsibleUserName?: string | null;
  priceChannelId?: string | null;
  priceChannelName?: string | null;
  customerId?: string | null;
  customerPartySiteId?: string | null;
  customerName?: string | null;
  customerPriceChannelLocked?: boolean;
  status: "Open" | "Issuing" | "Invoiced" | "Cancelled" | "Released";
  version: number;
  openedAt: string;
  updatedAt: string;
};

export type RestaurantTableOccupancy = {
  tableId: string;
  accountId: string | null;
  state: "Free" | RestaurantAccount["status"];
  responsibleUserId: string | null;
  openedAt: string | null;
  accountVersion: number | null;
};

export type RestaurantAccountAddition = {
  additionId: string;
  accountId: string;
  productId: string;
  productCode: string;
  description: string;
  unitCode: string;
  taxCode: string;
  taxRate: number;
  quantity: number;
  allowsFractionalSale: boolean;
  publicUnitPrice: number;
  promotionDiscountPerUnit: number;
  currencyCode: string;
  priceSource: string;
  addedByUserId: string;
  addedByName: string;
  addedAt: string;
  status: "Pending" | "Sent" | "Direct" | "Cancelled";
  stationId: string | null;
  stationName: string | null;
  preparationNote: string | null;
  replacementOfCancellationId?: string | null;
};

export type RestaurantAccountDraft = {
  account: RestaurantAccount;
  additions: RestaurantAccountAddition[];
};

export type RestaurantPriceChannelOption = {
  priceChannelId: string;
  name: string;
};


export type RestaurantCollectionAllocation = {
  additionId: string;
  quantity: number;
};

export type RestaurantCollectionSummary = {
  accountId: string;
  accountVersion: number;
  total: number;
  collected: number;
  remaining: number;
  collections: {
    collectionId: string;
    accountId: string;
    workSessionId: string;
    cashierUserId: string;
    amount: number;
    capturedAt: string;
    allocations: RestaurantCollectionAllocation[];
    payments: PosPaymentInput[];
    tipPayment: PosPaymentInput | null;
  }[];
  coveredQuantities: RestaurantCollectionAllocation[];
  tipsCollected: number;
  suggestedTipPercent: number;
};

export type RestaurantTipPendingPage = {
  items: { collectionId: string; accountId: string; amount: number;
    capturedAt: string }[];
  totalCount: number;
  totalAmount: number;
};
export type RestaurantTipSettlementRequest = {
  settlementId: string;
  collectionIds: string[];
  recipients: { partyId: string; amount: number }[];
  distributionBasis: "EqualByLaw" | "WorkerAgreement";
  agreementReference: string | null;
  paymentMethodCode: "Cash" | "Transfer";
  bankAccountId: string | null;
  paymentReference: string | null;
};
export type RestaurantTipSettlement = {
  settlementId: string; businessId: string; totalAmount: number;
  distributionBasis: "EqualByLaw" | "WorkerAgreement";
  agreementReference: string | null;
  paymentMethodCode: "Cash" | "Transfer";
  paymentReference: string | null;
  createdAt: string;
  recipients: { partyId: string; amount: number }[];
};

export type RestaurantMenuCategory = {
  categoryId: string;
  name: string;
  productCount: number;
};

export type RestaurantProduct = {
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
  priceSource: string;
  promotionDiscount: number;
  productCategoryId: string | null;
  categoryName: string | null;
  imageUrl: string | null;
};

export type RestaurantProductPage = {
  items: RestaurantProduct[];
  hasMore: boolean;
  nextOffset: number | null;
};

export type RestaurantWaiterPage = {
  requireCredential: boolean;
  restrictTableToWaiter: boolean;
  items: { userId: string; name: string; shortName: string | null }[];
  page: number;
  pageSize: number;
  totalCount: number;
};

export type RestaurantAttention = {
  token: string;
  tableId: string;
  waiterUserId: string;
};

export type RestaurantCommandSendResult = {
  roundId: string;
  draft: RestaurantAccountDraft;
  cards: {
    cardId: string;
    stationId: string;
    stationName: string;
    state: string;
    lineCount: number;
  }[];
};

export type RestaurantCommandCard = {
  cardId: string;
  stationId: string;
  stationName: string;
  tableCode: string;
  state: "Pending" | "Preparing" | "Ready" | "ToDeliver" | "Delivered" | "Cancelled";
  receivedAt: string;
  rowVersion: string;
  printState?: "Pending" | "Claimed" | "Submitted" | "Failed" | "Uncertain" | null;
  printError?: string | null;
  lines: { additionId: string; quantity: number; productName: string;
    addedByName: string; addedAt: string; preparationNote: string | null;
    cancellationId: string | null; cancellationQuantity: number | null;
    cancellationStatus: string | null;
    cancellationPreparationState: string | null;
    cancellationRowVersion: string | null }[];
};

export type RestaurantCancellation = {
  cancellationId: string;
  accountId: string;
  additionId: string;
  cardId: string;
  quantity: number;
  reasonCode: string;
  explanation: string;
  status: "Requested" | "Confirmed" | "Approved" | "Rejected";
  preparationState: "NotPrepared" | "Prepared" | "Partial" | null;
  requestedByUserId: string;
  requestedAt: string;
  rowVersion: string;
  expectedAccountVersion: number;
  consumedComponents: { productId: string; quantity: number }[] | null;
};

export type RestaurantCommandBoardPage = {
  items: RestaurantCommandCard[];
  page: number;
  hasMore: boolean;
  statesWithPendingCorrections?: string[];
};

export type RestaurantSettlementPreview = {
  grossAmount: number;
  withholdingTotal: number;
  netAmount: number;
};

export type RestaurantCheckoutResponse = {
  receipt: PosPrintableReceipt;
  isReplay: boolean;
};

export type RestaurantPrebill = {
  accountId: string;
  tableCode: string;
  accountVersion: number;
  printedAt: string;
  lines: { description: string; quantity: number; unitPrice: number;
    total: number; addedByName: string }[];
  total: number;
  suggestedTipPercent: number;
  suggestedTipAmount: number;
};

const attentionHeaders = (token?: string) => token
  ? { "X-Restaurant-Attention": token } : undefined;

export const restaurantAccountApi = {
  cancellations: (accountId: string, token?: string) =>
    apiClient.get<RestaurantCancellation[]>(
      `/commerce/v1/restaurant/accounts/${accountId}/cancellations`,
      undefined, { cache: "no-store", headers: attentionHeaders(token) }),
  requestCancellation: (accountId: string, additionId: string,
    request: { cancellationId: string; quantity: number; reasonCode: string;
      explanation: string; expectedAccountVersion: number }, token?: string) =>
    apiClient.post<RestaurantCancellation>(
      `/commerce/v1/restaurant/accounts/${accountId}/additions/${additionId}/cancellations`,
      request, { headers: attentionHeaders(token) }),
  confirmCancellation: (cancellationId: string,
    preparationState: "NotPrepared" | "Prepared" | "Partial", rowVersion: string,
    consumedComponents?: { productId: string; quantity: number }[]) =>
    apiClient.put<RestaurantCancellation>(
      `/commerce/v1/restaurant/cancellations/${cancellationId}/confirmation`,
      { preparationState, rowVersion, consumedComponents }),
  cancellationComponents: (cancellationId: string) => apiClient.get<
    { productId: string; productName: string; expectedQuantity: number }[]>(
    `/commerce/v1/restaurant/cancellations/${cancellationId}/components`,
    undefined, { cache: "no-store" }),
  resolveCancellation: (cancellationId: string, approve: boolean,
    rowVersion: string) => apiClient.put<RestaurantCancellation>(
      `/commerce/v1/restaurant/cancellations/${cancellationId}/resolution`,
      { approve, rowVersion }),
  commandCards: (query: {
    stationId?: string;
    state?: RestaurantCommandCard["state"];
    tableCode?: string;
    page?: number;
  } = {}) => apiClient.get<RestaurantCommandBoardPage>(
    "/commerce/v1/restaurant/command-cards", query, { cache: "no-store" }),
  changeCommandState: (cardId: string, state: string, rowVersion: string) => apiClient.put<void>(
    `/commerce/v1/restaurant/command-cards/${cardId}/state`, { state, rowVersion }),
  reprintCommand: (cardId: string) => apiClient.post<void>(
    `/commerce/v1/restaurant/command-cards/${cardId}/reprint`, {}),
  waiters: (search?: string, page = 1) => apiClient.get<RestaurantWaiterPage>(
    "/commerce/v1/restaurant/waiters", { search, page }, { cache: "no-store" }),
  startAttention: (request: { tableId: string; waiterUserId: string; secret?: string }) =>
    apiClient.post<RestaurantAttention>("/commerce/v1/restaurant/attention", request),
  endAttention: (token: string) => apiClient.delete(
    "/commerce/v1/restaurant/attention", { headers: attentionHeaders(token) }),
  secondaryStatus: (waiterUserId: string) => apiClient.get<{ configured: boolean }>(
    `/commerce/v1/restaurant/waiters/${waiterUserId}/secondary-credential`,
    undefined, { cache: "no-store" }),
  setSecondary: (waiterUserId: string, secret: string) => apiClient.put(
    `/commerce/v1/restaurant/waiters/${waiterUserId}/secondary-credential`,
    { secret }),
  revokeSecondary: (waiterUserId: string) => apiClient.delete(
    `/commerce/v1/restaurant/waiters/${waiterUserId}/secondary-credential`),
  occupancy: () => apiClient.get<RestaurantTableOccupancy[]>(
    "/commerce/v1/restaurant/occupancy", undefined,
    { cache: "no-store" }),
  priceChannels: () => apiClient.get<RestaurantPriceChannelOption[]>(
    "/commerce/v1/restaurant/price-channels", undefined,
    { cache: "no-store" }),
  changePriceChannel: (accountId: string,
    priceChannelId: string | null, expectedVersion: number) =>
    apiClient.put<RestaurantAccountDraft>(
      `/commerce/v1/restaurant/accounts/${accountId}/price-channel`,
      { priceChannelId, expectedVersion }),
  changeCustomer: (accountId: string, customerId: string | null,
    customerPartySiteId: string | null, expectedVersion: number, token?: string) =>
    apiClient.put<RestaurantAccountDraft>(
      `/commerce/v1/restaurant/accounts/${accountId}/customer`,
      { customerId, customerPartySiteId, expectedVersion },
      { headers: attentionHeaders(token) }),
  open: (request: { accountId: string; tableId: string; warehouseId: string },
    token?: string) =>
    apiClient.post<RestaurantAccount>(
      "/commerce/v1/restaurant/accounts", request,
      { headers: attentionHeaders(token) }),
  draft: (accountId: string, token?: string) => apiClient.get<RestaurantAccountDraft>(
    `/commerce/v1/restaurant/accounts/${accountId}/draft`, undefined,
    { cache: "no-store", headers: attentionHeaders(token) }),
  prebill: (accountId: string, expectedVersion: number, token?: string) =>
    apiClient.post<RestaurantPrebill>(
      `/commerce/v1/restaurant/accounts/${accountId}/prebill`,
      { expectedVersion }, { headers: attentionHeaders(token) }),
  categories: (accountId: string, token?: string) => apiClient.get<RestaurantMenuCategory[]>(
    `/commerce/v1/restaurant/accounts/${accountId}/categories`, undefined,
    { cache: "no-store", headers: attentionHeaders(token) }),
  products: (accountId: string, query: {
    search?: string;
    categoryId?: string;
    skip?: number;
    take?: number;
  }, token?: string) => apiClient.get<RestaurantProductPage>(
    `/commerce/v1/restaurant/accounts/${accountId}/products`, query,
    { cache: "no-store", headers: attentionHeaders(token) }),
  add: (accountId: string, request: {
    additionId: string;
    productId: string;
    quantity: number;
    expectedVersion: number;
    replacementOfCancellationId?: string;
  }, token?: string) => apiClient.post<RestaurantAccountDraft>(
    `/commerce/v1/restaurant/accounts/${accountId}/additions`, request,
    { headers: attentionHeaders(token) }),
  changeQuantity: (accountId: string, additionId: string, request: {
    changeId: string;
    quantity: number;
    expectedVersion: number;
  }, token?: string) => apiClient.put<RestaurantAccountDraft>(
    `/commerce/v1/restaurant/accounts/${accountId}/additions/${additionId}/quantity`,
    request, { headers: attentionHeaders(token) }),
  changeNote: (accountId: string, additionId: string, request: {
    preparationNote: string | null;
    expectedVersion: number;
  }, token?: string) => apiClient.put<RestaurantAccountDraft>(
    `/commerce/v1/restaurant/accounts/${accountId}/additions/${additionId}/note`,
    request, { headers: attentionHeaders(token) }),
  sendPending: (accountId: string, request: {
    operationId: string;
    expectedVersion: number;
    additionId?: string;
  }, token?: string) => apiClient.post<RestaurantCommandSendResult>(
    `/commerce/v1/restaurant/accounts/${accountId}/commands`, request,
    { headers: attentionHeaders(token) }),
  settlementPreview: (accountId: string, workSessionId: string) =>
    apiClient.get<RestaurantSettlementPreview>(
      `/commerce/v1/restaurant/accounts/${accountId}/settlement-preview`,
      { workSessionId }, { cache: "no-store" }),
  collections: (accountId: string) =>
    apiClient.get<RestaurantCollectionSummary>(
      `/commerce/v1/restaurant/accounts/${accountId}/collections`,
      undefined, { cache: "no-store" }),
  pendingTips: () => apiClient.get<RestaurantTipPendingPage>(
    "/commerce/v1/restaurant/tips/pending", undefined,
    { cache: "no-store" }),
  recentTipSettlements: () => apiClient.get<RestaurantTipSettlement[]>(
    "/commerce/v1/restaurant/tips/settlements", undefined,
    { cache: "no-store" }),
  settleTips: (request: RestaurantTipSettlementRequest) =>
    apiClient.post<RestaurantTipSettlement>(
      "/commerce/v1/restaurant/tips/settlements", request),
  captureCollection: (accountId: string, request: {
    collectionId: string; workSessionId: string;
    expectedAccountVersion: number;
    allocations: RestaurantCollectionAllocation[];
    payments: PosPaymentInput[];
    tipPayment?: PosPaymentInput | null;
  }, idempotencyKey: string) => apiClient.post<RestaurantCollectionSummary>(
    `/commerce/v1/restaurant/accounts/${accountId}/collections`, request,
    { headers: { "Idempotency-Key": idempotencyKey } }),
  reverseCollection: (accountId: string, collectionId: string,
    expectedAccountVersion: number) => apiClient.post<RestaurantCollectionSummary>(
    `/commerce/v1/restaurant/accounts/${accountId}/collections/${collectionId}/reverse`,
    { expectedAccountVersion }),
  complete: (accountId: string, workSessionId: string,
    checkout: { expectedVersion: number; payments: PosPaymentInput[];
      documentType: PosSaleDocumentType }, idempotencyKey: string) =>
    apiClient.post<RestaurantCheckoutResponse>(
      `/commerce/v1/restaurant/accounts/${accountId}/complete`,
      { workSessionId, checkout },
      { headers: { "Idempotency-Key": idempotencyKey } }),
};
