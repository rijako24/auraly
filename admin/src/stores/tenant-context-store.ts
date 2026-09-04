import { create } from "zustand";
import { resolveAuthorizedSelection } from "@/lib/execution-context-selection";

export type ExecutionTenant = {
  tenantId: string;
  name: string;
};

interface TenantContextState {
  tenants: ExecutionTenant[];
  selectedTenantId: string | null;
  isLoaded: boolean;
  setTenants: (tenants: ExecutionTenant[]) => void;
  selectTenant: (tenantId: string) => void;
  establishIdentityTenant: (tenantId: string) => void;
  resetSession: () => void;
}

const STORAGE_KEY = "selected_tenant_id";

function persistedTenantId(): string | null {
  if (typeof window === "undefined") return null;
  try {
    return localStorage.getItem(STORAGE_KEY);
  } catch {
    return null;
  }
}

function persistTenantId(tenantId: string | null): void {
  if (typeof window === "undefined") return;
  try {
    if (tenantId) localStorage.setItem(STORAGE_KEY, tenantId);
    else localStorage.removeItem(STORAGE_KEY);
  } catch {
    /* Browser storage is optional; server authorization remains authoritative. */
  }
}

export const useTenantContextStore = create<TenantContextState>((set, get) => ({
  tenants: [],
  selectedTenantId: persistedTenantId(),
  isLoaded: false,
  setTenants: (tenants) => {
    const current = get().selectedTenantId;
    const selectedTenantId = resolveAuthorizedSelection(
      tenants.map((tenant) => tenant.tenantId),
      current,
    );
    persistTenantId(selectedTenantId);
    set({ tenants, selectedTenantId, isLoaded: true });
  },
  selectTenant: (tenantId) => {
    if (!get().tenants.some((tenant) => tenant.tenantId === tenantId)) return;
    persistTenantId(tenantId);
    set({ selectedTenantId: tenantId });
  },
  establishIdentityTenant: (tenantId) => {
    persistTenantId(tenantId);
    set({ tenants: [], selectedTenantId: tenantId, isLoaded: false });
  },
  resetSession: () =>
    set({ tenants: [], selectedTenantId: persistedTenantId(), isLoaded: false }),
}));
