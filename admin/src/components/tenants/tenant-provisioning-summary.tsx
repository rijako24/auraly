"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { Building2, CheckCircle2, Download, Mail, MonitorSmartphone, PackageCheck, ShieldCheck, Store, UserRound, Warehouse } from "lucide-react";

import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import type { ProvisionTenantRequest, ProvisionTenantResult } from "@/services/api/tenants";
import { loadPosInstaller } from "@/services/pos/pos-installer";
import { useTenantContextStore } from "@/stores/tenant-context-store";
import type { Tenant } from "@/types/entities";
import { formatDate } from "@/lib/utils";

type StoredSummary = { result: ProvisionTenantResult; form: ProvisionTenantRequest };

export function TenantProvisioningSummary({ tenant }: { tenant: Tenant }) {
  const router = useRouter();
  const selectTenant = useTenantContextStore((state) => state.selectTenant);
  const tenants = useTenantContextStore((state) => state.tenants);
  const [summary, setSummary] = useState<StoredSummary | null>(null);
  const [installerBusy, setInstallerBusy] = useState(false);
  const [installerError, setInstallerError] = useState<string | null>(null);

  useEffect(() => {
    try {
      const raw = sessionStorage.getItem(`tenant-provisioning-summary:${tenant.tenantId}`);
      setSummary(raw ? JSON.parse(raw) as StoredSummary : null);
    } catch {
      setSummary(null);
    }
  }, [tenant.tenantId]);

  const openOrganization = () => {
    if (tenants.some((item) => item.tenantId === tenant.tenantId)) selectTenant(tenant.tenantId);
    router.push("/dashboard");
  };

  const downloadInstaller = async () => {
    setInstallerBusy(true);
    setInstallerError(null);
    try {
      const installer = await loadPosInstaller();
      window.location.assign(installer.downloadUrl);
    } catch (error) {
      setInstallerError(error instanceof Error ? error.message : "No fue posible consultar el instalador.");
    } finally {
      setInstallerBusy(false);
    }
  };

  return (
    <div className="space-y-6">
      <section className="overflow-hidden rounded-3xl border border-emerald-200 bg-gradient-to-br from-slate-950 via-emerald-950 to-teal-900 text-white shadow-xl">
        <div className="grid gap-8 p-7 md:grid-cols-[1fr_auto] md:items-center">
          <div>
            <Badge className="border-emerald-300/30 bg-emerald-300/15 text-emerald-100 hover:bg-emerald-300/15">
              <CheckCircle2 className="mr-1.5 h-3.5 w-3.5" /> Aprovisionamiento completado
            </Badge>
            <p className="mt-5 text-xs font-bold uppercase tracking-[.2em] text-teal-300">Nueva organización Auraly</p>
            <h1 className="mt-2 text-3xl font-semibold">{tenant.name}</h1>
            <p className="mt-2 max-w-2xl text-sm leading-6 text-slate-300">
              La empresa quedó lista con su sede principal, bodegas, series operativas, consumidor final DIAN, roles iniciales e invitación de administrador.
            </p>
          </div>
          <div className="flex flex-col gap-2">
            <Button className="bg-teal-300 text-slate-950 hover:bg-teal-200" onClick={openOrganization}>
              <Store className="mr-2 h-4 w-4" /> Abrir organización
            </Button>
            <Button variant="outline" className="border-white/20 bg-white/5 text-white hover:bg-white/10 hover:text-white" onClick={() => router.push("/dashboard/tenants")}>
              Volver a empresas
            </Button>
          </div>
        </div>
      </section>

      <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
        <Metric icon={Building2} label="Empresa" value={summary?.form.legalName ?? tenant.name} detail={summary ? `NIT ${summary.form.nit}-${summary.form.verificationDigit}` : tenant.email} />
        <Metric icon={Store} label="Sede principal" value={summary?.form.businessName ?? "Sede creada"} detail={summary?.form.businessAddress ?? "Operación habilitada"} />
        <Metric icon={Warehouse} label="Inventario" value="2 bodegas" detail="VEN · venta / PED · pedidos" />
        <Metric icon={ShieldCheck} label="Estado" value={tenant.isActive ? "Activo" : "Inactivo"} detail={`Creado ${formatDate(tenant.createdAt)}`} />
      </div>

      <div className="grid gap-5 lg:grid-cols-[1.1fr_.9fr]">
        <section className="rounded-2xl border bg-card p-6 shadow-sm">
          <div className="flex items-start gap-3">
            <span className="rounded-xl bg-primary/10 p-2 text-primary"><PackageCheck className="h-5 w-5" /></span>
            <div><h2 className="font-semibold">Recursos preparados</h2><p className="text-sm text-muted-foreground">Configuración base creada de forma transaccional.</p></div>
          </div>
          <div className="mt-5 grid gap-3 sm:grid-cols-2">
            {["Bodegas VEN y PED", "Series de inventario desde 1", "Roles Cajero, Supervisor, Administrativo y Administrador", "Consumidor final 222222222222", "Unidades y motivos de inventario", "Invitación de administrador enviada"].map((item) =>
              <div key={item} className="flex gap-2 rounded-xl border bg-muted/20 p-3 text-sm"><CheckCircle2 className="mt-0.5 h-4 w-4 shrink-0 text-emerald-600" />{item}</div>)}
          </div>
        </section>

        <section className="rounded-2xl border bg-card p-6 shadow-sm">
          <div className="flex items-start gap-3">
            <span className="rounded-xl bg-primary/10 p-2 text-primary"><Mail className="h-5 w-5" /></span>
            <div><h2 className="font-semibold">Acceso del administrador</h2><p className="text-sm text-muted-foreground">Invitación segura con vigencia de 48 horas.</p></div>
          </div>
          <dl className="mt-5 space-y-3 text-sm">
            <Row label="Administrador" value="Pendiente de completar su registro" />
            <Row label="Invitación enviada a" value={summary?.form.invitationEmail ?? tenant.email} />
            <Row label="Estado" value="Invitación pendiente de activación" />
          </dl>
          <div className="mt-5 rounded-xl border border-amber-200 bg-amber-50 p-3 text-xs text-amber-900">
            El acceso solo se activa cuando el administrador define su contraseña desde el enlace enviado por Auraly.
          </div>
        </section>
      </div>

      <section className="flex flex-col gap-5 rounded-2xl border bg-card p-6 shadow-sm sm:flex-row sm:items-center sm:justify-between">
        <div className="flex items-start gap-3">
          <span className="rounded-xl bg-primary/10 p-2 text-primary"><MonitorSmartphone className="h-5 w-5" /></span>
          <div><h2 className="font-semibold">Instalar caja Auraly</h2><p className="text-sm text-muted-foreground">Descarga el instalador para Windows. Después podrás enrolar la caja en una sede disponible.</p></div>
        </div>
        <div className="text-right">
          <Button disabled={installerBusy} onClick={() => void downloadInstaller()}>
            <Download className="mr-2 h-4 w-4" />{installerBusy ? "Consultando..." : "Descargar instalador"}
          </Button>
          {installerError && <p className="mt-2 max-w-sm text-xs text-destructive">{installerError}</p>}
        </div>
      </section>

      {summary && <section className="rounded-2xl border bg-muted/20 p-5">
        <p className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">Trazabilidad del aprovisionamiento</p>
        <div className="mt-3 grid gap-3 text-xs sm:grid-cols-2 lg:grid-cols-4">
          <Id label="Tenant" value={summary.result.tenantId} />
          <Id label="Sede" value={summary.result.businessId} />
          <Id label="Bodega de venta" value={summary.result.salesWarehouseId} />
          <Id label="Administrador" value={summary.result.administratorUserId ?? "Pendiente de registro"} />
        </div>
      </section>}
    </div>
  );
}

function Metric({ icon: Icon, label, value, detail }: { icon: typeof UserRound; label: string; value: string; detail: string }) {
  return <article className="rounded-2xl border bg-card p-5 shadow-sm"><span className="grid h-10 w-10 place-items-center rounded-xl bg-primary/10 text-primary"><Icon className="h-5 w-5" /></span><p className="mt-4 text-xs font-medium uppercase tracking-wide text-muted-foreground">{label}</p><p className="mt-1 font-semibold">{value}</p><p className="mt-1 text-xs text-muted-foreground">{detail}</p></article>;
}
function Row({ label, value }: { label: string; value: string }) { return <div className="flex justify-between gap-4 border-b pb-2 last:border-0"><dt className="text-muted-foreground">{label}</dt><dd className="text-right font-medium">{value}</dd></div>; }
function Id({ label, value }: { label: string; value: string }) { return <div><p className="text-muted-foreground">{label}</p><code className="mt-1 block truncate rounded bg-background px-2 py-1">{value}</code></div>; }
