"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { useParams, useSearchParams } from "next/navigation";
import { ArrowLeft, Copy, FileText, Fingerprint, Mail, Pencil } from "lucide-react";
import { toast } from "sonner";

import { TenantBrand } from "@/components/brand/tenant-brand";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { PageError } from "@/components/ui/page-error";
import { PageLoading } from "@/components/ui/page-loading";
import { TenantEditDialog } from "@/components/tenants/tenant-edit-dialog";
import { TenantGovernancePanel } from "@/components/tenants/tenant-governance-panel";
import { TenantProvisioningSummary } from "@/components/tenants/tenant-provisioning-summary";
import { PlatformTenantSubscriptionCard } from "@/components/tenants/platform-tenant-subscription-card";
import { useTenant } from "@/hooks/use-tenants";
import { useAuthStore } from "@/stores/auth-store";

export default function TenantDetailPage() {
  const id = useParams().id as string;
  const provisioned = useSearchParams().get("provisioned") === "1";
  const { data: tenant, isLoading, isError, refetch } = useTenant(id);
  const [loginUrl, setLoginUrl] = useState("");
  const [editing, setEditing] = useState(false);
  const canEdit = useAuthStore(state => state.user?.permissions.includes("tenants.update") ?? false);

  useEffect(() => {
    if (tenant?.tenantKey) setLoginUrl(`${window.location.origin}/login?tenant=${encodeURIComponent(tenant.tenantKey)}`);
  }, [tenant?.tenantKey]);

  if (isLoading) return <PageLoading cards={2} />;
  if (isError || !tenant) return <PageError onRetry={refetch} />;

  const entityLabel = tenant.entityType === "NaturalPerson" ? "Persona natural" : "Persona jurídica";
  const identification = tenant.nit
    ? `${tenant.identificationTypeCode ?? "NIT"} ${tenant.nit}${tenant.identificationTypeCode !== "CC" && tenant.verificationDigit ? `-${tenant.verificationDigit}` : ""}`
    : "Sin configurar";

  return <div className="space-y-6">
    <header className="flex flex-col gap-4 sm:flex-row sm:items-center">
      <Button variant="ghost" size="icon" asChild><Link href="/dashboard/tenants" aria-label="Volver a tenants"><ArrowLeft className="h-4 w-4" /></Link></Button>
      <TenantBrand className="min-w-0 flex-1" imageClassName="h-16 w-24" displayName={tenant.name} logoUrl={tenant.logoUrl} />
      {canEdit && <Button type="button" variant="outline" onClick={() => setEditing(true)}><Pencil className="mr-2 h-4 w-4" />Editar información</Button>}
      <Badge variant={tenant.isActive ? "default" : "secondary"}>{tenant.isActive ? "Activo" : "Inactivo"}</Badge>
    </header>

    {provisioned && <TenantProvisioningSummary tenant={tenant} />}

    <div className="grid gap-5 xl:grid-cols-[1.15fr_.85fr]">
      <section className="rounded-2xl border bg-card p-6 shadow-sm">
        <div className="flex items-start justify-between gap-4"><div><p className="text-xs font-semibold uppercase tracking-wide text-primary">Identidad</p><h2 className="mt-1 text-xl font-semibold">Información legal y comercial</h2></div><FileText className="h-5 w-5 text-muted-foreground" /></div>
        <dl className="mt-6 grid gap-5 sm:grid-cols-2">
          <Value label="Tipo de persona" value={entityLabel} />
          <Value label={tenant.entityType === "NaturalPerson" ? "Nombre completo" : "Razón social"} value={tenant.legalName ?? tenant.name} />
          <Value label="Nombre comercial" value={tenant.name} />
          <Value label="Identificación" value={identification} mono />
          <Value label="Correo empresarial" value={tenant.email} />
          <Value label="Marca en reportes" value={tenant.logoUrl ? "Logo configurado" : "Pendiente de configurar"} />
        </dl>
      </section>

      <section className="rounded-2xl border bg-card p-6 shadow-sm">
        <div className="flex items-start justify-between gap-4"><div><p className="text-xs font-semibold uppercase tracking-wide text-primary">Acceso</p><h2 className="mt-1 text-xl font-semibold">Ingreso empresarial</h2></div><Fingerprint className="h-5 w-5 text-muted-foreground" /></div>
        <div className="mt-6 space-y-5">
          <Value label="Clave inmutable" value={tenant.tenantKey} mono />
          <div><p className="text-sm font-medium text-muted-foreground">Enlace de acceso</p><div className="mt-2 flex gap-2"><code className="min-w-0 flex-1 truncate rounded-md bg-muted px-3 py-2 text-xs">{loginUrl}</code><Button type="button" variant="outline" size="icon" disabled={!loginUrl} aria-label="Copiar enlace" onClick={() => void copy(loginUrl)}><Copy className="h-4 w-4" /></Button></div></div>
          <p className="flex items-center gap-2 text-xs text-muted-foreground"><Mail className="h-3.5 w-3.5" />El correo y el enlace pueden compartirse con el administrador autorizado.</p>
        </div>
      </section>
    </div>

    <TenantGovernancePanel tenant={tenant} />
    <PlatformTenantSubscriptionCard tenantId={tenant.tenantId} />
    <TenantEditDialog tenant={tenant} open={editing} onOpenChange={setEditing} onSaved={refetch} />
  </div>;
}

function Value({ label, value, mono = false }: { label: string; value: string; mono?: boolean }) { return <div><dt className="text-sm font-medium text-muted-foreground">{label}</dt><dd className={`mt-1 break-words font-medium ${mono ? "font-mono text-sm" : ""}`}>{value}</dd></div>; }
async function copy(value: string) { await navigator.clipboard.writeText(value); toast.success("Enlace copiado"); }
