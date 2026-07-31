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
  PosCustomerSearchPage,
  PosCustomerSelection,
  PosDraft,
  PosDraftLine,
  PosEdgeError,
  PosIssuedSaleSearchPage,
  PosIssuedSaleSummary,
  PosNextNumbers,
  PosPaymentInput,
  PosPrintableReceipt,
} from "./pos-edge-client";

export type OnlineRegisterOption = {
  businessId: string;
  businessName: string;
  registerId: string;
  registerCode: string;
  registerName: string;
  warehouseId: string;
  warehouseCode: string;
  warehouseName: string;
  warehouseAllowsNegativeStockSales: boolean;
  hasActiveEdgeEnrollment: boolean;
};

export type OnlineRegisterContext = Omit<
  OnlineRegisterOption,
  "hasActiveEdgeEnrollment"
>;

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
  registerId: string;
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

const REGISTER_STORAGE_KEY = "auraly.pos.online-register";

export async function loadOnlineRegisterOptions(): Promise<OnlineRegisterOption[]> {
  return request<OnlineRegisterOption[]>(
    "/api/commerce/v1/pos/register-context/options",
  );
}

export async function selectOnlineRegister(
  option: OnlineRegisterOption,
): Promise<OnlineRegisterContext> {
  const selected = await request<OnlineRegisterContext>(
    "/api/commerce/v1/pos/register-context/select",
    {
      method: "POST",
      body: JSON.stringify({
        businessId: option.businessId,
        registerId: option.registerId,
      }),
    },
  );
  window.localStorage.setItem(REGISTER_STORAGE_KEY, selected.registerId);
  return selected;
}

export function rememberedOnlineRegisterId(): string | null {
  try {
    return window.localStorage.getItem(REGISTER_STORAGE_KEY);
  } catch {
    return null;
  }
}

export function forgetOnlineRegister(): void {
  try {
    window.localStorage.removeItem(REGISTER_STORAGE_KEY);
  } catch {
    // Storage is only a convenience; the server remains authoritative.
  }
}

export class OnlinePosClient implements PosClient {
  readonly mode = "online" as const;
  private readonly versions = new Map<string, number>();
  private activeDraftId: string | null = null;

  constructor(
    private readonly context: OnlineRegisterContext,
    private readonly userId: string,
    private readonly userDisplayName: string,
  ) {}

  async health() {
    await request<{ status: string }>("/api/health");
    return {
      status: "ok",
      serverConnected: true,
      registerCode: this.context.registerCode,
      userDisplayName: this.userDisplayName,
      userId: this.userId,
    };
  }

