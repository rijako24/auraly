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
  priceListId: string | null;
  priceChannelId: string | null;
  isActive: boolean;
};

export type PosCustomerSearchPage = {
  items: PosCustomer[];
  hasMore: boolean;
  nextOffset: number | null;
};

export type PosCustomerSelection = { draft: PosDraft; customer: PosCustomer | null };

export type PosIssuedSaleSummary = {
  documentId: { value: string };
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
  net: number;
  tax: number;
  total: number;
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
};

export type PosCaptureResult = {
  status: "Added" | "NotFound" | "InsufficientInventory" | "OfflineValidationRequired";
  draft: PosDraft | null;
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

export type PosCompleteSaleResult = {
  issuedSale: {
    documentId: { value: string };
    documentNumber: string;
    fiscalNumber: string;
    cufe: string;
    qrPayload: string;
    total: number;
    outboxMessageId: string;
    wasAlreadyIssued: boolean;
  };
  nextDraft: PosDraft;
  nextDocumentNumber: PosDocumentNumberPreview;
  nextFiscalNumber: PosFiscalNumberPreview;
};

export class PosEdgeError extends Error {
  constructor(
    message: string,
    public readonly status: number,
  ) {
    super(message);
  }
}

export class PosEdgeClient {
  constructor(private readonly sessionToken: string) {}

  health() {
    return this.request<{
      status: string;
      serverConnected: boolean;
      registerCode: string;
      userDisplayName: string;
    }>("/edge/v1/health");
  }

  searchProducts(search = "", skip = 0, take = 50) {
    const query = new URLSearchParams({
      search,
      skip: String(skip),
      take: String(take),
    });
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

  customer(customerId: string) {
    return this.request<PosCustomer>(`/edge/v1/customers/${customerId}`);
  }

  activeDraft() {
    return this.request<PosDraft>("/edge/v1/drafts/active");
  }

  nextNumbers() {
    return this.request<PosNextNumbers>("/edge/v1/sales/next-number");
  }

  capture(value: string, customerId: string | null) {
    return this.request<PosCaptureResult>("/edge/v1/capture", {
      method: "POST",
      body: JSON.stringify({ value, customerId }),
    });
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

  setDiscount(draftId: string, lineId: string, discount: number) {
    return this.request<PosDraft>(
      `/edge/v1/drafts/${draftId}/lines/${lineId}/discount`,
      {
        method: "PUT",
        body: JSON.stringify({ discount }),
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

  removeLine(draftId: string, lineId: string) {
    return this.request<PosDraft>(
      `/edge/v1/drafts/${draftId}/lines/${lineId}`,
      { method: "DELETE" },
    );
  }

  cancelDraft(draftId: string) {
    return this.request<PosDraft>(
      `/edge/v1/drafts/${draftId}`,
      { method: "DELETE" },
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
  ) {
    return this.request<PosCompleteSaleResult>(
      `/edge/v1/drafts/${draftId}/complete`,
      {
        method: "POST",
        body: JSON.stringify({ customerIdentification, payments }),
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

  private async request<T>(path: string, init: RequestInit = {}): Promise<T> {
    const response = await fetch(`${EDGE_BASE_URL}${path}`, {
      ...init,
      cache: "no-store",
      headers: {
        "Content-Type": "application/json",
        "X-Auraly-Edge-Session": this.sessionToken,
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
    return (await response.json()) as T;
  }

  private async requestVoid(path: string, init: RequestInit = {}): Promise<void> {
    const response = await fetch(`${EDGE_BASE_URL}${path}`, {
      ...init,
      cache: "no-store",
      headers: {
        "Content-Type": "application/json",
        "X-Auraly-Edge-Session": this.sessionToken,
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
