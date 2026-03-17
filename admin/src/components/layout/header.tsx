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
        "sticky top-0 z-40 flex h-14 shrink-0 items-center gap-4 border-b border-border bg-background/95 px-4 backdrop-blur supports-[backdrop-filter]:bg-background/60",
        className
      )}
    >
      {/* Mobile menu button */}
      <Button
        variant="ghost"
        size="icon"
        className="lg:hidden"
        onClick={onMobileMenuClick}
        aria-label="Abrir menú"
      >
        <Menu className="h-5 w-5" />
      </Button>

      <BusinessSwitcher />

      <BreadcrumbNav className="flex-1 min-w-0" />

      <div className="flex items-center gap-1">
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
