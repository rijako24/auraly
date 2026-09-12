"use client";

import { AlertTriangle, ArrowLeft, Building2, CheckCircle2, FileKey2, Loader2, MonitorSmartphone, PackageCheck, PackageOpen, Receipt, RotateCcw, Warehouse, Wifi, WifiOff } from "lucide-react";
import Link from "next/link";
import { useEffect, useMemo, useState } from "react";
import type { PosSaleDocumentType } from "@/services/pos/pos-edge-client";
import {
  fiscalLaunchReadinessError,
} from "@/services/pos/pos-fiscal-guard";
import { rememberedSalesWorkspaceKey, salesWorkspaceKey, type SalesWorkspaceOption } from "@/services/pos/online-pos-client";
import { resolvePosWorkspaceSelection } from "@/services/pos/pos-workspace-selection";
import { Checkbox } from "@/components/ui/checkbox";
import { Progress } from "@/components/ui/progress";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { posInventoryPolicyPresentation } from "./pos-inventory-policy";
import {
  posPreparationView,
  type PosPreparationHealth,
} from "./pos-preparation-progress";
import { posPublicError } from "./pos-public-error";

export type PosSetupPreparation = {
  health: PosPreparationHealth | null;
  message?: string | null;
  error?: string | null;
  retrying?: boolean;
  onRetry?: () => void;
  onBack: () => void;
};

type Props = {
  options: SalesWorkspaceOption[];
  loading: boolean;
  error: string | null;
  notice?: string | null;
  tenantName: string;
  userDisplayName: string;
  onSelect: (option: SalesWorkspaceOption, documentType: PosSaleDocumentType) => Promise<void>;
  onCancel?: () => void;
  edgeCapable?: boolean;
  canEnrollOffline?: boolean;
  enrollmentUnavailableReason?: string | null;
  enrollmentCapacity?: { active: number; maximum: number } | null;
  onEnroll?: (option: SalesWorkspaceOption, documentType: PosSaleDocumentType) => Promise<void>;
  forcedDocumentType?: PosSaleDocumentType;
  fiscalHabilitationOnly?: boolean;
  enrollmentState?: "web" | "available" | "enrolled";
  configurationOffline?: boolean;
  configuredDocumentType?: PosSaleDocumentType;
  preparation?: PosSetupPreparation | null;
};