  async searchProducts(search = "", skip = 0, take = 50) {
    const page = await request<OnlineProductPage>(
      "/api/commerce/v1/pos/drafts/products/search",
      this.post({ context: this.scope(), search, skip, take }),
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

  async customer(customerId: string) {
    const customer = await request<OnlineCustomerPage["items"][number]>(
      "/api/commerce/v1/pos/drafts/customers/get",
      this.post({ context: this.scope(), customerId }),
    );
    return mapCustomer(customer);
  }

  async activeDraft() {
    const draft = await request<OnlineDraft>(
      "/api/commerce/v1/pos/drafts/active",
      this.post({ context: this.scope() }),
    );
    return this.mapDraft(draft);
  }

  async nextNumbers(): Promise<PosNextNumbers | null> {
    return null;
  }

  async capture(value: string, _customerId: string | null) {
    const draft = await this.ensureActive();
    try {
      const updated = await request<OnlineDraft>(
        `/api/commerce/v1/pos/drafts/${draft.draftId.value}/capture`,
        this.mutation({
          value,
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

  async setDiscount(draftId: string, lineId: string, discount: number) {
    return this.mapDraft(
      await request<OnlineDraft>(
        `/api/commerce/v1/pos/drafts/${draftId}/lines/${lineId}/discount`,
        this.mutation(
          { discount, expectedVersion: this.version(draftId) },
          "PUT",
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

  async removeLine(draftId: string, lineId: string) {
    return this.mapDraft(
      await request<OnlineDraft>(
        `/api/commerce/v1/pos/drafts/${draftId}/lines/${lineId}/remove`,
        this.mutation({ expectedVersion: this.version(draftId) }),
      ),
    );
  }

  async cancelDraft(draftId: string) {
    return this.mapDraft(
      await request<OnlineDraft>(
        `/api/commerce/v1/pos/drafts/${draftId}/reset`,
        this.mutation({ expectedVersion: this.version(draftId) }),
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
  ) {
    const preview = openPrintPreview();
    try {
      const result = await request<OnlineCheckoutResponse>(
        `/api/commerce/v1/pos/drafts/${draftId}/complete`,
        this.mutation({
          expectedVersion: this.version(draftId),
          payments,
        }, "POST", `online-sale-${draftId}`),
      );
      const nextDraft = this.mapDraft(result.nextDraft);
      if (preview)
        await renderReceipt(
          preview,
          result.receipt,
          this.qrImageUrl(result.receipt.documentId),
        );
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
        printPreviewOpened: preview !== null,
      } satisfies PosCompleteSaleResult;
    } catch (error) {
      preview?.close();
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
    const preview = openPrintPreview();
    try {
      const receipt = await request<PosPrintableReceipt>(
        `/api/commerce/v1/pos/drafts/sales/${documentId}/receipt`,
        this.post(this.scope()),
      );
      if (!preview)
        throw new PosEdgeError(
          "El navegador bloqueó la vista previa de impresión.",
          409,
        );
      await renderReceipt(preview, receipt, this.qrImageUrl(documentId));
    } catch (error) {
      preview?.close();
      throw error;
    }
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
      registerId: this.context.registerId,
      userId: this.userId,
      draftId: draft.draftId.value,
      expectedDraftVersion: this.version(draft.draftId.value),
    });
    return this.activeDraft();
  }

  async invoiceOrders(
    orderIds: string[],
    paymentMethodCode: string,
  ): Promise<InvoiceOrdersResponse> {
    const response = await invoiceCommerceOrders({
      registerId: this.context.registerId,
      userId: this.userId,
      orderIds,
      paymentMethodCode,
      paymentReference: null,
    });
    await this.activeDraft();
    return response;
  }
  private scope() {
    return {
      businessId: this.context.businessId,
      registerId: this.context.registerId,
    };
  }

  private post(body: unknown): RequestInit {
    return { method: "POST", body: JSON.stringify(body) };
  }

  private mutation(
    body: unknown,
    method = "POST",
    idempotencyKey = crypto.randomUUID(),
  ): RequestInit {
    return {
      method,
      body: JSON.stringify(body),
      headers: { "Idempotency-Key": idempotencyKey },
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
        "La versión de la venta no está disponible. Recarga la caja.",
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

async function request<T>(
  path: string,
  init: RequestInit = {},
): Promise<T> {
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
      detail =
        problem.detail || problem.message || problem.title || detail;
    } catch {
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

async function renderReceipt(
  preview: Window,
  receipt: PosPrintableReceipt,
  qrImageUrl: string,
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
          <div><span>${line.quantity} × ${currency.format(line.unitPrice)}</span><b>${currency.format(line.total)}</b></div>
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
    <h2>FACTURA ELECTRÓNICA DE VENTA</h2>
    <div>${new Date(receipt.issuedAt).toLocaleString("es-CO")}</div>
  </header>
  <section class="meta">
    <div><span>Documento Auraly</span><b>${escapeHtml(receipt.documentNumber)}</b></div>
    <div><span>Número DIAN</span><b>${escapeHtml(receipt.fiscalNumber)}</b></div>
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
  <div class="cufe"><strong>CUFE</strong><br>${escapeHtml(receipt.cufe)}</div>
  <img class="qr" src="${escapeHtml(qrImageUrl)}" alt="Código QR DIAN">
  <footer>Representación gráfica</footer>
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

function escapeHtml(value: string) {
  return value
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#39;");
}
