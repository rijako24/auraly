export function resolveAuthorizedSelection(
  authorizedIds: readonly string[],
  lastSelectedId: string | null,
): string | null {
  if (lastSelectedId) {
    const authorized = authorizedIds.find(
      (candidate) => candidate.toLowerCase() === lastSelectedId.toLowerCase(),
    );
    if (authorized) return authorized;
  }
  return authorizedIds[0] ?? null;
}
