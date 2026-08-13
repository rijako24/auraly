"use client";

import { useEffect, useState, type ReactNode } from "react";
import { CloudOff } from "lucide-react";

export function PwaProvider({ children }: { children: ReactNode }) {
  const [online, setOnline] = useState(true);

  useEffect(() => {
    setOnline(navigator.onLine);
    const connected = () => setOnline(true);
    const disconnected = () => setOnline(false);
    window.addEventListener("online", connected);
    window.addEventListener("offline", disconnected);
    if ("serviceWorker" in navigator && process.env.NODE_ENV === "production") {
      void navigator.serviceWorker.register("/app-sw.js", { scope: "/" });
    }
    return () => {
      window.removeEventListener("online", connected);
      window.removeEventListener("offline", disconnected);
    };
  }, []);

  return <>{children}{!online && (
    <div role="status" className="fixed inset-x-3 bottom-3 z-[100] mx-auto flex max-w-md items-center justify-center gap-2 rounded-2xl bg-amber-950 px-4 py-3 text-sm font-medium text-white shadow-2xl">
      <CloudOff className="h-4 w-4"/>
      Sin conexión. El trabajo compatible se guardará localmente.
    </div>
  )}</>;
}
