"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import {
  ChevronRight,
  ChevronsLeft,
  ChevronsRight,
} from "lucide-react";

import { AuralyLogo } from "@/components/brand/auraly-logo";
import { Avatar, AvatarFallback, AvatarImage } from "@/components/ui/avatar";
import { Button } from "@/components/ui/button";
import { Collapsible, CollapsibleContent, CollapsibleTrigger } from "@/components/ui/collapsible";
import { Separator } from "@/components/ui/separator";
import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from "@/components/ui/tooltip";
import { cn } from "@/lib/utils";
import { getInitials } from "@/lib/utils";
import { useAuthStore } from "@/stores/auth-store";
import { useSidebarStore } from "@/stores/sidebar-store";

import { navigation } from "./sidebar-nav-config";

function hasPermission(permissions: Set<string>, item: { permission?: string }): boolean {
  if (!item.permission) return true;
  return permissions.has(item.permission);
}

export function Sidebar() {
  const pathname = usePathname();
  const { isCollapsed, setCollapsed } = useSidebarStore();
  const { user } = useAuthStore();

  const permissions = new Set(user?.permissions ?? []);

  // Group navigation by separators, filtering by permission (show if user has at least one permission in module)
  const groups: { label: string; items: (typeof navigation)[number][] }[] = [];
  let currentGroup: { label: string; items: (typeof navigation)[number][] } | null = null;

  for (const entry of navigation) {
    if ("type" in entry && entry.type === "separator") {
      currentGroup = { label: entry.label, items: [] };
      groups.push(currentGroup);
    } else if ("href" in entry && hasPermission(permissions, entry)) {
      if (!currentGroup) {
        currentGroup = { label: "Principal", items: [] };
        groups.push(currentGroup);
      }
      currentGroup.items.push(entry);
    }
  }

  const filteredGroups = groups.filter((g) => g.items.length > 0);

  return (
    <aside
      className={cn(
        "relative flex h-dvh shrink-0 flex-col overflow-visible border-r border-sidebar-border bg-sidebar-background transition-[width] duration-200 ease-out will-change-[width] motion-reduce:transition-none",
        isCollapsed ? "w-[60px]" : "w-64"
      )}
    >
      <TooltipProvider delayDuration={0}>
        {/* Logo / Brand */}
        <div className="flex h-[72px] shrink-0 items-center border-b border-sidebar-border px-4">
          {isCollapsed ? (
            <Tooltip>
              <TooltipTrigger asChild>
                <Link
                  href="/dashboard"
                  className="flex items-center justify-center w-full rounded-md hover:bg-sidebar-accent transition-colors"
                >
                  <AuralyLogo collapsed markClassName="h-7 w-8" />
                </Link>
              </TooltipTrigger>
              <TooltipContent side="right">AURALY Admin</TooltipContent>
            </Tooltip>
          ) : (
            <Link
              href="/dashboard"
              className="flex items-center rounded-md px-2 py-1.5 hover:bg-sidebar-accent transition-colors"
            >
              <AuralyLogo />
            </Link>
          )}
        </div>

        {/* Navigation */}
        <div className="flex-1 overflow-y-auto py-5 [scrollbar-width:none] [&::-webkit-scrollbar]:hidden">
          <div className={cn(isCollapsed ? "space-y-0 px-2 py-4" : "space-y-3 px-3")}>
            {filteredGroups.map((group) => (
              <Collapsible key={group.label} defaultOpen className="group">
                <CollapsibleTrigger asChild>
                  {isCollapsed ? (
                    <div className="hidden" />
                  ) : (
                    <button className="flex w-full items-center gap-2 rounded-md px-2 py-1.5 text-[11px] font-semibold uppercase tracking-[0.08em] text-sidebar-foreground/55 hover:text-sidebar-foreground hover:bg-sidebar-accent transition-colors">
                      <ChevronRight className="h-3.5 w-3.5 transition-transform group-data-[state=open]:rotate-90" />
                      {group.label}
                    </button>
                  )}
                </CollapsibleTrigger>
                <CollapsibleContent>
                  <div className={cn(isCollapsed ? "space-y-1" : "mt-1 space-y-0.5 pl-4 pr-1")}>
                    {group.items.map((item) => {
                      if (!("href" in item)) return null;
                      const Icon = item.icon;
                      const isActive = item.href === "/dashboard" ? pathname === item.href : pathname === item.href || pathname.startsWith(item.href + "/");

                      const linkContent = (
                        <>
                          <Icon className={cn("h-4 w-4 shrink-0", isCollapsed && "mx-auto")} />
                          {<span className={cn("min-w-0 overflow-hidden whitespace-nowrap transition-[max-width,opacity,transform] duration-150 ease-out", isCollapsed ? "max-w-0 translate-x-1 opacity-0" : "max-w-[12rem] translate-x-0 opacity-100")}>{item.name}</span>}
                        </>
                      );

                      const link = (
                        <Link
                          href={item.href}
                          className={cn(
                            "flex items-center gap-3 rounded-lg px-3 py-2.5 text-sm font-medium transition-[padding,gap,background-color,color] duration-150 ease-out",
                            isActive
                              ? "bg-sidebar-accent text-sidebar-accent-foreground"
                              : "text-sidebar-foreground/80 hover:bg-sidebar-accent hover:text-sidebar-accent-foreground",
                            isCollapsed && "justify-center gap-0 px-2"
                          )}
                        >
                          {linkContent}
                        </Link>
                      );

                      return isCollapsed ? (
                        <Tooltip key={item.href}>
                          <TooltipTrigger asChild>{link}</TooltipTrigger>
                          <TooltipContent side="right">{item.name}</TooltipContent>
                        </Tooltip>
                      ) : (
                        <div key={item.href}>{link}</div>
                      );
                    })}
                  </div>
                </CollapsibleContent>
              </Collapsible>
            ))}
          </div>
        </div>

        <Separator className="opacity-50" />

        {/* User section */}
        <div className="shrink-0 p-2">
          <div
            className={cn(
              "flex items-center gap-2 rounded-md p-2 transition-colors hover:bg-sidebar-accent",
              isCollapsed && "justify-center px-0"
            )}
          >
            {isCollapsed ? (
              <Tooltip>
                <TooltipTrigger asChild>
                  <Avatar className="h-8 w-8">
                    <AvatarImage src={user?.avatarUrl ?? undefined} alt={user?.firstName} />
                    <AvatarFallback className="text-xs">
                      {user ? getInitials(`${user.firstName} ${user.lastName}`) : "?"}
                    </AvatarFallback>
                  </Avatar>
                </TooltipTrigger>
                <TooltipContent side="right">
                  {user ? `${user.firstName} ${user.lastName}` : "Usuario"}
                </TooltipContent>
              </Tooltip>
            ) : (
              <>
                <Avatar className="h-8 w-8 shrink-0">
                  <AvatarImage src={user?.avatarUrl ?? undefined} alt={user?.firstName} />
                  <AvatarFallback className="text-xs">
                    {user ? getInitials(`${user.firstName} ${user.lastName}`) : "?"}
                  </AvatarFallback>
                </Avatar>
                <div className="min-w-0 flex-1">
                  <p className="truncate text-sm font-medium text-sidebar-foreground">
                    {user ? `${user.firstName} ${user.lastName}` : "Usuario"}
                  </p>
                  <p className="truncate text-xs text-sidebar-foreground/70">
                    {user?.email ?? "—"}
                  </p>
                </div>
              </>
            )}
          </div>
        </div>

        {/* Collapse / Expand button */}
        <div className="absolute -right-3 top-16 z-10">
          <Tooltip>
            <TooltipTrigger asChild>
              <Button
                variant="outline"
                size="icon"
                className="h-7 w-7 rounded-full border-sidebar-border bg-sidebar-background shadow-md transition-transform duration-200 hover:scale-110 hover:bg-sidebar-accent"
                onClick={() => setCollapsed(!isCollapsed)}
              >
                {isCollapsed ? (
                  <ChevronsRight className="h-4 w-4 animate-in fade-in zoom-in-75 duration-200" />
                ) : (
                  <ChevronsLeft className="h-4 w-4 animate-in fade-in zoom-in-75 duration-200" />
                )}
              </Button>
            </TooltipTrigger>
            <TooltipContent side="right">
              {isCollapsed ? "Expandir barra lateral" : "Colapsar barra lateral"}
            </TooltipContent>
          </Tooltip>
        </div>
      </TooltipProvider>
    </aside>
  );
}
