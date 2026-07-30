import type { OnlineRegisterOption } from "@/services/pos/online-pos-client";

export type OnlineRegisterBootstrap = {
  tenantName: string;
  userDisplayName: string;
  options: OnlineRegisterOption[];
  canEnrollPosDevice: boolean;
};

export async function loadOnlineRegisterBootstrap(): Promise<OnlineRegisterBootstrap> {
  const response = await fetch(
    "/api/commerce/v1/pos/register-context/bootstrap",
    {
      cache: "no-store",
      credentials: "include",
      headers: { "Content-Type": "application/json" },
    },
  );
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
      // Preserve the server response when it is not JSON.
    }
    throw new Error(detail);
  }
  return (await response.json()) as OnlineRegisterBootstrap;
}
