"use client";

import { useState } from "react";
import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
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
  const searchParams = useSearchParams();
  const partyId = searchParams.get("partyId") ?? undefined;
  const partyNameParts = (searchParams.get("name")?.trim() ?? "").split(/\s+/).filter(Boolean);
  const firstName = partyNameParts.shift() ?? "";
  const { data: rolesData, isLoading, isError, refetch } = useRoles({ page: 1, pageSize: 100 });
  const [form, setForm] = useState({
    firstName,
    lastName: partyNameParts.join(" "),
    email: searchParams.get("email") ?? "",
    username: "",
    password: "",
    phoneNumber: searchParams.get("phone") ?? "",
    partyId,
    isActive: true,
  });
  const [selectedRoles, setSelectedRoles] = useState<Set<string>>(new Set());
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [errors, setErrors] = useState<Record<string, string>>({});
  const roles = rolesData?.items ?? [];

  const updateForm = (key: keyof typeof form, value: string | boolean) =>
    setForm((prev) => ({ ...prev, [key]: value }));

  const toggleRole = (roleId: string) => {
    setSelectedRoles((prev) => {
      const next = new Set(prev);
      if (next.has(roleId)) next.delete(roleId);
      else next.add(roleId);
      return next;
    });
  };

  const handleSubmit = async (event: React.FormEvent) => {
    event.preventDefault();
    const nextErrors: Record<string, string> = {};
    if (!form.firstName.trim()) nextErrors.firstName = "Este campo es requerido";
    if (!form.lastName.trim()) nextErrors.lastName = "Este campo es requerido";
    if (!form.email.trim()) nextErrors.email = "Este campo es requerido";
    if (!form.username.trim()) nextErrors.username = "Este campo es requerido";
    if (!form.password.trim()) nextErrors.password = "Este campo es requerido";
    if (selectedRoles.size === 0) nextErrors.roles = "Este campo es requerido";
    setErrors(nextErrors);
    if (Object.keys(nextErrors).length > 0) return;
    setIsSubmitting(true);
    try {
      const created = await usersApi.create(form as unknown as Parameters<typeof usersApi.create>[0]);
      await Promise.all(
        Array.from(selectedRoles).map((roleId) =>
          usersApi.assignRole(created.userId, { roleId })
        )
      );
      toast.success("Usuario creado");
      router.push("/dashboard/parties");
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
          <Link href="/dashboard/parties"><ArrowLeft className="h-4 w-4" /></Link>
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
              <UserField label="Nombre" error={errors.firstName}><Input id="firstName" aria-invalid={Boolean(errors.firstName)} className={errors.firstName ? "border-destructive" : ""} value={form.firstName} onChange={(e) => updateForm("firstName", e.target.value)} /></UserField>
              <UserField label="Apellido" error={errors.lastName}><Input id="lastName" aria-invalid={Boolean(errors.lastName)} className={errors.lastName ? "border-destructive" : ""} value={form.lastName} onChange={(e) => updateForm("lastName", e.target.value)} /></UserField>
            </div>
            <UserField label="Correo" error={errors.email}><Input id="email" type="email" aria-invalid={Boolean(errors.email)} className={errors.email ? "border-destructive" : ""} value={form.email} onChange={(e) => updateForm("email", e.target.value)} /></UserField>
            <UserField label="Usuario" error={errors.username}><Input id="username" aria-invalid={Boolean(errors.username)} className={errors.username ? "border-destructive" : ""} value={form.username} onChange={(e) => updateForm("username", e.target.value)} /></UserField>
            <UserField label="Contraseña de acceso y modo sin conexión POS" error={errors.password}><Input id="password" type="password" aria-invalid={Boolean(errors.password)} className={errors.password ? "border-destructive" : ""} value={form.password} onChange={(e) => updateForm("password", e.target.value)} /></UserField>
            <div className="space-y-2"><Label htmlFor="phoneNumber">Telefono</Label><Input id="phoneNumber" value={form.phoneNumber} onChange={(e) => updateForm("phoneNumber", e.target.value)} /></div>
            <div className="flex items-center space-x-2"><Switch id="isActive" checked={form.isActive} onCheckedChange={(checked) => updateForm("isActive", checked)} /><Label htmlFor="isActive">Usuario activo</Label></div>
          </CardContent>
        </Card>

        <Card>
          <CardHeader><CardTitle>Roles</CardTitle></CardHeader>
          <CardContent>
            <div className={`space-y-3 rounded-lg ${errors.roles ? "border border-destructive p-3" : ""}`}>
              {errors.roles && <p className="text-sm text-destructive">{errors.roles}</p>}
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
          <Button variant="outline" asChild><Link href="/dashboard/parties">Cancelar</Link></Button>
        </div>
      </form>
    </div>
  );
}

function UserField({ label, error, children }: { label: string; error?: string; children: React.ReactNode }) { return <div className="space-y-2"><Label>{label} <span className="text-destructive">*</span></Label>{children}{error && <p className="text-sm text-destructive">{error}</p>}</div>; }
