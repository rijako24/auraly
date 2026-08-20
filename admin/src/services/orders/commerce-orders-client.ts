export type CommerceOrderClaim = {
  claimId: string;
  workSessionId: string;
  deviceId: string | null;
  userId: string;
  expiresAt: string;
  isOwnedByCurrentActor: boolean;
};

export type CommerceOrderListItem = {
  orderId: string;
  orderNumber: string;
  status: string;
  source: number;
  customerName: string | null;
  customerIdentification: string | null;
  customerPhone: string | null;
  currency: string;
  total: number;
  lineCount: number;
  createdAt: string;
  canInvoice: boolean;
  invoiceDocumentId: string | null;
  claim: CommerceOrderClaim | null;
};

export type CommerceOrderPage = {
  items: CommerceOrderListItem[];
  page: number;
  pageSize: number;
  totalCount: number;
  hasMore: boolean;
};

export type CommerceOrderLine = {
  orderItemId: string;
  productId: string | null;
  productCode: string | null;
  sku: string | null;
  productName: string;
  unitCode: string;
  quantity: number;
  unitPrice: number;
  discountAmount: number;
  lineTotal: number;
};

export type CommerceOrderDetail = CommerceOrderListItem & {
  businessId: string;
  customerId: string | null;
  customerEmail: string | null;
  deliveryAddress: string | null;
  notes: string | null;
  subtotal: number;
  discountTotal: number;
  paymentTransactionId: string | null;
  paymentStatus: string | null;
  lines: CommerceOrderLine[];
};

export type CommerceOrderFilters = {
  page?: number;
  pageSize?: number;
  orderNumber?: string;
  customer?: string;
  product?: string;
  status?: string;
  createdFrom?: string;
  createdTo?: string;
  hasPendingBalance?: boolean;
  source?: number;
  warehouseId?: string;
  routeId?: string;
  onlyMine?: boolean;
};

export type InvoiceOrderResult = {
  orderId: string;
  orderNumber: string;
  status: string;
  documentId: string | null;
  documentNumber: string | null;
  error: string | null;
};

export type InvoiceOrdersResponse = {
  operationId: string;
  status: string;
  requestedCount: number;
  completedCount: number;
  failedCount: number;
  isReplay: boolean;
  results: InvoiceOrderResult[];
};

export type RecoverOrderResult = {
  orderId: string;
  draftId: string;
  draftVersion: number;
  orderNumber: string;
  payableAmount: number;
};

export async function loadCommerceOrders(
  filters: CommerceOrderFilters,
): Promise<CommerceOrderPage> {
  const query = new URLSearchParams();
  Object.entries(filters).forEach(([key, value]) => {
    if (value !== undefined && value !== "") query.set(key, String(value));
  });
  return orderRequest<CommerceOrderPage>(
    `/api/commerce/v1/orders?${query.toString()}`,
  );
}

export function loadCommerceOrder(orderId: string) {
  return orderRequest<CommerceOrderDetail>(
    `/api/commerce/v1/orders/${orderId}`,
  );
}

export function recoverCommerceOrder(
  orderId: string,
  request: {
    workSessionId: string;
    userId: string;
    draftId: string;
    expectedDraftVersion: number;
  },
) {
  return orderRequest<RecoverOrderResult>(
    `/api/commerce/v1/orders/${orderId}/recover`,
    {
      method: "POST",
      headers: { "Idempotency-Key": crypto.randomUUID() },
      body: JSON.stringify(request),
    },
  );
}

export function invoiceCommerceOrders(request: {
  workSessionId: string;
  warehouseId: string;
  userId: string;
  orderIds: string[];
  paymentMethodCode: string;
  paymentReference: string | null;
}) {
  return orderRequest<InvoiceOrdersResponse>(
    "/api/commerce/v1/orders/invoice",
    {
      method: "POST",
      headers: { "Idempotency-Key": crypto.randomUUID() },
      body: JSON.stringify(request),
    },
  );
}

async function orderRequest<T>(
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
      };
      detail = problem.detail || problem.message || problem.title || detail;
    } catch {
      // Preserve plain server text.
    }
    throw new Error(detail);
  }
  return (await response.json()) as T;
}
