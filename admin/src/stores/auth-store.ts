import { create } from "zustand";
import { persist } from "zustand/middleware";
import type { AuthUser } from "@/types/api";

interface AuthState {
  user: AuthUser | null;
  isAuthenticated: boolean;
  setAuth: (user: AuthUser) => void;
  setExecutionAccess: (roles: string[], permissions: string[]) => void;
  logout: () => Promise<void>;
  clearAuth: () => void;
}

export const useAuthStore = create<AuthState>()(
  persist(
    (set) => ({
      user: null,
      isAuthenticated: false,
      setAuth: (user) => {
        set({ user, isAuthenticated: true });
      },
      setExecutionAccess: (roles, permissions) =>
        set((state) => ({
          user: state.user ? { ...state.user, roles, permissions } : null,
        })),
      clearAuth: () => set({ user: null, isAuthenticated: false }),
      logout: async () => {
        set({ user: null, isAuthenticated: false });
        try {
          await fetch("/api/auth/logout", {
            method: "POST",
            credentials: "include",
          });
        } catch {
          // The local session is already closed; the server cookie can expire
          // independently when the device recovers connectivity.
        }
      },
    }),
    {
      name: "auth-state",
      partialize: (state) => ({ user: state.user, isAuthenticated: state.isAuthenticated }),
    }
  )
);
