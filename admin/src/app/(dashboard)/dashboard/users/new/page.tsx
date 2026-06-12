"use client";

import { useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { ArrowLeft } from "lucide-react";
import { toast } from "sonner";

import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Checkbox } from "@/components/ui/checkbox";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { PageError } from "@/components/ui/page-error";
import { PageLoading } from "@/components/ui/page-loading";
import { Switch } from "@/components/ui/switch";
import { useRoles } from "@/hooks/use-roles";
import { usersApi } from "@/services/api";

export default function NewUserPage() {
  const router = useRouter();
  const { data: rolesData, isLoading, isError, refetch } = useRoles({ page: 1, pageSize: 100 });
  const [form, setForm] = useState({
    firstName: "",
    lastName: "",
    email: "",
    username: "",
    password: "",
    phoneNumber: "",
    isActive: true,
  });
  const [selectedRoles, setSelectedRoles] = useState<Set<string>>(new Set());
  const [isSubmitting, setIsSubmitting] = useState(false);
  const roles = rolesData?.items ?? [];

  const updateForm = (key: keyof typeof form, value: string | boolean) =>
    setForm((prev) => ({ ...prev, [key]: value }));

  const toggleRole = (roleId: string) => {
    setSelectedRoles((prev) => {
      const next = new Set(prev);
      next.has(roleId) ? next.delete(roleId) : next.add(roleId);
      return next;
    });
  };

  const handleSubmit = async (event: React.FormEvent) => {
    event.preventDefault();
    setIsSubmitting(true);
    try {
      const created = await usersApi.create(form as unknown as Parameters<typeof usersApi.create>[0]);
      await Promise.all(
        Array.from(selectedRoles).map((roleId) =>
          usersApi.assignRole(created.userId, { roleId })
        )
      );
      toast.success("Usuario creado");
      router.push("/dashboard/users");
    } catch {
      toast.error("No se pudo crear el usuario");
    } finally {
      setIsSubmitting(false);
    }
  };

  if (isLoading) return <PageLoading cards={1} />;
  if (isError) return <PageError onRetry={refetch} />;

  return (
    <div className="space-y-6">
      <div className="flex items-center gap-4">
        <Button variant="ghost" size="icon" asChild>
          <Link href="/dashboard/users"><ArrowLeft className="h-4 w-4" /></Link>
        </Button>
        <div className="flex-1">
          <h1 className="text-2xl font-semibold tracking-tight">Nuevo Usuario</h1>
          <p className="text-muted-foreground">Crea un nuevo usuario en la plataforma</p>
        </div>
      </div>

      <form onSubmit={handleSubmit} className="space-y-6">
        <Card>
          <CardHeader><CardTitle>Datos del usuario</CardTitle></CardHeader>
          <CardContent className="space-y-4">
            <div className="grid gap-4 sm:grid-cols-2">
              <div className="space-y-2"><Label htmlFor="firstName">Nombre</Label><Input id="firstName" value={form.firstName} onChange={(e) => updateForm("firstName", e.target.value)} required /></div>
              <div className="space-y-2"><Label htmlFor="lastName">Apellido</Label><Input id="lastName" value={form.lastName} onChange={(e) => updateForm("lastName", e.target.value)} required /></div>
            </div>
            <div className="space-y-2"><Label htmlFor="email">Email</Label><Input id="email" type="email" value={form.email} onChange={(e) => updateForm("email", e.target.value)} required /></div>
            <div className="space-y-2"><Label htmlFor="username">Username</Label><Input id="username" value={form.username} onChange={(e) => updateForm("username", e.target.value)} required /></div>
            <div className="space-y-2"><Label htmlFor="password">Contrasena</Label><Input id="password" type="password" value={form.password} onChange={(e) => updateForm("password", e.target.value)} required /></div>
            <div className="space-y-2"><Label htmlFor="phoneNumber">Telefono</Label><Input id="phoneNumber" value={form.phoneNumber} onChange={(e) => updateForm("phoneNumber", e.target.value)} /></div>
            <div className="flex items-center space-x-2"><Switch id="isActive" checked={form.isActive} onCheckedChange={(checked) => updateForm("isActive", checked)} /><Label htmlFor="isActive">Usuario activo</Label></div>
          </CardContent>
        </Card>

        <Card>
          <CardHeader><CardTitle>Roles</CardTitle></CardHeader>
          <CardContent>
            <div className="space-y-3">
              {roles.map((role) => (
                <div key={role.roleId} className="flex items-center space-x-2">
                  <Checkbox id={role.roleId} checked={selectedRoles.has(role.roleId)} onCheckedChange={() => toggleRole(role.roleId)} />
                  <Label htmlFor={role.roleId} className="cursor-pointer font-normal">{role.name}{role.description ? ` - ${role.description}` : ""}</Label>
                </div>
              ))}
              {roles.length === 0 && <p className="text-sm text-muted-foreground">No hay roles disponibles.</p>}
            </div>
          </CardContent>
        </Card>

        <div className="flex gap-2">
          <Button type="submit" disabled={isSubmitting}>{isSubmitting ? "Creando..." : "Crear Usuario"}</Button>
          <Button variant="outline" asChild><Link href="/dashboard/users">Cancelar</Link></Button>
        </div>
      </form>
    </div>
  );
}
