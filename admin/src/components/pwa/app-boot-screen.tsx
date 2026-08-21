"use client";

import { useEffect, useState } from "react";

export function AppBootScreen() {
  const [visible, setVisible] = useState(true);

  useEffect(() => {
    let secondFrame = 0;
    const firstFrame = requestAnimationFrame(() => {
      secondFrame = requestAnimationFrame(() => setVisible(false));
    });
    const fallback = window.setTimeout(() => setVisible(false), 1200);
    return () => {
      cancelAnimationFrame(firstFrame);
      cancelAnimationFrame(secondFrame);
      window.clearTimeout(fallback);
    };
  }, []);

  if (!visible) return null;
  return <div id="auraly-standalone-boot" aria-label="Iniciando Auraly" />;
}
