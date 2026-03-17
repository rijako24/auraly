"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import {
  BarChart3,
  Building2,
  Calendar,
  CalendarDays,
  CreditCard,
  FileSearch,
  LayoutDashboard,
  MessageSquare,
  Package,
  Search,
  Settings,
  Shield,
  Store,
  UserCog,
  UserPlus,
  Users,
} from "lucide-react";

import { Button } from "@/components/ui/button";

import {
  CommandDialog,
  CommandEmpty,
  CommandGroup,
  CommandInput,
  CommandItem,
  CommandList,
} from "@/components/ui/command";
import { navigation } from "./sidebar-nav-config";

const iconMap: Record<string, React.ComponentType<{ className?: string }>> = {
  LayoutDashboard,
  BarChart3,
  Package,
  Users,
  CalendarDays,
  Calendar,
  MessageSquare,
  UserPlus,
  CreditCard,
  Building2,
  Store,
  UserCog,
  Shield,
  FileSearch,
  Settings,
};

export function SearchCommand() {
  const [open, setOpen] = useState(false);
  const router = useRouter();

  useEffect(() => {
    const down = (e: KeyboardEvent) => {
      if (e.key === "k" && (e.metaKey || e.ctrlKey)) {
        e.preventDefault();
        setOpen((o) => !o);
      }
    };
    document.addEventListener("keydown", down);
    return () => document.removeEventListener("keydown", down);
  }, []);

  const navItems = navigation.filter((entry) => "href" in entry);

  const runCommand = (href: string) => {
    setOpen(false);
    router.push(href);
  };

  return (
    <>
      <Button
        variant="ghost"
        size="icon"
        onClick={() => setOpen(true)}
        className="h-9 w-9"
        aria-label="Buscar"
      >
        <Search className="h-4 w-4" />
      </Button>
      <CommandDialog open={open} onOpenChange={setOpen}>
      <CommandInput placeholder="Buscar páginas..." />
      <CommandList>
        <CommandEmpty>No se encontraron resultados.</CommandEmpty>
        <CommandGroup heading="Navegación">
          {navItems.map((item) => {
            const Icon = "icon" in item ? item.icon : null;
            return (
              <CommandItem
                key={"href" in item ? item.href : ""}
                value={item.name}
                onSelect={() => "href" in item && runCommand(item.href)}
              >
                {Icon && <Icon className="mr-2 h-4 w-4" />}
                {item.name}
              </CommandItem>
            );
          })}
        </CommandGroup>
      </CommandList>
    </CommandDialog>
    </>
  );
}