export function PosOnlineSetup({ options, loading, error, notice, tenantName, userDisplayName, onSelect, onCancel, edgeCapable = false, canEnrollOffline = false, enrollmentUnavailableReason, enrollmentCapacity, onEnroll, forcedDocumentType, fiscalHabilitationOnly = false, enrollmentState = "web", configurationOffline = false, configuredDocumentType, preparation = null }: Props) {
  const businesses = useMemo(() => Array.from(new Map(options.map((option) => [option.businessId, option.businessName]))), [options]);
  const [businessId, setBusinessId] = useState("");
  const [warehouseId, setWarehouseId] = useState("");
  const [documentType, setDocumentType] = useState<PosSaleDocumentType>("SalesReceipt");
  const [busy, setBusy] = useState(false);
  const [prepareInstalled, setPrepareInstalled] = useState(false);
  const [fiscalError, setFiscalError] = useState<string | null>(null);
  const warehouses = useMemo(() => options.filter((option) => option.businessId === businessId), [businessId, options]);
  const selected = useMemo(() => warehouses.find((option) => option.warehouseId === warehouseId), [warehouseId, warehouses]);

  useEffect(() => {
    if (forcedDocumentType) {
      setDocumentType(forcedDocumentType);
      return;
    }
    if (configuredDocumentType) {
      setDocumentType(configuredDocumentType);
      return;
    }
    const saved = window.localStorage.getItem("auraly.pos.document-type");
    if (saved === "SalesInvoice" || saved === "SalesReceipt") setDocumentType(saved);
  }, [configuredDocumentType, forcedDocumentType]);
  useEffect(() => {
    if (!options.length) return;
    const remembered = rememberedSalesWorkspaceKey();
    const saved = options.find((option) => salesWorkspaceKey(option.businessId, option.warehouseId) === remembered);
    const value = resolvePosWorkspaceSelection(options, saved?.businessId ?? businessId, saved?.warehouseId ?? warehouseId);
    if (value.businessId !== businessId) setBusinessId(value.businessId);
    if (value.warehouseId !== warehouseId) setWarehouseId(value.warehouseId);
  }, [options, businessId, warehouseId]);
  async function choose(mode: "online" | "enroll") {
    if (configurationOffline) {
      onCancel?.();
      return;
    }
    if (!selected) return;
    setBusy(true);
    setFiscalError(null);
    try {
      const effectiveMode = fiscalHabilitationOnly ? "online" : mode;
      if (documentType === "SalesInvoice") {
        const readinessError = fiscalLaunchReadinessError(effectiveMode, {
          isReadyForOnlineSales: selected.fiscalReadyForOnlineSales === true,
          hasDianDocumentQuota: selected.hasDianDocumentQuota,
        }, fiscalHabilitationOnly);
        if (readinessError) {
          setFiscalError(readinessError);
          return;
        }
      }
      if (effectiveMode === "enroll") await onEnroll?.(selected, documentType);
      else await onSelect(selected, documentType);
    } catch (caught) {
      setFiscalError(caught instanceof Error ? caught.message : "No fue posible cargar esta ubicación.");
    } finally { setBusy(false); }
  }

  const invoice = documentType === "SalesInvoice";
  const fiscalReady = selected?.fiscalReadyForOnlineSales === true;
  const fiscalWarnings = selected?.fiscalWarningMessages ?? [];
  const preparing = Boolean(preparation);
  return <main className="relative min-h-screen overflow-auto bg-[#071a1d] p-4 text-white sm:p-5">
    {preparing ? <button type="button" onClick={preparation!.onBack} className="fixed left-4 top-4 z-20 inline-flex h-10 items-center gap-2 rounded-xl border border-white/15 bg-[#0b2428] px-3 text-sm font-semibold"><ArrowLeft className="h-4 w-4" />Volver</button> : onCancel ? <button type="button" onClick={onCancel} className="fixed left-4 top-4 z-20 inline-flex h-10 items-center gap-2 rounded-xl border border-white/15 bg-[#0b2428] px-3 text-sm font-semibold"><ArrowLeft className="h-4 w-4" />Volver</button> : <Link href="/dashboard" className="fixed left-4 top-4 z-20 inline-flex h-10 items-center gap-2 rounded-xl border border-white/15 bg-[#0b2428] px-3 text-sm font-semibold"><ArrowLeft className="h-4 w-4" />Panel</Link>}
    <div className="mx-auto flex min-h-[calc(100vh-2rem)] max-w-5xl items-center py-14"><section className="grid w-full overflow-hidden rounded-[2rem] border border-white/10 bg-[#0b2428] shadow-2xl md:grid-cols-[.72fr_1.45fr]">
      <aside className="bg-gradient-to-br from-teal-400/20 to-transparent p-7"><span className="grid h-12 w-12 place-items-center rounded-2xl bg-teal-300 text-[#071a1d]"><MonitorSmartphone /></span><p className="mt-7 text-xs font-bold uppercase tracking-[.15em] text-teal-200">{tenantName || "Auraly"}</p><h1 className="mt-2 text-3xl font-black">{preparing ? "Preparando tu caja" : fiscalHabilitationOnly ? "Factura de habilitación" : "Prepara facturación"}</h1><p className="mt-3 text-sm leading-6 text-slate-300">Hola, {userDisplayName}. {preparing ? "La preparación continúa aquí. Verás el avance real y, si algo falla, podrás reintentar sin quedar bloqueado." : fiscalHabilitationOnly ? "Confirma la sede para emitir un documento técnico contra el set de pruebas DIAN." : "Confirma ubicación y documento en una sola pantalla. La activación DIAN se administra únicamente desde Configuración fiscal."}</p><ol className="mt-7 space-y-3 text-sm">{preparing ? <><Step number="1" text="Identidad local" active /><Step number="2" text="Catálogo y precios" active /><Step number="3" text="Validación final" active /></> : <><Step number="1" text="Sede y bodega" active={!!selected} /><Step number="2" text="Documento de venta" active /><Step number="3" text={fiscalHabilitationOnly ? "Prueba DIAN" : invoice ? "Validación fiscal" : "Entrar a ventas"} active={fiscalHabilitationOnly || !invoice || fiscalReady} /></>}</ol></aside>
      <div className="p-6 md:p-9">{preparation ? <PreparationPanel value={preparation} /> : loading ? <Loading /> : <div className="space-y-5">
        <Combo title="Sede" icon={Building2} value={businessId} onChange={(value) => { setBusinessId(value); setWarehouseId(""); }} disabled={configurationOffline} items={businesses.map(([id, name]) => ({ id, name }))} />
        <Combo title="Bodega" icon={Warehouse} value={warehouseId} onChange={setWarehouseId} disabled={configurationOffline || !businessId} items={warehouses.map((option) => ({ id: option.warehouseId, name: [option.warehouseCode, option.warehouseName].filter(Boolean).join(" · ") }))} />
        {selected && <InventoryPolicyNotice allowsNegativeStock={selected.warehouseAllowsNegativeStockSales}/>}
        {selected && <div><p className="mb-2 text-sm font-semibold">{forcedDocumentType ? "Documento de habilitación" : "Documento predeterminado"}</p><div className="grid grid-cols-2 gap-2"><DocumentButton active={invoice} icon={FileKey2} title="Factura electrónica" disabled={configurationOffline} onClick={() => setDocumentType("SalesInvoice")} /><DocumentButton active={!invoice} icon={Receipt} title="Comprobante de venta" disabled={Boolean(forcedDocumentType) || configurationOffline} onClick={() => setDocumentType("SalesReceipt")} /></div><p className="mt-2 text-xs text-slate-400">{forcedDocumentType ? "El asistente mantiene la factura electrónica para enviar el documento al set de pruebas DIAN." : "Un cliente configurado para factura electrónica la fuerza automáticamente sin cambiar este predeterminado."}</p></div>}
        {selected && invoice && fiscalHabilitationOnly && <div className="flex items-start gap-3 rounded-2xl border border-violet-300/30 bg-violet-300/10 p-4 text-sm text-violet-50"><FileKey2 className="mt-0.5 h-5 w-5 shrink-0" /><span><strong className="block text-white">Modo de habilitación DIAN</strong><span className="mt-1 block leading-5 text-violet-100">Usará la configuración de pruebas de esta sede. No requiere que la facturación productiva esté activa y no genera inventario, venta ni contabilidad.</span></span></div>}
        {selected && invoice && !fiscalHabilitationOnly && !fiscalReady && <div className="rounded-2xl border border-amber-300/25 bg-amber-100/10 p-5 text-amber-100"><div className="flex gap-3"><FileKey2 className="h-6 w-6 shrink-0" /><div><p className="font-bold">Facturación electrónica pendiente</p><p className="mt-1 text-sm">El POS no configura certificados ni resoluciones. Un administrador debe completar la activación DIAN para esta sede.</p><Link href="/dashboard/settings/fiscal" className="mt-3 inline-block font-bold underline">Abrir configuración fiscal</Link></div></div></div>}
        {selected && invoice && !fiscalHabilitationOnly && fiscalReady && <div className="flex items-center gap-2 rounded-xl border border-emerald-300/20 bg-emerald-300/10 p-3 text-sm text-emerald-100"><CheckCircle2 className="h-5 w-5" />Facturación electrónica activa para esta sede.</div>}
        {selected && !fiscalHabilitationOnly && fiscalWarnings.length > 0 && <div role="alert" className="rounded-2xl border border-amber-300/30 bg-amber-100/10 p-4 text-sm text-amber-100"><p className="font-bold">Atención con la resolución DIAN</p><ul className="mt-2 list-disc space-y-1 pl-5">{fiscalWarnings.map((warning) => <li key={warning}>{warning}</li>)}</ul></div>}
        {!configurationOffline && (error || fiscalError) && <p className="rounded-xl border border-red-300/20 bg-red-400/10 p-3 text-sm text-red-100">{error || fiscalError}</p>}
        {configurationOffline && <p role="status" className="rounded-xl border border-amber-300/25 bg-amber-300/10 p-3 text-sm font-semibold text-amber-100">Sin conexión con Auraly. La configuración se muestra en modo de solo lectura.</p>}
        {notice && <p role="status" className="flex items-center gap-2 rounded-xl border border-teal-300/25 bg-teal-300/10 p-3 text-sm font-semibold text-teal-50"><Loader2 className="h-4 w-4 animate-spin" />{notice}</p>}
        {!options.length && !error && <p className="rounded-xl border border-amber-300/20 bg-amber-300/10 p-4 text-sm text-amber-100">No hay bodegas activas disponibles para este usuario.</p>}
        {enrollmentState === "enrolled" && <div role="status" className="flex items-start gap-3 rounded-2xl border border-emerald-300/30 bg-emerald-300/10 p-4 text-sm text-emerald-50">
          <CheckCircle2 className="mt-0.5 h-5 w-5 shrink-0" />
          <span><strong className="block text-white">Equipo enrolado</strong><span className="mt-1 block leading-5 text-emerald-100">Esta instalación trabaja con el motor local y recibe los cambios por sincronización.</span></span>
        </div>}
        {edgeCapable && !fiscalHabilitationOnly && <label className={`flex items-start gap-3 rounded-2xl border p-4 text-sm transition ${prepareInstalled ? "border-teal-300/50 bg-teal-300/15" : "border-white/15 bg-[#102e33]"} ${canEnrollOffline && !configurationOffline ? "cursor-pointer" : "opacity-70"}`}>
          <Checkbox className="mt-0.5 border-teal-200 data-[state=checked]:bg-teal-300 data-[state=checked]:text-[#071a1d]" checked={prepareInstalled} disabled={configurationOffline || !canEnrollOffline || busy} onCheckedChange={(checked) => setPrepareInstalled(checked === true)} />
          <span><strong className="block text-white">Preparar este equipo para trabajar sin conexión</strong><span className="mt-1 block leading-5 text-slate-300">Descarga usuarios, permisos, productos, clientes y precios. Después del enrolamiento la caja usa siempre el motor local sincronizado.</span>{enrollmentCapacity && <span className="mt-2 block text-xs text-teal-100">Cajas enroladas: {enrollmentCapacity.active} de {enrollmentCapacity.maximum}</span>}{!canEnrollOffline && <span role="alert" className="mt-2 block font-semibold text-amber-100">{enrollmentUnavailableReason ?? "No es posible enrolar otra caja. Comunícate con el administrador."}</span>}</span>
        </label>}
        <button onClick={() => void choose(!fiscalHabilitationOnly && edgeCapable && prepareInstalled ? "enroll" : "online")} disabled={(!selected && !configurationOffline) || (configurationOffline && !onCancel) || busy} className="h-12 w-full rounded-xl bg-teal-300 font-bold text-[#071a1d] disabled:opacity-35">{busy ? (!fiscalHabilitationOnly && prepareInstalled ? "Preparando equipo…" : "Entrando…") : fiscalHabilitationOnly ? "Continuar con la prueba DIAN" : "Continuar a ventas"}</button>
      </div>}</div>
    </section></div>
  </main>;
}

