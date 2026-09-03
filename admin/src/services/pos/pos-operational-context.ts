export function posOperationalContextKey(
  tenantId: string | null | undefined,
  userId: string | null | undefined,
): string | null {
  const tenant = tenantId?.trim();
  const user = userId?.trim();
  return tenant && user ? `${tenant}:${user}` : null;
}

export function posWorkspaceStorageKey(
  tenantId: string | null | undefined,
  userId: string | null | undefined,
): string | null {
  const context = posOperationalContextKey(tenantId, userId);
  return context ? `auraly.pos.sales-workspace:${context}` : null;
}

export function posWorkspaceOptionsCacheKey(
  tenantId: string | null | undefined,
  userId: string | null | undefined,
): string | null {
  const context = posOperationalContextKey(tenantId, userId);
  return context ? `available:${context}` : null;
}
