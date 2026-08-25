import type { PartyUserRoleAssignment } from "@/services/api/parties";

export const configuredPasswordMask = "••••••••••";

const sameId = (left: string | null, right: string | null) =>
  left?.toLocaleLowerCase() === right?.toLocaleLowerCase();

export function effectiveUserRoleAssignments(
  assignments: PartyUserRoleAssignment[],
  businessId: string | null,
) {
  if (!businessId) return assignments.filter((item) => item.businessId === null);
  return assignments.filter(
    (item) => item.businessId === null || sameId(item.businessId, businessId),
  );
}

export function snapshotSaveHandlers(handlers: Map<string, () => Promise<void>>) {
  return [...handlers.values()];
}
