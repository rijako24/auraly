import { create } from "zustand";
import type { Business } from "@/types/entities";

interface BusinessContextState {
  businesses: Business[];
  selectedBusinessId: string | null;
  isLoaded: boolean;
  setBusinesses: (businesses: Business[]) => void;
  selectBusiness: (businessId: string) => void;
  reset: () => void;
}

function loadPersistedBusinessId(): string | null {
  if (typeof window === "undefined") return null;
  try {
    return localStorage.getItem("selected_business_id");
  } catch {
    return null;
  }
}

function persistBusinessId(id: string | null) {
  if (typeof window === "undefined") return;
  try {
    if (id) localStorage.setItem("selected_business_id", id);
    else localStorage.removeItem("selected_business_id");
  } catch {
    /* noop */
  }
}

export const useBusinessContextStore = create<BusinessContextState>()(
  (set, get) => ({
    businesses: [],
    selectedBusinessId: loadPersistedBusinessId(),
    isLoaded: false,

    setBusinesses: (businesses) => {
      const current = get().selectedBusinessId;
      const validSelection =
        current != null &&
        businesses.some((b) => b.businessId === current);

      const nextId = validSelection
        ? current
        : businesses[0]?.businessId ?? null;

      persistBusinessId(nextId);
      set({
        businesses,
        isLoaded: true,
        selectedBusinessId: nextId,
      });
    },

    selectBusiness: (businessId) => {
      persistBusinessId(businessId);
      set({ selectedBusinessId: businessId });
    },

    reset: () => {
      persistBusinessId(null);
      set({
        businesses: [],
        selectedBusinessId: null,
        isLoaded: false,
      });
    },
  })
);
