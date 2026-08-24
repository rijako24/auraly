"use client";

import { useEffect } from "react";
import { PosEdgeClient, readEdgeTokenFromLaunch } from "@/services/pos/pos-edge-client";
import {
  clearInstalledPosUserSession,
  installedPosLaunchDestination,
} from "@/services/pos/pos-launch-session";
import { AuralyLoadingState } from "@/components/brand/auraly-loading-state";
import { useAuthStore } from "@/stores/auth-store";

export default function PosLaunchPage() {
  useEffect(() => {
    let active = true;
    const launch = async () => {
      const edgeToken = readEdgeTokenFromLaunch();
      clearInstalledPosUserSession(window.sessionStorage);
      useAuthStore.getState().clearAuth();
      if (!edgeToken) {
        window.location.replace("/login?redirect=%2Fpos");
        return;
      }
      const health = await new PosEdgeClient(edgeToken).health().catch(() => null);
      if (active) window.location.replace(installedPosLaunchDestination(health));
    };
    void launch();
    return () => { active = false; };
  }, []);

  return <AuralyLoadingState title="Preparando Auraly POS" overlay />;
}
