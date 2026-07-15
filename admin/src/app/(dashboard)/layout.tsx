"use client";

import { useEffect } from "react";
import { useSidebarStore } from "@/stores/sidebar-store";

import { Header } from "@/components/layout/header";
import { MobileNav } from "@/components/layout/mobile-nav";
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

  const handleMobileMenuClick = () => setOpen(true);

  useEffect(() => {
    document.body.classList.add("dashboard-shell");
    return () => document.body.classList.remove("dashboard-shell");
  }, []);

  return (
    <AuthSyncProvider>
      <div className="flex h-dvh overflow-hidden bg-background">
        {/* Desktop sidebar - hidden on mobile */}
        {!isMobile && <Sidebar />}

        {/* Mobile nav - Sheet slides from left */}
        <MobileNav />

        {/* Main content area */}
        <div className="flex min-h-0 min-w-0 flex-1 flex-col">
          <Header onMobileMenuClick={handleMobileMenuClick} />
          <main className="min-h-0 min-w-0 flex-1 overflow-y-auto overflow-x-hidden bg-background px-3 py-4 sm:px-5 lg:px-8 lg:py-7">
            <BusinessContextProvider>{children}</BusinessContextProvider>
          </main>
        </div>
      </div>
    </AuthSyncProvider>
  );
}
