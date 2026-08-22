import { PosEdgeError } from "./pos-edge-client";

export type PosApprovalRequest = {
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

export type SupervisorCredentialStatus = { isConfigured: boolean; createdAt: string | null; validUntil: string | null };
export type PosApprovalPushSubscription = { endpoint: string; p256dh: string; auth: string };

type Negotiation = { clientAccessUri: string; expiresAt: string };

export class PosApprovalClient {
  private headers(): Record<string, string> {
    const tenantId = window.localStorage.getItem("selected_tenant_id");
    const businessId = window.localStorage.getItem("selected_business_id");
    return {
      "Content-Type": "application/json",
      ...(tenantId ? { "X-Tenant-Id": tenantId } : {}),
      ...(businessId ? { "X-Business-Id": businessId } : {}),
    };
  }

  private async request<T>(path: string, init: RequestInit = {}): Promise<T> {
    const response = await fetch(path, {
      ...init,
      credentials: "include",
      cache: "no-store",
      headers: { ...this.headers(), ...init.headers },
    });
    if (!response.ok) {
      const raw = await response.text();
      let message = raw || response.statusText;
      let code: string | undefined;
      try {
        const problem = JSON.parse(raw) as { detail?: string; title?: string; code?: string };
        message = problem.detail || problem.title || message;
        code = problem.code || problem.title;
      } catch { /* plain response */ }
      throw new PosEdgeError(message, response.status, code);
    }
    if (response.status === 204) return undefined as T;
    return response.json() as Promise<T>;
  }

  create(input: {
    businessId: string;
    deviceId?: string | null;
    workSessionId?: string | null;
    draftId: string;
    lineId?: string | null;
    permissionResource: string;
    contextJson: string;
  }) {
    return this.request<PosApprovalRequest>("/api/commerce/v1/pos/approvals/", {
      method: "POST",
      body: JSON.stringify(input),
    });
  }

  get(id: string) {
    return this.request<PosApprovalRequest>(`/api/commerce/v1/pos/approvals/${id}`);
  }

  authorizeLocally(id: string, secret: string) {
    return this.request<{ status: string }>(
      `/api/commerce/v1/pos/approvals/${id}/local-authorization`,
      { method: "POST", body: JSON.stringify({ secret }) },
    );
  }

  pending() {
    return this.request<PosApprovalRequest[]>("/api/commerce/v1/pos/approvals/pending");
  }

  decide(id: string, approve: boolean) {
    return this.request<{ status: string }>(
      `/api/commerce/v1/pos/approvals/${id}/decision`,
      { method: "POST", body: JSON.stringify({ approve }) },
    );
  }

  configureCredential(secret: string, validityHours: 8 | 168 | null) {
    return this.request<void>(
      "/api/commerce/v1/pos/approvals/supervisor-credential",
      { method: "PUT", body: JSON.stringify({ secret, validityHours }) },
    );
  }

  credentialStatus() {
    return this.request<SupervisorCredentialStatus>("/api/commerce/v1/pos/approvals/supervisor-credential");
  }

  revokeCredential() {
    return this.request<void>("/api/commerce/v1/pos/approvals/supervisor-credential", { method: "DELETE" });
  }

  userCredentialStatus(userId: string) {
    return this.request<SupervisorCredentialStatus>(`/api/commerce/v1/pos/approvals/users/${userId}/supervisor-credential`);
  }

  configureUserCredential(userId: string, secret: string, validityHours: 8 | 168 | null) {
    return this.request<void>(`/api/commerce/v1/pos/approvals/users/${userId}/supervisor-credential`, { method: "PUT", body: JSON.stringify({ secret, validityHours }) });
  }

  revokeUserCredential(userId: string) {
    return this.request<void>(`/api/commerce/v1/pos/approvals/users/${userId}/supervisor-credential`, { method: "DELETE" });
  }

  pushPublicKey() {
    return this.request<{ publicKey: string }>("/api/commerce/v1/pos/approvals/push/public-key");
  }

  savePushSubscription(subscription: PosApprovalPushSubscription) {
    return this.request<void>("/api/commerce/v1/pos/approvals/push/subscription", { method: "PUT", body: JSON.stringify(subscription) });
  }

  removePushSubscription(subscription: PosApprovalPushSubscription) {
    return this.request<void>("/api/commerce/v1/pos/approvals/push/subscription", { method: "DELETE", body: JSON.stringify(subscription) });
  }

  async subscribe(onApprovalsChanged: () => void): Promise<() => void> {
    const negotiation = await this.request<Negotiation>(
      "/api/commerce/v1/pos/approvals/synchronization/negotiate",
      { method: "POST" },
    );
    const socket = new WebSocket(
      negotiation.clientAccessUri,
      "json.webpubsub.azure.v1",
    );
    const onMessage = (event: MessageEvent<string>) => {
      try {
        const envelope = JSON.parse(event.data) as {
          type?: string;
          data?: { stream?: string } | string;
        };
        const data = typeof envelope.data === "string"
          ? JSON.parse(envelope.data) as { stream?: string }
          : envelope.data;
        if (envelope.type === "message" && data?.stream === "Approvals")
          onApprovalsChanged();
      } catch { /* ignore protocol frames that are not data messages */ }
    };
    socket.addEventListener("message", onMessage);
    await new Promise<void>((resolve, reject) => {
      const timeout = window.setTimeout(
        () => reject(new Error("No fue posible abrir el canal de aprobación.")),
        8_000,
      );
      socket.addEventListener("open", () => {
        window.clearTimeout(timeout);
        resolve();
      }, { once: true });
      socket.addEventListener("error", () => {
        window.clearTimeout(timeout);
        reject(new Error("No fue posible abrir el canal de aprobación."));
      }, { once: true });
    });
    return () => {
      socket.removeEventListener("message", onMessage);
      socket.close();
    };
  }
}

export const posApprovalClient = new PosApprovalClient();
