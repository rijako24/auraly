"use client";

import { useState } from "react";
import Link from "next/link";
import { ArrowLeft } from "lucide-react";

import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Switch } from "@/components/ui/switch";
import { Textarea } from "@/components/ui/textarea";
import { useSearchParams } from "next/navigation";
import { RolePermissionWorkspace } from "@/components/roles/role-permission-workspace";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { useRouter } from "next/navigation";

export default function NewRolePage() {
  const cloneFromId = useSearchParams().get("clone") ?? undefined;
  return <RolePermissionWorkspace cloneFromId={cloneFromId} />;
}

function LegacyNewRolePage() {
  const router = useRouter();
  const [name, setName] = useState("");
  const [description, setDescription] = useState("");
  const [isSystemRole, setIsSystemRole] = useState(false);
  const [isActive, setIsActive] = useState(true);

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    router.push("/dashboard/roles");
  };

  return (
    <div className="space-y-6">
      <div className="flex items-center gap-4">
        <Button variant="ghost" size="icon" asChild>
          <Link href="/dashboard/roles">
            <ArrowLeft className="h-4 w-4" />
          </Link>
        </Button>
        <div className="flex-1">
          <h1 className="text-2xl font-semibold tracking-tight">
            Nuevo Rol
          </h1>
          <p className="text-muted-foreground">
            Crea un nuevo rol con sus permisos
          </p>
        </div>
      </div>

      <form onSubmit={handleSubmit}>
        <Card>
          <CardHeader>
            <CardTitle>Datos del rol</CardTitle>
          </CardHeader>
          <CardContent className="space-y-4">
            <div className="space-y-2">
              <Label htmlFor="name">Nombre</Label>
              <Input
                id="name"
                value={name}
                onChange={(e) => setName(e.target.value)}
                placeholder="Ej: Manager"
                required
              />
            </div>
            <div className="space-y-2">
              <Label htmlFor="description">Descripción</Label>
              <Textarea
                id="description"
                value={description}
                onChange={(e) => setDescription(e.target.value)}
                placeholder="Describe las responsabilidades de este rol"
                rows={3}
              />
            </div>
            <div className="flex items-center space-x-2">
              <Switch
                id="isSystemRole"
                checked={isSystemRole}
                onCheckedChange={setIsSystemRole}
              />
              <Label htmlFor="isSystemRole">Rol de sistema</Label>
            </div>
            <div className="flex items-center space-x-2">
              <Switch
                id="isActive"
                checked={isActive}
                onCheckedChange={setIsActive}
              />
              <Label htmlFor="isActive">Activo</Label>
            </div>
          </CardContent>
        </Card>
        <div className="mt-6 flex gap-2">
          <Button type="submit">Crear Rol</Button>
          <Button variant="outline" asChild>
            <Link href="/dashboard/roles">Cancelar</Link>
          </Button>
        </div>
      </form>
    </div>
  );
}
