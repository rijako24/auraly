"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import {
  ChevronRight,
  MessageCircle,
  PanelLeftClose,
  PanelLeftOpen,
} from "lucide-react";

import { Avatar, AvatarFallback, AvatarImage } from "@/components/ui/avatar";
import { Button } from "@/components/ui/button";
import { Collapsible, CollapsibleContent, CollapsibleTrigger } from "@/components/ui/collapsible";
import { ScrollArea } from "@/components/ui/scroll-area";
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
        "relative flex h-screen flex-col border-r border-sidebar-border bg-sidebar-background transition-all duration-300 ease-in-out",
        isCollapsed ? "w-[52px]" : "w-56"
      )}
    >
      <TooltipProvider delayDuration={0}>
        {/* Logo / Brand */}
        <div className="flex h-14 shrink-0 items-center border-b border-sidebar-border px-3">
          {isCollapsed ? (
            <Tooltip>
              <TooltipTrigger asChild>
                <Link
                  href="/dashboard"
                  className="flex items-center justify-center w-full rounded-md hover:bg-sidebar-accent transition-colors"
                >
                  <MessageCircle className="h-6 w-6 text-sidebar-primary shrink-0" />
                </Link>
              </TooltipTrigger>
              <TooltipContent side="right">Quantix AI Admin</TooltipContent>
            </Tooltip>
          ) : (
            <Link
              href="/dashboard"
              className="flex items-center gap-2 rounded-md px-2 py-1.5 hover:bg-sidebar-accent transition-colors"
            >
              <MessageCircle className="h-6 w-6 text-sidebar-primary shrink-0" />
              <span className="font-semibold text-sidebar-primary truncate">Quantix AI</span>
            </Link>
          )}
        </div>

        {/* Navigation */}
        <ScrollArea className="flex-1 py-3">
          <div className="space-y-1 px-2">
            {filteredGroups.map((group) => (
              <Collapsible key={group.label} defaultOpen className="group">
                <CollapsibleTrigger asChild>
                  {isCollapsed ? (
                    <div className="flex items-center justify-center w-9 h-9 rounded-md" />
                  ) : (
                    <button className="flex w-full items-center gap-2 rounded-md px-2 py-1.5 text-xs font-medium text-sidebar-foreground/70 hover:text-sidebar-foreground hover:bg-sidebar-accent transition-colors">
                      <ChevronRight className="h-3.5 w-3.5 transition-transform group-data-[state=open]:rotate-90" />
                      {group.label}
                    </button>
                  )}
                </CollapsibleTrigger>
                <CollapsibleContent>
                  <div className="mt-1 space-y-0.5">
                    {group.items.map((item) => {
                      if (!("href" in item)) return null;
                      const Icon = item.icon;
                      const isActive = pathname === item.href || pathname.startsWith(item.href + "/");

                      const linkContent = (
                        <>
                          <Icon className={cn("h-4 w-4 shrink-0", isCollapsed && "mx-auto")} />
                          {!isCollapsed && <span className="truncate">{item.name}</span>}
                        </>
                      );

                      const link = (
                        <Link
                          href={item.href}
                          className={cn(
                            "flex items-center gap-2.5 rounded-md px-2 py-2 text-sm font-medium transition-colors",
                            isActive
                              ? "bg-sidebar-accent text-sidebar-accent-foreground"
                              : "text-sidebar-foreground/80 hover:bg-sidebar-accent hover:text-sidebar-accent-foreground",
                            isCollapsed && "justify-center px-2"
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
        </ScrollArea>

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
                className="h-6 w-6 rounded-full border-sidebar-border bg-sidebar-background shadow-sm hover:bg-sidebar-accent"
                onClick={() => setCollapsed(!isCollapsed)}
              >
                {isCollapsed ? (
                  <PanelLeftOpen className="h-3 w-3" />
                ) : (
                  <PanelLeftClose className="h-3 w-3" />
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
