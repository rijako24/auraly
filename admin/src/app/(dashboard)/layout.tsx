"use client";

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

  return (
    <AuthSyncProvider>
      <div className="flex h-screen overflow-hidden bg-background">
        {/* Desktop sidebar - hidden on mobile */}
        {!isMobile && <Sidebar />}

        {/* Mobile nav - Sheet slides from left */}
        <MobileNav />

        {/* Main content area */}
        <div className="flex flex-1 flex-col min-w-0">
          <Header onMobileMenuClick={handleMobileMenuClick} />
          <main className="flex-1 overflow-auto p-4 lg:p-6">
            <BusinessContextProvider>{children}</BusinessContextProvider>
          </main>
        </div>
      </div>
    </AuthSyncProvider>
  );
}
