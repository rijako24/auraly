"use client";

import { useEffect, useState } from "react";
import { useSidebarStore } from "@/stores/sidebar-store";

import { Header } from "@/components/layout/header";
import { MobileNav } from "@/components/layout/mobile-nav";
import { MobileBottomNav } from "@/components/layout/mobile-bottom-nav";
import { Sidebar } from "@/components/layout/sidebar";
import { AuthSyncProvider } from "@/providers/auth-sync-provider";
import { BusinessContextProvider } from "@/providers/business-context-provider";

import { useMediaQuery } from "@/hooks/use-media-query";

export default function DashboardLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  const setOpen = useSidebarStore((s) => s.setOpen);
  const isMobile = useMediaQuery("(max-width: 1024px)");
  const [viewportHeight, setViewportHeight] = useState("100dvh");

  const handleMobileMenuClick = () => setOpen(true);

  useEffect(() => {
    document.body.classList.add("dashboard-shell");
    return () => document.body.classList.remove("dashboard-shell");
  }, []);

  useEffect(() => {
    const viewport = window.visualViewport;
    let frame = 0;
    const update = () => {
      cancelAnimationFrame(frame);
      frame = requestAnimationFrame(() =>
        setViewportHeight(`${Math.round(viewport?.height ?? window.innerHeight)}px`));
    };
    update();
    viewport?.addEventListener("resize", update);
    viewport?.addEventListener("scroll", update);
    window.addEventListener("resize", update);
    window.addEventListener("orientationchange", update);
    window.addEventListener("pageshow", update);
    return () => {
      cancelAnimationFrame(frame);
      viewport?.removeEventListener("resize", update);
      viewport?.removeEventListener("scroll", update);
      window.removeEventListener("resize", update);
      window.removeEventListener("orientationchange", update);
      window.removeEventListener("pageshow", update);
    };
  }, []);

  return (
    <AuthSyncProvider>
      <div className="flex overflow-hidden bg-background" style={{ height: viewportHeight }}>
        {/* Desktop sidebar - hidden on mobile */}
        {!isMobile && <Sidebar />}

        {/* Mobile nav - Sheet slides from left */}
        <MobileNav />

        {/* Main content area */}
        <div className="flex min-h-0 min-w-0 flex-1 flex-col">
          <Header onMobileMenuClick={handleMobileMenuClick} />
          <main className="min-h-0 min-w-0 flex-1 overflow-y-auto overflow-x-hidden bg-background px-3 pb-5 pt-4 sm:px-5 lg:px-8 lg:py-7">
            <BusinessContextProvider>{children}</BusinessContextProvider>
          </main>
          <MobileBottomNav />
        </div>
      </div>
    </AuthSyncProvider>
  );
}
