"use client";

import { useMemo, useState } from "react";
import Link from "next/link";
import { useParams } from "next/navigation";
import { ArrowLeft, Save } from "lucide-react";

import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Checkbox } from "@/components/ui/checkbox";
import type { AppRole, Permission } from "@/types/entities";
import { cn } from "@/lib/utils";

const MODULES = [
  "Users",
  "Roles",
  "Businesses",
  "Services",
  "Reservations",
  "Conversations",
  "Leads",
  "Payments",
  "Settings",
] as const;

const ACTIONS = ["read", "create", "update", "delete"] as const;

const MOCK_PERMISSIONS: Permission[] = MODULES.flatMap((module) =>
  ACTIONS.map((action, idx) => ({
    permissionId: `perm-${module}-${action}`,
    module,
    action,
    resource: module.toLowerCase(),
    description: `${action} en ${module}`,
    createdAt: "2025-01-01T00:00:00Z",
  }))
);

const MOCK_ROLE: AppRole & { permissions?: Permission[] } = {
  roleId: "role-1",
  tenantId: null,
  name: "Admin",
  description: "Acceso completo al sistema. Puede gestionar usuarios, roles y configuración.",
  isSystemRole: true,
  isActive: true,
  createdAt: "2025-01-01T00:00:00Z",
  updatedAt: null,
  permissions: MOCK_PERMISSIONS.filter((p) =>
    ["Users", "Roles", "Businesses", "Settings"].includes(p.module)
  ),
};

export default function RoleDetailPage() {
  const params = useParams();
  const id = (params as { id: string }).id;
  const [role] = useState(() => ({ ...MOCK_ROLE, roleId: id }));
  const [permissions, setPermissions] = useState<Set<string>>(() => {
    const set = new Set<string>();
    MOCK_ROLE.permissions?.forEach((p) =>
      set.add(`${p.module}:${p.action}`)
    );
    return set;
  });

  const handleToggle = (module: string, action: string) => {
    const key = `${module}:${action}`;
    setPermissions((prev) => {
      const next = new Set(prev);
      if (next.has(key)) next.delete(key);
      else next.add(key);
      return next;
    });
  };

  const handleSave = () => {
    // Mock save
  };

  const groupedPermissions = useMemo(() => {
    const map = new Map<string, { action: string; permissionId: string }[]>();
    MOCK_PERMISSIONS.forEach((p) => {
      const list = map.get(p.module) ?? [];
      list.push({ action: p.action, permissionId: p.permissionId });
      map.set(p.module, list);
    });
    return map;
  }, []);

  return (
    <div className="space-y-6">
      <div className="flex items-center gap-4">
        <Button variant="ghost" size="icon" asChild>
          <Link href="/dashboard/roles">
            <ArrowLeft className="h-4 w-4" />
          </Link>
        </Button>
        <div className="flex-1">
          <h1 className="text-2xl font-semibold tracking-tight">{role.name}</h1>
          <p className="text-muted-foreground">{role.description ?? "Sin descripción"}</p>
        </div>
        <Button onClick={handleSave}>
          <Save className="mr-2 h-4 w-4" />
          Guardar cambios
        </Button>
      </div>

      <Card>
        <CardHeader>
          <CardTitle>Matriz de permisos</CardTitle>
          <p className="text-sm text-muted-foreground">
            Selecciona los permisos para este rol
          </p>
        </CardHeader>
        <CardContent className="space-y-6">
          {Array.from(groupedPermissions.entries()).map(([module, actions]) => (
            <div key={module} className="space-y-2">
              <h3 className="font-medium">{module}</h3>
              <div className="grid grid-cols-2 gap-2 sm:grid-cols-4">
                {actions.map(({ action, permissionId }) => {
                  const key = `${module}:${action}`;
                  const checked = permissions.has(key);
                  return (
                    <div
                      key={permissionId}
                      className={cn(
                        "flex items-center space-x-2 rounded-md border p-3",
                        checked && "border-primary bg-primary/5"
                      )}
                    >
                      <Checkbox
                        id={key}
                        checked={checked}
                        onCheckedChange={() => handleToggle(module, action)}
                      />
                      <label
                        htmlFor={key}
                        className="cursor-pointer text-sm capitalize"
                      >
                        {action}
                      </label>
                    </div>
                  );
                })}
              </div>
            </div>
          ))}
        </CardContent>
      </Card>
    </div>
  );
}
