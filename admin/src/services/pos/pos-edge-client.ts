const EDGE_BASE_URL =
  process.env.NEXT_PUBLIC_AURALY_POS_EDGE_URL ?? "http://127.0.0.1:47831";

export type DraftId = { value: string };
export type ProductId = { value: string };

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
    return this.request<{ status: string }>("/edge/v1/health");
  }

  activeDraft() {
    return this.request<PosDraft>("/edge/v1/drafts/active");
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

  removeLine(draftId: string, lineId: string) {
    return this.request<PosDraft>(
      `/edge/v1/drafts/${draftId}/lines/${lineId}`,
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

  recoverTemporary(draftId: string) {
    return this.request<PosDraft>(
      `/edge/v1/temporaries/${draftId}/recover`,
      { method: "POST" },
    );
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
      const detail = await response.text();
      throw new PosEdgeError(detail || response.statusText, response.status);
    }
    return (await response.json()) as T;
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
