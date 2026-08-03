import { apiClient } from "@/services/api/client";
import type { SalesWorkspaceOption } from "@/services/pos/online-pos-client";

export type SalesWorkspaceBootstrap = {
  tenantName: string;
  userId: string;
  userDisplayName: string;
  options: SalesWorkspaceOption[];
  canEnrollPosDevice: boolean;
};

export async function loadSalesWorkspaceBootstrap(): Promise<SalesWorkspaceBootstrap> {
  return apiClient.get<SalesWorkspaceBootstrap>(
    "/commerce/v1/pos/workspace/bootstrap",
  );
}
