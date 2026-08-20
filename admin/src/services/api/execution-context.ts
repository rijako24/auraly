import type { BusinessContextOption } from "@/stores/business-context-store";
import type { ExecutionTenant } from "@/stores/tenant-context-store";

export type ExecutionAccess = {
  tenantId: string;
  businessId: string | null;
  roles: string[];
  permissions: string[];
};

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
  const response = await fetch(`/api/execution-context/${path}`, {
    credentials: "include",
    cache: "no-store",
    headers,
    signal: controller.signal,
  }).finally(() => window.clearTimeout(timeout));
  if (!response.ok) {
    const problem = await response.json().catch(() => null) as
      | { detail?: string; title?: string }
      | null;
    throw new Error(problem?.detail ?? problem?.title ?? "No se pudo cargar el contexto de trabajo.");
  }
  return response.json() as Promise<T>;
}

export const executionContextApi = {
  tenants: () => request<ExecutionTenant[]>("tenants"),
  businesses: (tenantId: string) =>
    request<BusinessContextOption[]>("businesses", tenantId),
  access: (tenantId: string, businessId: string) =>
    request<ExecutionAccess>("access", tenantId, businessId),
};
