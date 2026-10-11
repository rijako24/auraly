"use client";

import { useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { restaurantAccountApi } from "@/services/api/restaurant-account";
import type { PartyWorkspaceDetail } from "@/services/api/parties";
import { useAuthStore } from "@/stores/auth-store";

export function WaiterRolePanel({ detail, editing, canConfigure, shortName, onShortNameChange }: {
  detail: PartyWorkspaceDetail;
  editing: boolean;
  canConfigure: boolean;
  shortName: string;
  onShortNameChange: (value: string) => void;
}) {
  const waiter = detail.waiter;
  const userId = detail.user?.userId;
  const canManageSecret = useAuthStore((state) => state.user?.permissions.includes("pos.approvals.manage_credential") ?? false);
  const queryClient = useQueryClient();
  const [secret, setSecret] = useState("");
  const [busy, setBusy] = useState(false);
  const credential = useQuery({
    queryKey: ["restaurant-waiter-secondary", userId],
    queryFn: () => restaurantAccountApi.secondaryStatus(userId!),
    enabled: !!userId && canManageSecret,
  });
  async function changeCredential(revoke: boolean) {
    if (!userId || busy) return;
    setBusy(true);
    try {
      if (revoke) await restaurantAccountApi.revokeSecondary(userId);
      else await restaurantAccountApi.setSecondary(userId, secret);
      setSecret("");
      await queryClient.invalidateQueries({ queryKey: ["restaurant-waiter-secondary", userId] });
      toast.success(revoke ? "Clave secundaria retirada." : "Clave secundaria guardada.");
    } catch (error) {
      toast.error(error instanceof Error ? error.message : "No se pudo actualizar la clave.");
    } finally { setBusy(false); }
  }
  if (!waiter) return null;
  const status = !waiter.isActive ? "Mesero inactivo" : !detail.user ? "Sin usuario vinculado" :
    !detail.user.isActive ? "Usuario inactivo" :
    !detail.user.restaurantAttentionAllowed ? "Falta permiso de atención" : "Listo para atender con su usuario";

  return <section className="overflow-hidden rounded-2xl border bg-card shadow-sm">
    <div className="border-b bg-muted/20 px-4 py-4 sm:px-5">
      <h3 className="font-semibold">Atención de mesas</h3>
      <p className="mt-1 text-sm text-muted-foreground">El mesero pertenece a la empresa y comparte la misma ficha en todas sus sedes.</p>
    </div>
    <div className="space-y-5 p-4 sm:p-5">
      <Badge variant="outline">{status}</Badge>
      <div className="grid gap-4 sm:grid-cols-2">
        <div><p className="text-xs font-medium text-muted-foreground">Código</p><p className="mt-1 font-medium">{waiter.code}</p></div>
        <label className="space-y-2"><span className="block text-sm font-medium">Nombre corto (opcional)</span>
          {editing && canConfigure ? <Input maxLength={80} value={shortName} onChange={event => onShortNameChange(event.target.value)} /> :
            <span className="block font-medium">{waiter.shortName ?? "Sin nombre corto"}</span>}
        </label>
      </div>
      <p className="text-sm text-muted-foreground">Vincula el usuario manualmente y asígnale el rol de atención. En un puesto compartido la sede decide si pide «Tu clave»; cuando la pide, el mesero puede usar su clave principal o la secundaria de atención.</p>
      {userId && canManageSecret && <div className="rounded-2xl border bg-muted/20 p-4">
        <p className="font-semibold">Clave secundaria de atención</p>
        <p className="mt-1 text-sm text-muted-foreground">Cuatro dígitos para identificar al mesero rápidamente en una caja compartida. No reemplaza su contraseña de ingreso.</p>
        <p className="mt-2 text-xs text-muted-foreground">{credential.isLoading ? "Consultando…" : credential.data?.configured ? "Configurada" : "Sin configurar"}</p>
        <div className="mt-3 flex flex-wrap gap-2"><Input className="h-12 w-44 text-center text-lg tracking-widest" type="password" inputMode="numeric" autoComplete="new-password" maxLength={4} value={secret} onChange={(event) => setSecret(event.target.value.replace(/\D/g, "").slice(0, 4))} placeholder="4 dígitos" aria-label="Clave secundaria de atención" />
          <Button className="min-h-12" disabled={busy || !/^\d{4}$/.test(secret)} onClick={() => void changeCredential(false)}>{credential.data?.configured ? "Cambiar clave" : "Crear clave"}</Button>
          {credential.data?.configured && <Button className="min-h-12" variant="outline" disabled={busy} onClick={() => void changeCredential(true)}>Retirar clave</Button>}</div>
      </div>}
    </div>
  </section>;
}