function Combo({ title, icon: Icon, items, value, onChange, disabled = false }: { title: string; icon: typeof Building2; items: { id: string; name: string }[]; value: string; onChange: (value: string) => void; disabled?: boolean }) {
  const only = items.length === 1 ? items[0] : null;
  return <div className="block"><span className="mb-2 flex items-center gap-2 text-sm font-semibold"><Icon className="h-4 w-4 text-teal-200" />{title}</span>{only ? <div className="flex h-12 items-center rounded-xl border border-teal-300/30 bg-[#102e33] px-4 font-semibold text-white">{only.name}</div> : <Select value={value} onValueChange={onChange} disabled={disabled}><SelectTrigger className="h-12 rounded-xl border-teal-300/30 bg-[#102e33] px-4 font-semibold text-white focus:ring-teal-300"><SelectValue placeholder={`Selecciona ${title.toLocaleLowerCase("es")}`} /></SelectTrigger><SelectContent>{items.map((item) => <SelectItem key={item.id} value={item.id}>{item.name}</SelectItem>)}</SelectContent></Select>}</div>;
}

function PreparationPanel({ value }: { value: PosSetupPreparation }) {
  const view = posPreparationView(value.health);
  const error = posPublicError(
    value.error ?? (value.health?.lastSynchronizationFailed ? value.health.lastSynchronizationError : null),
    "No fue posible terminar la preparación de esta caja.",
  );
  const failed = Boolean(error);
  const visibleProgress = view.resourceProgress ?? view.overallProgress;
  const currentResource = value.message ?? view.currentResource;

  return <div aria-live="polite" className="relative flex min-h-[31rem] flex-col justify-center">
    <div className={`rounded-[1.75rem] border p-5 md:p-6 ${failed ? "border-red-300/25 bg-red-400/[.07]" : "border-teal-200/15 bg-[#092126]"}`}>
      <div className="flex items-start gap-4">
        <span className={`grid h-12 w-12 shrink-0 place-items-center rounded-2xl border ${failed ? "border-red-300/30 bg-red-300/10 text-red-200" : "border-teal-200/25 bg-teal-300/10 text-teal-200"}`}>
          {failed ? <AlertTriangle className="h-6 w-6" /> : <Loader2 className="h-6 w-6 animate-spin" />}
        </span>
        <div className="min-w-0">
          <p className="text-xs font-bold uppercase tracking-[.18em] text-teal-200">Preparando tu caja</p>
          <h2 className="mt-1 text-2xl font-black text-white">{failed ? "No se pudo completar la preparación" : view.title}</h2>
          <p className={`mt-2 text-sm leading-6 ${failed ? "text-red-100" : "text-slate-300"}`}>{error ?? view.detail}</p>
        </div>
      </div>

      <div className="mt-6 rounded-2xl border border-white/10 bg-black/15 p-4">
        <div className="flex items-end justify-between gap-4">
          <div className="min-w-0">
            <p className="text-xs font-semibold uppercase tracking-[.16em] text-slate-400">Ahora</p>
            <p className="mt-1 truncate font-bold text-white">{currentResource}</p>
          </div>
          <p className="shrink-0 text-3xl font-black tabular-nums text-teal-200">
            {visibleProgress === null ? "…" : `${visibleProgress}%`}
          </p>
        </div>
        {visibleProgress === null ? (
          <div role="progressbar" aria-label="Preparación en curso" className="mt-4 h-2.5 overflow-hidden rounded-full bg-white/10">
            <div className="h-full w-1/3 animate-pulse rounded-full bg-gradient-to-r from-teal-400 via-cyan-200 to-teal-400" />
          </div>
        ) : (
          <Progress aria-label={`Preparación ${visibleProgress}%`} value={visibleProgress} className="mt-4 h-2.5 bg-white/10 [&>div]:bg-gradient-to-r [&>div]:from-teal-400 [&>div]:to-cyan-200" />
        )}
        <p className="mt-3 text-xs text-slate-300">{view.processedLabel ?? "El avance queda guardado en este equipo"}</p>
      </div>

      <div className="mt-4 flex items-center gap-3 rounded-xl border border-white/10 bg-white/[0.04] p-3 text-sm text-slate-200">
        {value.health?.serverConnected ? <Wifi className="h-4 w-4 shrink-0 text-emerald-300" /> : <WifiOff className="h-4 w-4 shrink-0 text-amber-300" />}
        <span>{view.connectionLabel}</span>
      </div>

      {failed && value.onRetry && <div className="mt-5">
        <button type="button" onClick={value.onRetry} disabled={value.retrying} className="inline-flex h-11 items-center justify-center gap-2 rounded-xl bg-teal-300 px-4 font-bold text-[#071a1d] disabled:opacity-40">
          <RotateCcw className={`h-4 w-4 ${value.retrying ? "animate-spin" : ""}`} />
          {value.retrying ? "Reintentando…" : "Reintentar preparación"}
        </button>
      </div>}
    </div>
  </div>;
}
function InventoryPolicyNotice({ allowsNegativeStock }: { allowsNegativeStock: boolean }) {
  const presentation = posInventoryPolicyPresentation(allowsNegativeStock);
  const Icon = allowsNegativeStock ? PackageOpen : PackageCheck;
  return <div className={`flex items-start gap-3 rounded-xl border p-3 text-sm ${allowsNegativeStock ? "border-amber-300/25 bg-amber-300/10 text-amber-100" : "border-emerald-300/25 bg-emerald-300/10 text-emerald-100"}`}>
    <Icon className="mt-0.5 h-5 w-5 shrink-0" />
    <span><strong className="block">{presentation.setupLabel}</strong><span className="mt-0.5 block text-xs opacity-80">{presentation.detail}</span></span>
  </div>;
}
function DocumentButton({ active, icon: Icon, title, onClick, disabled = false }: { active: boolean; icon: typeof Receipt; title: string; onClick: () => void; disabled?: boolean }) { return <button type="button" onClick={onClick} disabled={disabled} className={`flex min-h-16 items-center gap-3 rounded-2xl border p-3 text-left text-sm font-bold transition disabled:cursor-not-allowed disabled:opacity-35 ${active ? "border-teal-300 bg-teal-300/15" : "border-white/15 bg-[#102e33]"}`}><Icon className="h-5 w-5 shrink-0 text-teal-200" />{title}{active && <CheckCircle2 className="ml-auto h-4 w-4 text-teal-200" />}</button>; }
function Loading({ text = "Cargando sedes y bodegas…" }: { text?: string }) {
  return <div role="status" aria-live="polite" className="relative flex min-h-[28rem] flex-col items-center justify-center overflow-hidden rounded-[1.75rem] border border-teal-200/10 bg-[#092126] px-6 text-center">
    <div aria-hidden className="absolute h-72 w-72 rounded-full bg-teal-300/[.06] blur-3xl" />
    <div className="auraly-setup-loader relative grid h-28 w-28 place-items-center rounded-full border border-teal-200/25 bg-[#0b2b30] shadow-[0_0_60px_rgba(94,234,212,.14)]">
      <span className="absolute inset-2 rounded-full border border-dashed border-teal-200/35" />
      <span className="absolute inset-[-1px] rounded-full border-2 border-transparent border-t-teal-200 border-r-teal-300/60" />
      <Building2 className="h-9 w-9 text-teal-200" />
    </div>
    <p className="mt-7 text-lg font-bold text-white">{text}</p>
    <div aria-hidden className="relative mt-3 h-6 w-full max-w-sm text-sm text-slate-300">
      <span className="auraly-setup-loader-stage">Conectando con Auraly</span>
      <span className="auraly-setup-loader-stage">Verificando tus sedes</span>
      <span className="auraly-setup-loader-stage">Preparando la caja</span>
    </div>
    <span className="sr-only">Espera mientras Auraly prepara la configuración del punto de venta.</span>
  </div>;
}
function Step({ number, text, active }: { number: string; text: string; active: boolean }) { return <li className={`flex items-center gap-3 ${active ? "text-white" : "text-slate-500"}`}><span className={`grid h-7 w-7 place-items-center rounded-full ${active ? "bg-teal-300 text-[#071a1d]" : "bg-white/10"}`}>{number}</span>{text}</li>; }
