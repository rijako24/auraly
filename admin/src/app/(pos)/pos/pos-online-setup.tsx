"use client";

import { ArrowLeft, Building2, CheckCircle2, FileKey2, Loader2, MonitorSmartphone, Receipt, Warehouse } from "lucide-react";
import Link from "next/link";
import { useEffect, useMemo, useState } from "react";
import { fiscalConfigurationApi, type FiscalResolutionConfiguration } from "@/services/api/fiscal-configuration";
import type { PosSaleDocumentType } from "@/services/pos/pos-edge-client";
import {
  fiscalLaunchReadinessError,
} from "@/services/pos/pos-fiscal-guard";
import { rememberedSalesWorkspaceKey, salesWorkspaceKey, type SalesWorkspaceOption } from "@/services/pos/online-pos-client";
import { resolvePosWorkspaceSelection } from "@/services/pos/pos-workspace-selection";
import { Checkbox } from "@/components/ui/checkbox";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";

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
};

export function PosOnlineSetup({ options, loading, error, notice, tenantName, userDisplayName, onSelect, onCancel, edgeCapable = false, canEnrollOffline = false, enrollmentUnavailableReason, enrollmentCapacity, onEnroll, forcedDocumentType }: Props) {
  const businesses = useMemo(() => Array.from(new Map(options.map((option) => [option.businessId, option.businessName]))), [options]);
  const [businessId, setBusinessId] = useState("");
  const [warehouseId, setWarehouseId] = useState("");
  const [documentType, setDocumentType] = useState<PosSaleDocumentType>("SalesReceipt");
  const [busy, setBusy] = useState(false);
  const [prepareInstalled, setPrepareInstalled] = useState(false);
  const [fiscal, setFiscal] = useState<FiscalResolutionConfiguration | null>(null);
  const [fiscalLoading, setFiscalLoading] = useState(false);
  const [fiscalError, setFiscalError] = useState<string | null>(null);
  const warehouses = useMemo(() => options.filter((option) => option.businessId === businessId), [businessId, options]);
  const selected = useMemo(() => warehouses.find((option) => option.warehouseId === warehouseId), [warehouseId, warehouses]);

  useEffect(() => {
    if (forcedDocumentType) {
      setDocumentType(forcedDocumentType);
      return;
    }
    const saved = window.localStorage.getItem("auraly.pos.document-type");
    if (saved === "SalesInvoice" || saved === "SalesReceipt") setDocumentType(saved);
  }, [forcedDocumentType]);
  useEffect(() => {
    if (!options.length) return;
    const remembered = rememberedSalesWorkspaceKey();
    const saved = options.find((option) => salesWorkspaceKey(option.businessId, option.warehouseId) === remembered);
    const value = resolvePosWorkspaceSelection(options, saved?.businessId ?? businessId, saved?.warehouseId ?? warehouseId);
    if (value.businessId !== businessId) setBusinessId(value.businessId);
    if (value.warehouseId !== warehouseId) setWarehouseId(value.warehouseId);
  }, [options, businessId, warehouseId]);
  useEffect(() => {
    if (!selected) { setFiscal(null); return; }
    let active = true;
    setFiscalLoading(true);
    setFiscalError(null);
    fiscalConfigurationApi.get(selected.businessId)
      .then((value) => active && setFiscal(value))
      .catch((caught: unknown) => active && setFiscalError(caught instanceof Error ? caught.message : "No fue posible verificar la activación fiscal."))
      .finally(() => active && setFiscalLoading(false));
    return () => { active = false; };
  }, [selected]);
  async function choose(mode: "online" | "enroll") {
    if (!selected) return;
    setBusy(true);
    setFiscalError(null);
    try {
      if (documentType === "SalesInvoice") {
        const latest = await fiscalConfigurationApi.get(selected.businessId);
        setFiscal(latest);
        const readinessError = fiscalLaunchReadinessError(mode, latest);
        if (readinessError) {
          setFiscalError(readinessError);
          return;
        }
      }
      if (mode === "enroll") await onEnroll?.(selected, documentType);
      else await onSelect(selected, documentType);
    } catch (caught) {
      setFiscalError(caught instanceof Error ? caught.message : "No fue posible cargar esta ubicación.");
    } finally { setBusy(false); }
  }

  const invoice = documentType === "SalesInvoice";
  return <main className="relative min-h-screen overflow-auto bg-[#071a1d] p-4 text-white sm:p-5">
    {onCancel ? <button type="button" onClick={onCancel} className="fixed left-4 top-4 z-20 inline-flex h-10 items-center gap-2 rounded-xl border border-white/15 bg-[#0b2428] px-3 text-sm font-semibold"><ArrowLeft className="h-4 w-4" />Volver</button> : <Link href="/dashboard" className="fixed left-4 top-4 z-20 inline-flex h-10 items-center gap-2 rounded-xl border border-white/15 bg-[#0b2428] px-3 text-sm font-semibold"><ArrowLeft className="h-4 w-4" />Panel</Link>}
    <div className="mx-auto flex min-h-[calc(100vh-2rem)] max-w-5xl items-center py-14"><section className="grid w-full overflow-hidden rounded-[2rem] border border-white/10 bg-[#0b2428] shadow-2xl md:grid-cols-[.72fr_1.45fr]">
      <aside className="bg-gradient-to-br from-teal-400/20 to-transparent p-7"><span className="grid h-12 w-12 place-items-center rounded-2xl bg-teal-300 text-[#071a1d]"><MonitorSmartphone /></span><p className="mt-7 text-xs font-bold uppercase tracking-[.15em] text-teal-200">{tenantName || "Auraly"}</p><h1 className="mt-2 text-3xl font-black">Prepara facturación</h1><p className="mt-3 text-sm leading-6 text-slate-300">Hola, {userDisplayName}. Confirma ubicación y documento en una sola pantalla. La activación DIAN se administra únicamente desde Configuración fiscal.</p><ol className="mt-7 space-y-3 text-sm"><Step number="1" text="Sede y bodega" active={!!selected} /><Step number="2" text="Documento de venta" active /><Step number="3" text={invoice ? "Validación fiscal" : "Entrar a ventas"} active={!invoice || !!fiscal?.isReadyForOnlineSales} /></ol></aside>
      <div className="p-6 md:p-9">{loading ? <Loading /> : <div className="space-y-5">
        <Combo title="Sede" icon={Building2} value={businessId} onChange={(value) => { setBusinessId(value); setWarehouseId(""); }} items={businesses.map(([id, name]) => ({ id, name }))} />
        <Combo title="Bodega" icon={Warehouse} value={warehouseId} onChange={setWarehouseId} disabled={!businessId} items={warehouses.map((option) => ({ id: option.warehouseId, name: `${option.warehouseCode} · ${option.warehouseName}` }))} />
        {selected && <div><p className="mb-2 text-sm font-semibold">{forcedDocumentType ? "Documento de habilitación" : "Documento predeterminado"}</p><div className="grid grid-cols-2 gap-2"><DocumentButton active={invoice} icon={FileKey2} title="Factura electrónica" onClick={() => setDocumentType("SalesInvoice")} /><DocumentButton active={!invoice} icon={Receipt} title="Comprobante de venta" disabled={Boolean(forcedDocumentType)} onClick={() => setDocumentType("SalesReceipt")} /></div><p className="mt-2 text-xs text-slate-400">{forcedDocumentType ? "El asistente mantiene la factura electrónica para enviar el documento al set de pruebas DIAN." : "Un cliente configurado para factura electrónica la fuerza automáticamente sin cambiar este predeterminado."}</p></div>}
        {selected && invoice && fiscalLoading && <Loading text="Verificando activación fiscal…" />}
        {selected && invoice && !fiscalLoading && fiscal && !fiscal.isReadyForOnlineSales && <div className="rounded-2xl border border-amber-300/25 bg-amber-100/10 p-5 text-amber-100"><div className="flex gap-3"><FileKey2 className="h-6 w-6 shrink-0" /><div><p className="font-bold">Facturación electrónica pendiente</p><p className="mt-1 text-sm">El POS no configura certificados ni resoluciones. Un administrador debe completar la activación DIAN para esta sede.</p><Link href="/dashboard/settings/fiscal" className="mt-3 inline-block font-bold underline">Abrir configuración fiscal</Link></div></div></div>}
        {selected && invoice && fiscal?.isReadyForOnlineSales && <div className="flex items-center gap-2 rounded-xl border border-emerald-300/20 bg-emerald-300/10 p-3 text-sm text-emerald-100"><CheckCircle2 className="h-5 w-5" />Resolución {fiscal.authorizationNumber} activa para esta sede.</div>}
        {(error || fiscalError) && <p className="rounded-xl border border-red-300/20 bg-red-400/10 p-3 text-sm text-red-100">{error || fiscalError}</p>}
        {notice && <p role="status" className="flex items-center gap-2 rounded-xl border border-teal-300/25 bg-teal-300/10 p-3 text-sm font-semibold text-teal-50"><Loader2 className="h-4 w-4 animate-spin" />{notice}</p>}
        {!options.length && !error && <p className="rounded-xl border border-amber-300/20 bg-amber-300/10 p-4 text-sm text-amber-100">No hay bodegas activas disponibles para este usuario.</p>}
        {edgeCapable && <label className={`flex items-start gap-3 rounded-2xl border p-4 text-sm transition ${prepareInstalled ? "border-teal-300/50 bg-teal-300/15" : "border-white/15 bg-[#102e33]"} ${canEnrollOffline ? "cursor-pointer" : "opacity-70"}`}>
          <Checkbox className="mt-0.5 border-teal-200 data-[state=checked]:bg-teal-300 data-[state=checked]:text-[#071a1d]" checked={prepareInstalled} disabled={!canEnrollOffline || busy} onCheckedChange={(checked) => setPrepareInstalled(checked === true)} />
          <span><strong className="block text-white">Preparar este equipo para trabajar sin conexión</strong><span className="mt-1 block leading-5 text-slate-300">Descarga usuarios, permisos, productos, clientes y precios. Después del enrolamiento la caja usa siempre el motor local sincronizado.</span>{enrollmentCapacity && <span className="mt-2 block text-xs text-teal-100">Cajas enroladas: {enrollmentCapacity.active} de {enrollmentCapacity.maximum}</span>}{!canEnrollOffline && <span role="alert" className="mt-2 block font-semibold text-amber-100">{enrollmentUnavailableReason ?? "No es posible enrolar otra caja. Comunícate con el administrador."}</span>}</span>
        </label>}
        <button onClick={() => void choose(edgeCapable && prepareInstalled ? "enroll" : "online")} disabled={!selected || busy || (invoice && fiscalLoading)} className="h-12 w-full rounded-xl bg-teal-300 font-bold text-[#071a1d] disabled:opacity-35">{busy ? (prepareInstalled ? "Preparando equipo…" : "Entrando…") : "Continuar a ventas"}</button>
      </div>}</div>
    </section></div>
  </main>;
}

