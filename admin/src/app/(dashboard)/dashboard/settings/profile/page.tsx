"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { ArrowLeft } from "lucide-react";
import { toast } from "sonner";

import { Avatar, AvatarFallback } from "@/components/ui/avatar";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { getInitials } from "@/lib/utils";
import { usersApi } from "@/services/api";
import { useAuthStore } from "@/stores/auth-store";

export default function ProfileSettingsPage() {
  const authUser = useAuthStore((state) => state.user);
  const [form, setForm] = useState({
    firstName: "",
    lastName: "",
    email: "",
    phoneNumber: "",
  });
  const [isSubmitting, setIsSubmitting] = useState(false);

  useEffect(() => {
    if (!authUser) return;
    setForm({
      firstName: authUser.firstName,
      lastName: authUser.lastName,
      email: authUser.email,
      phoneNumber: "",
    });
  }, [authUser]);

  const fullName = `${form.firstName} ${form.lastName}`.trim();
  const updateForm = (key: keyof typeof form, value: string) =>
    setForm((prev) => ({ ...prev, [key]: value }));

  const handleSaveProfile = async (event: React.FormEvent) => {
    event.preventDefault();
    if (!authUser) return;
    setIsSubmitting(true);
    try {
      await usersApi.update(authUser.userId, form);
      toast.success("Perfil actualizado");
    } catch {
      toast.error("No se pudo actualizar el perfil");
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <div className="space-y-6">
      <div className="flex items-center gap-4">
        <Button variant="ghost" size="icon" asChild>
          <Link href="/dashboard/settings"><ArrowLeft className="h-4 w-4" /></Link>
        </Button>
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">Perfil</h1>
          <p className="text-muted-foreground">Tu informacion personal</p>
        </div>
      </div>

      {!authUser ? (
        <Card><CardContent className="py-8 text-sm text-muted-foreground">No hay usuario autenticado.</CardContent></Card>
      ) : (
        <form onSubmit={handleSaveProfile}>
          <Card>
            <CardHeader>
              <CardTitle>Informacion personal</CardTitle>
            </CardHeader>
            <CardContent className="space-y-6">
              <div className="flex items-center gap-6">
                <Avatar className="h-24 w-24">
                  <AvatarFallback className="text-2xl">
                    {getInitials(fullName || authUser.username)}
                  </AvatarFallback>
                </Avatar>
                <div>
                  <p className="font-medium">{fullName || authUser.username}</p>
                  <p className="text-sm text-muted-foreground">{form.email}</p>
                </div>
              </div>

              <div className="grid gap-4 sm:grid-cols-2">
                <div className="space-y-2"><Label htmlFor="firstName">Nombre</Label><Input id="firstName" value={form.firstName} onChange={(e) => updateForm("firstName", e.target.value)} /></div>
                <div className="space-y-2"><Label htmlFor="lastName">Apellido</Label><Input id="lastName" value={form.lastName} onChange={(e) => updateForm("lastName", e.target.value)} /></div>
                <div className="space-y-2 sm:col-span-2"><Label htmlFor="email">Email</Label><Input id="email" type="email" value={form.email} onChange={(e) => updateForm("email", e.target.value)} /></div>
                <div className="space-y-2 sm:col-span-2"><Label htmlFor="phone">Telefono</Label><Input id="phone" value={form.phoneNumber} onChange={(e) => updateForm("phoneNumber", e.target.value)} /></div>
              </div>

              <Button type="submit" disabled={isSubmitting}>
                {isSubmitting ? "Guardando..." : "Guardar cambios"}
              </Button>
            </CardContent>
          </Card>
        </form>
      )}
    </div>
  );
}
