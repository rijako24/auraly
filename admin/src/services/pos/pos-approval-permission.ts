export function approvalRequestConfirmsExistingPermission(error: unknown): boolean {
  if (typeof error !== "object" || error === null) return false;
  return (error as { code?: unknown }).code === "PermissionAlreadyGranted";
}
