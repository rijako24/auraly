import type { SalesWorkspaceOption } from "@/services/pos/online-pos-client";

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
): Promise<PosEnrollmentAuthorization> {
  const response = await fetch("/api/commerce/v1/pos/enrollments", {
    method: "POST",
    cache: "no-store",
    credentials: "include",
    headers: {
      "Content-Type": "application/json",
      ...(window.localStorage.getItem("selected_tenant_id")
        ? { "X-Tenant-Id": window.localStorage.getItem("selected_tenant_id")! }
        : {}),
      "X-Business-Id": option.businessId,
    },
    body: JSON.stringify({
      businessId: option.businessId,
      warehouseId: option.warehouseId,
      deviceName: window.navigator.userAgentData?.platform
        ? `Auraly POS · ${window.navigator.userAgentData.platform}`
        : `Auraly POS · ${window.navigator.platform || "Windows"}`,
    }),
  });
  if (!response.ok) throw new Error(await problemDetail(response));
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
  if (!response.ok) throw new Error(await problemDetail(response));
}

async function problemDetail(response: Response): Promise<string> {
  const raw = await response.text();
  try {
    const problem = JSON.parse(raw) as {
      detail?: string;
      title?: string;
      message?: string;
    };
    return problem.detail || problem.message || problem.title || raw;
  } catch {
    return raw || response.statusText;
  }
}

declare global {
  interface Navigator {
    userAgentData?: { platform?: string };
  }
}
