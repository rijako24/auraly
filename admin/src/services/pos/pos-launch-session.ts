export type PosLaunchHealth = {
  status: string;
  identityReady: boolean;
};

export type EnrolledWorkspaceSnapshot = {
  businessId: string;
  businessName: string;
  warehouseId: string;
  warehouseName: string;
  warehouseAllowsNegativeStockSales: boolean;
  fiscalReady: boolean;
  fiscalWarnings: string[];
  dianQuotaAvailable: boolean | null;
};

export type EnrolledWorkspaceOption = {
  businessId: string;
  businessName: string;
  warehouseId: string;
  warehouseCode: string;
  warehouseName: string;
  warehouseAllowsNegativeStockSales: boolean;
  hasActiveEdgeEnrollment: boolean;
  fiscalReadyForOnlineSales: boolean;
  fiscalReadyForEnrollment: boolean;
  hasDianDocumentQuota: boolean;
  fiscalWarningMessages: string[];
};

export function enrolledWorkspaceOption(
  workstation: EnrolledWorkspaceSnapshot,
): EnrolledWorkspaceOption {
  return {
    businessId: workstation.businessId,
    businessName: workstation.businessName,
    warehouseId: workstation.warehouseId,
    warehouseCode: "",
    warehouseName: workstation.warehouseName,
    warehouseAllowsNegativeStockSales:
      workstation.warehouseAllowsNegativeStockSales,
    hasActiveEdgeEnrollment: true,
    fiscalReadyForOnlineSales: workstation.fiscalReady,
    fiscalReadyForEnrollment: workstation.fiscalReady,
    hasDianDocumentQuota: workstation.dianQuotaAvailable !== false,
    fiscalWarningMessages: workstation.fiscalWarnings,
  };
}

export function resolvePosExecutionMode(
  hasInstalledApp: boolean,
  health: PosLaunchHealth | null,
): "online" | "edge" | null {
  if (!hasInstalledApp) return "online";
  if (!health) return null;
  return health.status === "EnrollmentRequired" ? "online" : "edge";
}

export function workspaceActivationMode(
  currentMode: "edge" | "online" | null,
  currentBusinessId: string,
  currentWarehouseId: string,
  selectedBusinessId: string,
  selectedWarehouseId: string,
): "keep-edge" | "activate-online" | "reenrollment-required" {
  if (currentMode !== "edge") return "activate-online";
  return currentBusinessId === selectedBusinessId &&
    currentWarehouseId === selectedWarehouseId
    ? "keep-edge"
    : "reenrollment-required";
}

export function installedPosLaunchDestination(health: PosLaunchHealth | null) {
  void health;
  return "/login";
}

export function shouldAutoActivateRememberedWorkspace(
  rememberedWorkspaceKey: string | null,
) {
  return Boolean(rememberedWorkspaceKey);
}
