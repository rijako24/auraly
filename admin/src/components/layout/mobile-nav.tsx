"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { ChevronRight } from "lucide-react";

import { AuralyLogo } from "@/components/brand/auraly-logo";
import { Avatar, AvatarFallback, AvatarImage } from "@/components/ui/avatar";
import { Collapsible, CollapsibleContent, CollapsibleTrigger } from "@/components/ui/collapsible";
import { ScrollArea } from "@/components/ui/scroll-area";
import {
  Sheet,
  SheetContent,
  SheetHeader,
  SheetTitle,
} from "@/components/ui/sheet";
import { Separator } from "@/components/ui/separator";
import { cn } from "@/lib/utils";
import { getInitials } from "@/lib/utils";
import { useAuthStore } from "@/stores/auth-store";
import { useSidebarStore } from "@/stores/sidebar-store";
import { useMediaQuery } from "@/hooks/use-media-query";

import { UserMenu } from "./user-menu";
import { authorizedNavigationGroups } from "./sidebar-nav-config";

export function MobileNav() {
  const pathname = usePathname();
  const isMobile = useMediaQuery("(max-width: 1024px)");
  const { isOpen, setOpen } = useSidebarStore();
  const { user } = useAuthStore();

  const handleClose = () => setOpen(false);

  const filteredGroups = authorizedNavigationGroups(user?.permissions ?? []);

  return (
    <Sheet
      open={isMobile && isOpen}
      onOpenChange={(open) => isMobile && setOpen(open ?? false)}
    >
      <SheetContent
        side="left"
        className="w-72 p-0 flex flex-col"
        showClose={true}
      >
        <SheetHeader className="border-b border-border px-4 py-3">
          <SheetTitle asChild>
            <Link
              href="/dashboard"
              onClick={handleClose}
              className="flex items-center gap-2 text-lg font-semibold"
            >
              <AuralyLogo className="[&>span]:text-foreground" />
            </Link>
          </SheetTitle>
        </SheetHeader>

        <ScrollArea className="flex-1 py-3">
          <nav className="px-3 space-y-4">
            {filteredGroups.map((group) => (
              <Collapsible key={group.label} defaultOpen className="group">
                <CollapsibleTrigger className="mb-2 flex w-full items-center gap-2 rounded-md px-2 py-1.5 text-left text-xs font-semibold uppercase tracking-[0.08em] text-muted-foreground transition-colors hover:bg-accent hover:text-foreground">
                  <ChevronRight className="h-3.5 w-3.5 transition-transform group-data-[state=open]:rotate-90" />
                  {group.label}
                </CollapsibleTrigger>
                <CollapsibleContent className="space-y-0.5 pl-4 pr-1">
                  {group.items.map((item) => {
                    const Icon = item.icon;
                    const isActive =
                      pathname === item.href ||
                      pathname.startsWith(item.href + "/");

                    return (
                      <Link
                        key={item.href}
                        href={item.href}
                        onClick={handleClose}
                        className={cn(
                          "flex items-center gap-2.5 rounded-md px-2 py-2 text-sm font-medium transition-colors",
                          isActive
                            ? "bg-accent text-accent-foreground"
                            : "text-foreground/80 hover:bg-accent hover:text-accent-foreground"
                        )}
                      >
                        <Icon className="h-4 w-4 shrink-0" />
                        <span>{item.name}</span>
                      </Link>
                    );
                  })}
                </CollapsibleContent>
              </Collapsible>
            ))}
          </nav>
        </ScrollArea>

        <Separator />
        <div className="p-3 flex items-center gap-3">
          <Avatar className="h-9 w-9 shrink-0">
            <AvatarImage src={user?.avatarUrl ?? undefined} alt={user?.firstName} />
            <AvatarFallback className="text-xs">
              {user ? getInitials(`${user.firstName} ${user.lastName}`) : "?"}
            </AvatarFallback>
          </Avatar>
          <div className="flex-1 min-w-0">
            <p className="text-sm font-medium truncate">
              {user ? `${user.firstName} ${user.lastName}` : "Usuario"}
            </p>
            <p className="text-xs text-muted-foreground truncate">
              {user?.email ?? "—"}
            </p>
          </div>
          <UserMenu />
        </div>
      </SheetContent>
    </Sheet>
  );
}
