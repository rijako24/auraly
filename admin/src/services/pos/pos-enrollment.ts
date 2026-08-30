import type { SalesWorkspaceOption } from "@/services/pos/online-pos-client";
import { fetchWithSessionRetry } from "@/services/api/client";
import type { PosSensitiveAuthorization } from "@/services/pos/pos-edge-client";
import { posEnrollmentProblemDetail } from "./pos-enrollment-error";

const EDGE_BASE_URL =
  process.env.NEXT_PUBLIC_AURALY_POS_EDGE_URL ?? "http://127.0.0.1:47831";

export type PosEnrollmentAuthorization = {
  enrollmentSessionId: string;
  redemptionCode: string;
  expiresAt: string;
  workspace: Omit<SalesWorkspaceOption, "hasActiveEdgeEnrollment">;
};

export async function authorizePosEnrollment(
  option: SalesWorkspaceOption,
  draftId?: string,
  authorization?: PosSensitiveAuthorization,
): Promise<PosEnrollmentAuthorization> {
  const response = await fetchWithSessionRetry("/api/commerce/v1/pos/enrollments", {
    method: "POST",
    cache: "no-store",
    credentials: "include",
    headers: {
      "Content-Type": "application/json",
      ...(window.localStorage.getItem("selected_tenant_id")
        ? { "X-Tenant-Id": window.localStorage.getItem("selected_tenant_id")! }
        : {}),
      "X-Business-Id": option.businessId,
      ...(draftId ? { "X-Auraly-Draft-Id": draftId } : {}),
      ...(authorization?.approvalRequestId
        ? { "X-Auraly-Approval-Id": authorization.approvalRequestId }
        : {}),
      ...(authorization?.operationId
        ? { "Idempotency-Key": authorization.operationId }
        : {}),
    },
    body: JSON.stringify({
      businessId: option.businessId,
      warehouseId: option.warehouseId,
      deviceName: window.navigator.userAgentData?.platform
        ? `Auraly · ${window.navigator.userAgentData.platform}`
        : `Auraly · ${window.navigator.platform || "Windows"}`,
    }),
  });
  if (!response.ok) throw new Error(await posEnrollmentProblemDetail(response));
  return (await response.json()) as PosEnrollmentAuthorization;
}

export async function redeemPosEnrollment(
  edgeSessionToken: string,
  authorization: PosEnrollmentAuthorization,
): Promise<void> {
  const response = await fetch(`${EDGE_BASE_URL}/edge/v1/enrollment/redeem`, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      "X-Auraly-Edge-Session": edgeSessionToken,
    },
    body: JSON.stringify({
      enrollmentSessionId: authorization.enrollmentSessionId,
      redemptionCode: authorization.redemptionCode,
    }),
  });
  if (!response.ok) throw new Error(await posEnrollmentProblemDetail(response));
}

export async function waitForRedeemedPosEdge(
  edgeSessionToken: string,
  timeoutMilliseconds = 30_000,
): Promise<void> {
  const deadline = Date.now() + timeoutMilliseconds;
  await new Promise((resolve) => window.setTimeout(resolve, 1_500));
  while (Date.now() < deadline) {
    try {
      const response = await fetch(`${EDGE_BASE_URL}/edge/v1/health`, {
        cache: "no-store",
        headers: { "X-Auraly-Edge-Session": edgeSessionToken },
      });
      if (response.ok) {
        const health = await response.json() as { status?: string };
        if (health.status && health.status !== "EnrollmentRequired") return;
      }
    } catch {
      // The service is expected to be briefly unavailable while it restarts.
    }
    await new Promise((resolve) => window.setTimeout(resolve, 500));
  }
  throw new Error("El equipo quedó enrolado, pero el servicio local no volvió a iniciar. Cierra y abre Auraly.");
}

declare global {
  interface Navigator {
    userAgentData?: { platform?: string };
  }
}