function Combo({ title, icon: Icon, items, value, onChange, disabled = false }: { title: string; icon: typeof Building2; items: { id: string; name: string }[]; value: string; onChange: (value: string) => void; disabled?: boolean }) {
  const only = items.length === 1 ? items[0] : null;
  return <div className="block"><span className="mb-2 flex items-center gap-2 text-sm font-semibold"><Icon className="h-4 w-4 text-teal-200" />{title}</span>{only ? <div className="flex h-12 items-center rounded-xl border border-teal-300/30 bg-[#102e33] px-4 font-semibold text-white">{only.name}</div> : <Select value={value} onValueChange={onChange} disabled={disabled}><SelectTrigger className="h-12 rounded-xl border-teal-300/30 bg-[#102e33] px-4 font-semibold text-white focus:ring-teal-300"><SelectValue placeholder={`Selecciona ${title.toLocaleLowerCase("es")}`} /></SelectTrigger><SelectContent>{items.map((item) => <SelectItem key={item.id} value={item.id}>{item.name}</SelectItem>)}</SelectContent></Select>}</div>;
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
      <span className="auraly-setup-loader-stage [animation-delay:1.6s]">Verificando tus sedes</span>
      <span className="auraly-setup-loader-stage [animation-delay:3.2s]">Preparando la caja</span>
    </div>
    <span className="sr-only">Espera mientras Auraly prepara la configuración del punto de venta.</span>
  </div>;
}
function Step({ number, text, active }: { number: string; text: string; active: boolean }) { return <li className={`flex items-center gap-3 ${active ? "text-white" : "text-slate-500"}`}><span className={`grid h-7 w-7 place-items-center rounded-full ${active ? "bg-teal-300 text-[#071a1d]" : "bg-white/10"}`}>{number}</span>{text}</li>; }
