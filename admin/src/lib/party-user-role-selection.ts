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

export type PartyRoleSaveHandler = { save: () => Promise<void>; validate?: () => void };

export function snapshotSaveHandlers<T>(handlers: ReadonlyMap<string, T>) {
  return [...handlers.values()];
}
