"use client";

import { useEffect, useState } from "react";
import { AuralyLoadingState } from "@/components/brand/auraly-loading-state";

export function AppBootScreen() {
  const [visible, setVisible] = useState(true);

  useEffect(() => {
    const minimumDisplay = window.setTimeout(() => setVisible(false), 900);
    return () => window.clearTimeout(minimumDisplay);
  }, []);

  if (!visible) return null;
  return <AuralyLoadingState title="Iniciando Auraly" overlay />;
}
