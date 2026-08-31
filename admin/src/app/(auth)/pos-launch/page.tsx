"use client";

import { useEffect } from "react";
import { readEdgeTokenFromLaunch } from "@/services/pos/pos-edge-client";
import { AuralyLoadingState } from "@/components/brand/auraly-loading-state";
import { useAuthStore } from "@/stores/auth-store";

export default function PosLaunchPage() {
  useEffect(() => {
    let active = true;
    const launch = async () => {
      const edgeToken = readEdgeTokenFromLaunch();
      if (!edgeToken) {
        window.location.replace("/login");
        return;
      }
      useAuthStore.getState().clearAuth();
      if (active) window.location.replace("/login");
    };
    void launch();
    return () => { active = false; };
  }, []);

  return <AuralyLoadingState title="Preparando Auraly" overlay />;
}
