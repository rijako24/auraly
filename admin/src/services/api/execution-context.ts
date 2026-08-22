import type { BusinessContextOption } from "@/stores/business-context-store";
import type { ExecutionTenant } from "@/stores/tenant-context-store";
import { fetchWithSessionRetry } from "./client";

export type ExecutionAccess = {
  tenantId: string;
  businessId: string | null;
  roles: string[];
  permissions: string[];
};

function currentUserId() {
  try {
    const persisted = JSON.parse(localStorage.getItem("auth-state") ?? "null") as { state?: { user?: { userId?: string } } } | null;
    return persisted?.state?.user?.userId ?? "anonymous";
  } catch { return "anonymous"; }
}

function contextCacheKey(path: string, tenantId?: string, businessId?: string) {
  return `auraly.offline.context:${currentUserId()}:${path}:${tenantId ?? ""}:${businessId ?? ""}`;
}

function readCachedContext<T>(key: string): T | null {
  try { return JSON.parse(localStorage.getItem(key) ?? "null") as T | null; } catch { return null; }
}

async function request<T>(
  path: string,
  tenantId?: string,
  businessId?: string,
): Promise<T> {
  const headers: Record<string, string> = { "Content-Type": "application/json" };
  if (tenantId) headers["X-Tenant-Id"] = tenantId;
  if (businessId) headers["X-Business-Id"] = businessId;
  const controller = new AbortController();
  const timeout = window.setTimeout(() => controller.abort(), 15_000);
  const key = contextCacheKey(path, tenantId, businessId);
  let response: Response;
  try {
    response = await fetchWithSessionRetry(`/api/execution-context/${path}`, {
      credentials: "include",
      cache: "no-store",
      headers,
      signal: controller.signal,
    });
  } catch (error) {
    const cached = readCachedContext<T>(key);
    if (cached) return cached;
    throw error;
  } finally { window.clearTimeout(timeout); }
  if (!response.ok) {
    const problem = await response.json().catch(() => null) as
      | { detail?: string; title?: string }
      | null;
    throw new Error(problem?.detail ?? problem?.title ?? "No se pudo cargar el contexto de trabajo.");
  }
  const value = await response.json() as T;
  try { localStorage.setItem(key, JSON.stringify(value)); } catch { /* Offline cache is best effort. */ }
  return value;
}

export const executionContextApi = {
  tenants: () => request<ExecutionTenant[]>("tenants"),
  businesses: (tenantId: string) =>
    request<BusinessContextOption[]>("businesses", tenantId),
  access: (tenantId: string, businessId: string) =>
    request<ExecutionAccess>("access", tenantId, businessId),
};
