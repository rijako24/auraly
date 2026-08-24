"use client";

import { useEffect } from "react";
import { Loader2 } from "lucide-react";
import { readEdgeTokenFromLaunch } from "@/services/pos/pos-edge-client";

export default function PosLaunchPage() {
  useEffect(() => {
    readEdgeTokenFromLaunch();
    window.location.replace("/pos");
  }, []);

  return <div className="flex min-h-48 items-center justify-center gap-2 text-sm text-[#667f7d]">
    <Loader2 className="h-5 w-5 animate-spin" /> Preparando Auraly POS…
  </div>;
}
