import { PosEdgeError } from "./pos-edge-client";
import {
  isApprovalSynchronizationMessage,
  shouldMaintainApprovalRealtimeConnection,
} from "./pos-approval-synchronization";
import { fetchWithSessionRetry } from "@/services/api/client";

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

export type SupervisorCredentialStatus = { isConfigured: boolean; createdAt: string | null; validUntil: string | null; isOneTime?: boolean };
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
    const response = await fetchWithSessionRetry(path, {
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

  configureCredential(secret: string, validityHours: 8 | 168 | null, isOneTime = false) {
    return this.request<void>(
      "/api/commerce/v1/pos/approvals/supervisor-credential",
      { method: "PUT", body: JSON.stringify({ secret, validityHours, isOneTime }) },
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

  configureUserCredential(userId: string, secret: string, validityHours: 8 | 168 | null, isOneTime = false) {
    return this.request<void>(`/api/commerce/v1/pos/approvals/users/${userId}/supervisor-credential`, { method: "PUT", body: JSON.stringify({ secret, validityHours, isOneTime }) });
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
    let stopped = false;
    let connecting = false;
    let socket: WebSocket | null = null;
    let reconnectTimer: number | null = null;
    const disconnect = () => {
      if (reconnectTimer !== null) window.clearTimeout(reconnectTimer);
      reconnectTimer = null;
      const current = socket;
      socket = null;
      current?.close();
    };
    const scheduleReconnect = () => {
      if (stopped || !shouldMaintainApprovalRealtimeConnection(document.visibilityState)) return;
      if (reconnectTimer !== null) window.clearTimeout(reconnectTimer);
      reconnectTimer = window.setTimeout(() => {
        reconnectTimer = null;
        void connect();
      }, 1_000);
    };
    const connect = async () => {
      if (stopped || connecting || socket ||
        !shouldMaintainApprovalRealtimeConnection(document.visibilityState)) return;
      connecting = true;
      try {
        const negotiation = await this.request<Negotiation>(
          "/api/commerce/v1/pos/approvals/synchronization/negotiate",
          { method: "POST" },
        );
        if (stopped || !shouldMaintainApprovalRealtimeConnection(document.visibilityState)) return;
        const current = new WebSocket(
          negotiation.clientAccessUri,
          "json.webpubsub.azure.v1",
        );
        socket = current;
        current.addEventListener("message", (event: MessageEvent<string>) => {
          if (isApprovalSynchronizationMessage(event.data)) onApprovalsChanged();
        });
        await new Promise<void>((resolve, reject) => {
          const timeout = window.setTimeout(
            () => reject(new Error("No fue posible abrir el canal de aprobación.")),
            8_000,
          );
          current.addEventListener("open", () => {
            window.clearTimeout(timeout);
            resolve();
          }, { once: true });
          current.addEventListener("error", () => {
            window.clearTimeout(timeout);
            reject(new Error("No fue posible abrir el canal de aprobación."));
          }, { once: true });
        });
        current.addEventListener("close", () => {
          if (stopped || socket !== current) return;
          socket = null;
          scheduleReconnect();
        });
        onApprovalsChanged();
      } catch {
        disconnect();
        scheduleReconnect();
      } finally {
        connecting = false;
      }
    };
    const visibilityChanged = () => {
      if (shouldMaintainApprovalRealtimeConnection(document.visibilityState)) void connect();
      else disconnect();
    };
    document.addEventListener("visibilitychange", visibilityChanged);
    void connect();
    return () => {
      stopped = true;
      document.removeEventListener("visibilitychange", visibilityChanged);
      disconnect();
    };
  }
}

export const posApprovalClient = new PosApprovalClient();
