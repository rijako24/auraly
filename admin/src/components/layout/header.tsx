"use client";

import { Menu } from "lucide-react";

import { BreadcrumbNav } from "@/components/layout/breadcrumb-nav";
import { BusinessSwitcher } from "@/components/layout/business-switcher";
import { NotificationsDropdown } from "@/components/layout/notifications-dropdown";
import { SearchCommand } from "@/components/layout/search-command";
import { ThemeToggle } from "@/components/layout/theme-toggle";
import { UserMenu } from "@/components/layout/user-menu";
import { Button } from "@/components/ui/button";
import { cn } from "@/lib/utils";

interface HeaderProps {
  onMobileMenuClick?: () => void;
  className?: string;
}

export function Header({ onMobileMenuClick, className }: HeaderProps) {
  return (
    <header
      className={cn(
        "sticky top-0 z-40 flex min-h-14 shrink-0 items-center gap-2 border-b border-border bg-background/95 px-2 py-2 backdrop-blur supports-[backdrop-filter]:bg-background/60 sm:gap-3 sm:px-4 lg:gap-4",
        className
      )}
    >
      {/* Mobile menu button */}
      <Button
        variant="ghost"
        size="icon"
        className="h-9 w-9 shrink-0 lg:hidden"
        onClick={onMobileMenuClick}
        aria-label="Abrir menú"
      >
        <Menu className="h-5 w-5" />
      </Button>

      <BusinessSwitcher />

      <BreadcrumbNav className="hidden min-w-0 flex-1 sm:block" />

      <div className="ml-auto flex shrink-0 items-center gap-0.5 sm:gap-1">
        {/* Search - opens command palette */}
        <SearchCommand />

        {/* Notifications */}
        <NotificationsDropdown />

        {/* Theme toggle */}
        <ThemeToggle />

        {/* User menu */}
        <UserMenu />
      </div>
    </header>
  );
}
