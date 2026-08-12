import { create } from "zustand";
import { resolveAuthorizedSelection } from "@/lib/execution-context-selection";
import type { Business } from "@/types/entities";

export type BusinessContextOption = Pick<
  Business,
  "businessId" | "tenantId" | "name"
>;

interface BusinessContextState {
  businesses: BusinessContextOption[];
  selectedBusinessId: string | null;
  isLoaded: boolean;
  setBusinesses: (businesses: BusinessContextOption[]) => void;
  selectBusiness: (businessId: string) => void;
  clearForTenantChange: () => void;
  reset: () => void;
}

const STORAGE_KEY = "selected_business_id";

function loadPersistedBusinessId(): string | null {
  if (typeof window === "undefined") return null;
  try {
    return localStorage.getItem(STORAGE_KEY);
  } catch {
    return null;
  }
}

function persistBusinessId(id: string | null) {
  if (typeof window === "undefined") return;
  try {
    if (id) localStorage.setItem(STORAGE_KEY, id);
    else localStorage.removeItem(STORAGE_KEY);
  } catch {
    /* Browser storage is optional; server authorization remains authoritative. */
  }
}

export const useBusinessContextStore = create<BusinessContextState>()(
  (set, get) => ({
    businesses: [],
    selectedBusinessId: loadPersistedBusinessId(),
    isLoaded: false,

    setBusinesses: (businesses) => {
      const nextId = resolveAuthorizedSelection(
        businesses.map((business) => business.businessId),
        get().selectedBusinessId,
      );
      persistBusinessId(nextId);
      set({ businesses, isLoaded: true, selectedBusinessId: nextId });
    },

    selectBusiness: (businessId) => {
      if (!get().businesses.some((business) => business.businessId === businessId)) return;
      persistBusinessId(businessId);
      set({ selectedBusinessId: businessId });
    },

    clearForTenantChange: () => {
      persistBusinessId(null);
      set({ businesses: [], selectedBusinessId: null, isLoaded: false });
    },

    reset: () =>
      set({ businesses: [], selectedBusinessId: loadPersistedBusinessId(), isLoaded: false }),
  }),
);