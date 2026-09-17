export type PosLaunchHealth = {
  status: string;
  identityReady: boolean;
};

export function usesEnrolledPosRuntime(health: PosLaunchHealth) {
  return health.status !== "EnrollmentRequired";
}

export function shouldUseEnrolledPosRuntime(
  health: PosLaunchHealth,
  workspaceChangeRequested: boolean,
) {
  return usesEnrolledPosRuntime(health) && !workspaceChangeRequested;
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
