import { apiClient } from "@/services/api/client";
import type { SalesWorkspaceOption } from "@/services/pos/online-pos-client";

export type SalesWorkspaceBootstrap = {
  tenantId: string;
  tenantName: string;
  userId: string;
  userDisplayName: string;
  options: SalesWorkspaceOption[];
  canEnrollPosDevice: boolean;
  hasEnrollmentPermission: boolean;
  activeEnrolledDeviceCount: number;
  maximumEnrolledDevices: number;
  enrollmentUnavailableReason: string | null;
};

// One navigation transfers its already authorized bootstrap to the restaurant entry.
export const salesWorkspaceBootstrapHandoffKey = (tenantId: string, userId: string) =>
  ["pos-workspace-bootstrap-handoff", tenantId, userId] as const;

export async function loadSalesWorkspaceBootstrap(): Promise<SalesWorkspaceBootstrap> {
  return apiClient.get<SalesWorkspaceBootstrap>(
    "/commerce/v1/pos/workspace/bootstrap",
    undefined,
    { cache: "no-store" },
  );
}
