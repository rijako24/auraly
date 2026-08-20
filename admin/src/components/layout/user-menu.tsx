"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useQueryClient } from "@tanstack/react-query";
import { Building2, LogOut, Settings, User } from "lucide-react";

import { Avatar, AvatarFallback, AvatarImage } from "@/components/ui/avatar";
import { Button } from "@/components/ui/button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuRadioGroup,
  DropdownMenuRadioItem,
  DropdownMenuSeparator,
  DropdownMenuSub,
  DropdownMenuSubContent,
  DropdownMenuSubTrigger,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { cn, getInitials } from "@/lib/utils";
import { useAuthStore } from "@/stores/auth-store";
import { useBusinessContextStore } from "@/stores/business-context-store";
import { useTenantContextStore } from "@/stores/tenant-context-store";

interface UserMenuProps {
  className?: string;
}

export function UserMenu({ className }: UserMenuProps) {
  const router = useRouter();
  const queryClient = useQueryClient();
  const { user, logout: authLogout, setExecutionAccess } = useAuthStore();
  const resetBusinessContext = useBusinessContextStore((state) => state.reset);
  const clearBusinessForTenant = useBusinessContextStore(
    (state) => state.clearForTenantChange,
  );
  const tenants = useTenantContextStore((state) => state.tenants);
  const selectedTenantId = useTenantContextStore((state) => state.selectedTenantId);
  const selectTenant = useTenantContextStore((state) => state.selectTenant);
  const resetTenantSession = useTenantContextStore((state) => state.resetSession);
  const selectedTenant = tenants.find((tenant) => tenant.tenantId === selectedTenantId);

  const logout = async () => {
    resetBusinessContext();
    resetTenantSession();
    await authLogout().catch(() => undefined);
    router.replace("/login");
    router.refresh();
  };

  const changeTenant = (tenantId: string) => {
    if (tenantId === selectedTenantId) return;
    clearBusinessForTenant();
    setExecutionAccess([], []);
    selectTenant(tenantId);
    queryClient.removeQueries({
      predicate: (query) => query.queryKey[0] !== "execution-context",
    });
  };

  const displayName = user
    ? `${user.firstName} ${user.lastName}`.trim() || "Usuario"
    : "Usuario";
  const initials = user ? getInitials(displayName) : "?";

  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button
          variant="ghost"
          className={cn(
            "relative h-9 w-9 rounded-full focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2",
            className,
          )}
          aria-label="Menú de usuario"
        >
          <Avatar className="h-8 w-8">
            <AvatarImage src={user?.avatarUrl ?? undefined} alt={displayName} />
            <AvatarFallback className="bg-muted text-xs">{initials}</AvatarFallback>
          </Avatar>
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent className="w-64" align="end" forceMount>
        <DropdownMenuLabel className="font-normal">
          <div className="flex flex-col space-y-1">
            <p className="text-sm font-medium leading-none">{displayName}</p>
            <p className="text-xs leading-none text-muted-foreground">
              {user?.email ?? "—"}
            </p>
          </div>
        </DropdownMenuLabel>

        {tenants.length > 0 && (
          <>
            <DropdownMenuSeparator />
            {tenants.length === 1 ? (
              <div className="flex items-center gap-2 px-2 py-1.5 text-sm">
                <Building2 className="h-4 w-4 text-muted-foreground" />
                <div className="min-w-0">
                  <p className="text-xs text-muted-foreground">Organización</p>
                  <p className="truncate font-medium">{tenants[0].name}</p>
                </div>
              </div>
            ) : (
              <DropdownMenuSub>
                <DropdownMenuSubTrigger className="gap-2">
                  <Building2 className="h-4 w-4" />
                  <div className="min-w-0 flex-1 text-left">
                    <p className="text-xs text-muted-foreground">Organización</p>
                    <p className="truncate">{selectedTenant?.name ?? "Seleccionar"}</p>
                  </div>
                </DropdownMenuSubTrigger>
                <DropdownMenuSubContent className="w-64">
                  <DropdownMenuLabel>Cambiar organización</DropdownMenuLabel>
                  <DropdownMenuRadioGroup
                    value={selectedTenantId ?? ""}
                    onValueChange={changeTenant}
                  >
                    {tenants.map((tenant) => (
                      <DropdownMenuRadioItem
                        key={tenant.tenantId}
                        value={tenant.tenantId}
                        className="gap-2"
                      >
                        <span className="truncate">{tenant.name}</span>

                      </DropdownMenuRadioItem>
                    ))}
                  </DropdownMenuRadioGroup>
                </DropdownMenuSubContent>
              </DropdownMenuSub>
            )}
          </>
        )}

        <DropdownMenuSeparator />
        <DropdownMenuItem asChild>
          <Link href="/dashboard/profile" className="flex cursor-pointer items-center gap-2">
            <User className="h-4 w-4" />
            <span>Perfil</span>
          </Link>
        </DropdownMenuItem>
        <DropdownMenuItem asChild>
          <Link href="/dashboard/settings" className="flex cursor-pointer items-center gap-2">
            <Settings className="h-4 w-4" />
            <span>Configuración</span>
          </Link>
        </DropdownMenuItem>
        <DropdownMenuSeparator />
        <DropdownMenuItem
          onClick={logout}
          className="cursor-pointer text-destructive focus:text-destructive"
        >
          <LogOut className="h-4 w-4" />
          <span>Cerrar sesión</span>
        </DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
