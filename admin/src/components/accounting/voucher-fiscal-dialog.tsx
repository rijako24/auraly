"use client";

import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { accountingApi, type SaveVoucherDraft, type VoucherDraftLineView, type VoucherFiscalAcquisition, type VoucherFiscalCapture, type VoucherFiscalPreview } from "@/services/api/accounting";
import { taxProfilesApi } from "@/services/api/tax-profiles";
import { taxationApi } from "@/services/api/taxation";
import { useAuthStore } from "@/stores/auth-store";
import { useReferenceOptions } from "@/hooks/use-reference-options";
import { WithholdingAdjustmentEditor } from "@/components/taxation/withholding-adjustment-editor";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { FormattedNumberInput } from "@/components/ui/formatted-number-input";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";

const noPermissions: string[] = [];
const money = new Intl.NumberFormat("es-CO", { style: "currency", currency: "COP", maximumFractionDigits: 4 });

// Mounted only while visible. This dialog owns the fiscal review; the parent
// owns the journal and invalidates the signature whenever that journal changes.
export function VoucherFiscalDialog({ businessId, draft, lines, onApply, onClose, onBusyChange }: {
  businessId: string; draft: SaveVoucherDraft; lines: VoucherDraftLineView[];
  onApply: (capture: VoucherFiscalCapture | null) => void; onClose: () => void;
  onBusyChange: (busy: boolean) => void;
}) {
  const permissions = useAuthStore(state => state.user?.permissions ?? noPermissions);
  const [capture, setCapture] = useState<VoucherFiscalCapture>(() => draft.fiscalCapture ?? { acquisitions: [] });
  const [preview, setPreview] = useState<VoucherFiscalPreview | null>(null);
  const [reviewed, setReviewed] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [sourceLineId, setSourceLineId] = useState("");
  const profiles = useQuery({ queryKey: ["tax-profiles", businessId, "active"], queryFn: () => taxProfilesApi.list(false), staleTime: 300_000 });
  const treatments = useReferenceOptions("purchase-tax-treatment");
  const canAdjust = permissions.includes("commerce.taxation.withholdings.manage");
  const rules = useQuery({ queryKey: ["withholding-rules", businessId, "active"], queryFn: () => taxationApi.listRules(false),
    enabled: permissions.includes("commerce.taxation.withholdings.view"), staleTime: 300_000 });
  const debitLines = lines.filter(line => line.value.lineId && line.value.accountId && line.value.debit > 0 && !line.value.credit);
  const creditLines = lines.filter(line => line.value.lineId && line.value.accountId && line.value.credit > 0 && !line.value.debit);
  const lineLabel = (id: string) => {
    const index = lines.findIndex(line => line.value.lineId === id);
    const line = lines[index];
    return line ? `${index + 1}. ${line.accountCode ?? "Sin cuenta"} · ${line.value.description}` : "Partida eliminada";
  };

  function change(patch: Partial<VoucherFiscalCapture>) {
    setCapture(current => ({ ...current, ...patch, calculationHash: null }));
    setReviewed(false); setError(null);
  }
  function changeAcquisition(id: string, patch: Partial<VoucherFiscalAcquisition>) {
    change({ acquisitions: capture.acquisitions.map(item => item.sourceLineId === id ? { ...item, ...patch } : item) });
  }
  async function review() {
    if (busy) return;
    setBusy(true); onBusyChange(true); setError(null); setReviewed(false);
    try {
      const result = await accountingApi.previewVoucherFiscal(businessId, { ...draft, lines: lines.map(line => line.value), fiscalCapture: capture });
      const bindings = (capture.withholdingBindings ?? []).filter(binding => result.withholding.lines.some(line => line.amount > 0 &&
        (line.manualLineId ? binding.manualLineId === line.manualLineId : binding.ruleId === line.ruleId)));
      const removedBindings = bindings.length !== (capture.withholdingBindings?.length ?? 0);
      setPreview(result); setReviewed(!removedBindings);
      setCapture(current => ({ ...current, withholdingBindings: removedBindings ? bindings : current.withholdingBindings,
        calculationHash: result.canConfirm && !removedBindings ? result.calculationHash : null }));
    } catch (value) { setError(value instanceof Error ? value.message : "No se pudo revisar el documento soporte."); }
    finally { setBusy(false); onBusyChange(false); }
  }

  return <Dialog open onOpenChange={open => { if (!open && !busy) onClose(); }}><DialogContent className="max-h-[calc(100dvh-2rem)] overflow-y-auto sm:max-w-4xl">
    <DialogHeader><DialogTitle>Datos fiscales del documento soporte</DialogTitle><DialogDescription>
      Vincula las bases, el IVA y las retenciones a las partidas que ya capturaste. Revisa el cálculo antes de contabilizar; guardar estos datos no genera deuda ni envía a DIAN.
    </DialogDescription></DialogHeader>
    {error && <p role="alert" className="rounded-xl border border-destructive p-3 text-sm text-destructive">{error}</p>}
    {profiles.isError && <p role="alert" className="text-sm text-destructive">No se pudieron cargar los perfiles de IVA. <Button variant="link" disabled={busy} onClick={() => void profiles.refetch()}>Reintentar perfiles</Button></p>}
    {treatments.isError && <p role="alert" className="text-sm text-destructive">No se pudieron cargar los tratamientos de IVA. <Button variant="link" disabled={busy} onClick={() => void treatments.refetch()}>Reintentar tratamientos</Button></p>}
    {rules.isError && <p role="alert" className="text-sm text-destructive">No se pudieron cargar las reglas de retención. <Button variant="link" disabled={busy} onClick={() => void rules.refetch()}>Reintentar reglas</Button></p>}
    <fieldset disabled={busy} className="space-y-4">
      <div className="flex flex-wrap items-end gap-3">
        <div className="min-w-0 flex-1 space-y-2"><Label>Partida de adquisición</Label><Select value={sourceLineId} onValueChange={setSourceLineId} disabled={busy}>
          <SelectTrigger aria-label="Partida para base fiscal"><SelectValue placeholder="Selecciona una partida débito" /></SelectTrigger><SelectContent>
            {debitLines.filter(line => !capture.acquisitions.some(item => item.sourceLineId === line.value.lineId)).map(line =>
              <SelectItem key={line.value.lineId} value={line.value.lineId!}>{lineLabel(line.value.lineId!)}</SelectItem>)}
          </SelectContent></Select></div>
        <Button variant="outline" disabled={busy || !sourceLineId} onClick={() => {
          change({ acquisitions: [...capture.acquisitions, { sourceLineId, taxExclusiveAmount: 0, taxProfileId: null, taxTreatment: "CapitalizedCost" }] });
          setSourceLineId("");
        }}>Agregar base fiscal</Button>
      </div>
      {capture.acquisitions.map((item, index) => <section key={item.sourceLineId} aria-label={`Base fiscal ${index + 1}`} className="space-y-3 rounded-xl border p-4">
        <div className="flex items-center justify-between gap-2"><h3 className="min-w-0 break-words text-sm font-semibold">{lineLabel(item.sourceLineId)}</h3>
          <Button variant="ghost" disabled={busy} onClick={() => change({ acquisitions: capture.acquisitions.filter(value => value.sourceLineId !== item.sourceLineId) })}>Quitar base</Button></div>
        <div className="grid gap-3 sm:grid-cols-2">
          <div className="space-y-2"><Label>Base sin IVA</Label><FormattedNumberInput kind="currency" ariaLabel={`Base sin IVA ${index + 1}`} value={item.taxExclusiveAmount} onValueChange={value => changeAcquisition(item.sourceLineId, { taxExclusiveAmount: value ?? 0 })} /></div>
          <div className="space-y-2"><Label>Perfil de IVA</Label><Select value={item.taxProfileId ?? "none"} disabled={busy || profiles.isPending || profiles.isError} onValueChange={value => changeAcquisition(item.sourceLineId, { taxProfileId: value === "none" ? null : value })}>
            <SelectTrigger aria-label={`Perfil de IVA ${index + 1}`}><SelectValue /></SelectTrigger><SelectContent><SelectItem value="none">Sin IVA</SelectItem>
              {profiles.data?.filter(profile => profile.dianTaxCode === "01").map(profile => <SelectItem key={profile.taxProfileId} value={profile.taxProfileId}>{profile.name} · {profile.rate}%</SelectItem>)}
            </SelectContent></Select></div>
          <div className="space-y-2"><Label>Tratamiento del IVA</Label><Select value={item.taxTreatment} disabled={busy || treatments.isPending || treatments.isError} onValueChange={value => changeAcquisition(item.sourceLineId, { taxTreatment: value as VoucherFiscalAcquisition["taxTreatment"], inputVatSourceLineId: null })}>
            <SelectTrigger aria-label={`Tratamiento del IVA ${index + 1}`}><SelectValue /></SelectTrigger><SelectContent>
              {treatments.data?.filter(option => option.code === "DeductibleInputVat" || option.code === "CapitalizedCost").map(option => <SelectItem key={option.id} value={option.code}>{option.label}</SelectItem>)}
            </SelectContent></Select></div>
          {item.taxTreatment === "DeductibleInputVat" && <div className="space-y-2"><Label>Partida de IVA descontable</Label><Select value={item.inputVatSourceLineId ?? "none"} disabled={busy} onValueChange={value => changeAcquisition(item.sourceLineId, { inputVatSourceLineId: value === "none" ? null : value })}>
            <SelectTrigger aria-label={`Partida de IVA ${index + 1}`}><SelectValue /></SelectTrigger><SelectContent><SelectItem value="none">Selecciona la partida</SelectItem>
              {debitLines.filter(line => line.value.lineId !== item.sourceLineId).map(line => <SelectItem key={line.value.lineId} value={line.value.lineId!}>{lineLabel(line.value.lineId!)}</SelectItem>)}
            </SelectContent></Select></div>}
          <div className="space-y-2"><Label htmlFor={`fiscal-concept-${index}`}>Código de concepto tributario (opcional)</Label><Input id={`fiscal-concept-${index}`} maxLength={32} value={item.withholdingConceptCode ?? ""} onChange={event => changeAcquisition(item.sourceLineId, { withholdingConceptCode: event.target.value || null })} /></div>
        </div>
      </section>)}
      {preview && <section aria-label="Revisión fiscal" className="space-y-3 rounded-xl border p-4">
        <p role="status" className="text-sm font-semibold">{reviewed ? preview.canConfirm ? "Revisión completa" : "Partidas por completar" : "Revisión pendiente: los datos cambiaron"}</p>
        <p className="text-sm">Adquisición con IVA {money.format(preview.withholding.grossAmount)} · Retenciones {money.format(preview.withholding.withholdingTotal)} · Cuenta por pagar {money.format(preview.withholding.netAmount)}</p>
        {preview.diagnostics.length > 0 && <ul className="list-disc space-y-1 pl-5 text-sm">{preview.diagnostics.map((diagnostic, index) => <li key={index}>{diagnostic}</li>)}</ul>}
        {preview.withholding.lines.filter(line => line.amount > 0).map(line => {
          const binding = capture.withholdingBindings?.find(value => line.manualLineId ? value.manualLineId === line.manualLineId : value.ruleId === line.ruleId);
          return <div key={line.manualLineId ?? line.ruleId} className="space-y-2"><Label>{line.name} · {money.format(line.amount)} · Partida crédito</Label>
            <Select value={binding?.sourceLineId ?? "none"} disabled={busy} onValueChange={value => change({ withholdingBindings: [
              ...(capture.withholdingBindings ?? []).filter(item => line.manualLineId ? item.manualLineId !== line.manualLineId : item.ruleId !== line.ruleId),
              ...(value === "none" ? [] : [{ sourceLineId: value, ruleId: line.manualLineId ? null : line.ruleId, manualLineId: line.manualLineId }]),
            ] })}><SelectTrigger aria-label={`Partida de retención ${line.name}`}><SelectValue /></SelectTrigger><SelectContent><SelectItem value="none">Selecciona la partida</SelectItem>
              {creditLines.map(value => <SelectItem key={value.value.lineId} value={value.value.lineId!}>{lineLabel(value.value.lineId!)}</SelectItem>)}
            </SelectContent></Select>
          </div>;
        })}
      </section>}
      {canAdjust && <WithholdingAdjustmentEditor businessId={businessId} occurredAt={draft.occurredAt} calculation={preview?.withholding ?? null}
        adjustments={capture.withholdingAdjustments ?? []} rules={rules.data ?? []} disabled={busy || rules.isError}
        onChange={withholdingAdjustments => change({ withholdingAdjustments })} />}
    </fieldset>
    <p className="text-xs text-muted-foreground">Puedes conservar los datos incompletos, corregir las partidas y revisar otra vez. El envío vuelve a validar el cálculo y la configuración vigente.</p>
    <DialogFooter><Button variant="outline" disabled={busy} onClick={onClose}>Cancelar</Button>
      <Button variant="outline" disabled={busy || !capture.acquisitions.length} onClick={() => void review()}>{busy ? "Revisando cálculo…" : "Revisar cálculo fiscal"}</Button>
      <Button disabled={busy} onClick={() => onApply(capture.acquisitions.length ? capture : null)}>Conservar datos fiscales</Button>
    </DialogFooter>
  </DialogContent></Dialog>;
}
