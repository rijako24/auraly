"use client";

import { useEffect } from "react";
import { readEdgeTokenFromLaunch } from "@/services/pos/pos-edge-client";
import { AuralyLoadingState } from "@/components/brand/auraly-loading-state";

export default function PosLaunchPage() {
  useEffect(() => {
    readEdgeTokenFromLaunch();
    window.location.replace("/pos");
  }, []);

  return <AuralyLoadingState title="Preparando Auraly POS" overlay />;
}
