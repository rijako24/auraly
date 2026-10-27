"use client";

import { useState } from "react";
import { accountingApi, type AccountingOpenPosition, type VoucherDraftPreparation } from "@/services/api/accounting";
import { PartySelect } from "@/components/parties/party-role-select";
import { PagedEntitySelect } from "@/components/forms/paged-entity-select";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Label } from "@/components/ui/label";
import { AccountingDocumentDialog } from "./accounting-document-dialog";

const money = new Intl.NumberFormat("es-CO", { style: "currency", currency: "COP", maximumFractionDigits: 4 });

export function VoucherPositionDialog({ businessId, documentId, occurredAt, mainPartyId, mainPartyName,
  mainPartyIdentification, onPrepared, onClose }: {
  businessId: string; documentId: string; occurredAt: string;
  mainPartyId?: string | null; mainPartyName?: string | null; mainPartyIdentification?: string | null;
  onPrepared: (value: VoucherDraftPreparation) => void; onClose: () => void;
}) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [party, setParty] = useState({ id: mainPartyId ?? "", name: mainPartyName ?? null,
    identification: mainPartyIdentification ?? null });
  const [position, setPosition] = useState<AccountingOpenPosition | null>(null);
  const [originOpen, setOriginOpen] = useState(false);

  async function prepare() {
    if (busy || !position) return;
    setBusy(true); setError(null);
    try { onPrepared(await accountingApi.supportCancellationCapture(position.subledgerId)); onClose(); }
    catch (failure) { setError(failure instanceof Error ? failure.message : "No se pudo preparar la anulación del soporte."); }
    finally { setBusy(false); }
  }

  return <Dialog open onOpenChange={open => { if (!open && !busy) onClose(); }}>
    <DialogContent className="max-h-[calc(100dvh-2rem)] overflow-y-auto sm:max-w-2xl">
      <DialogHeader><DialogTitle>Seleccionar soporte original</DialogTitle>
        <DialogDescription>Se prepararán las partidas inversas del soporte seleccionado. Revisar y guardar no lo anulan.</DialogDescription></DialogHeader>
      {error && <p role="alert" className="text-sm text-destructive">{error}</p>}
      <div className="space-y-2"><Label>Tercero</Label><PartySelect value={party.id} ariaLabel="Tercero del soporte original"
        selectedOption={party.id && party.name ? { value: party.id, label: party.name, description: party.identification } : null}
        onChange={(id, item) => { setParty({ id, name: item?.displayName ?? null, identification: item?.identification ?? null }); setPosition(null); }} /></div>
      <div className="space-y-2"><Label>Soporte</Label><PagedEntitySelect<AccountingOpenPosition>
        queryKey={["voucher-support-cancellation-options", businessId, documentId, party.id, occurredAt.slice(0, 10)]}
        value={position?.subledgerId ?? ""}
        selectedOption={position ? { value: position.subledgerId, label: position.documentNumber } : null}
        disabled={!party.id || busy} ariaLabel="Soporte original para anular" placeholder="Buscar soporte del proveedor"
        searchPlaceholder="Buscar número o referencia…" emptyMessage="No hay soportes disponibles para este tercero."
        loadPage={async (search, page, pageSize) => {
          const result = await accountingApi.openPositions({ subledgerKind: "Payable", partyId: party.id, search, page, pageSize,
            manualSupportOnly: true, effectiveOn: occurredAt.slice(0, 10) });
          return { ...result, totalPages: Math.ceil(result.totalCount / result.pageSize) };
        }} getOption={item => ({ value: item.subledgerId, label: item.documentNumber,
          description: `${item.accountCode ?? "Sin cuenta conciliada"} · ${item.partyName} · Disponible ${money.format(item.availableAmount)}` })}
        onChange={(_id, _option, item) => setPosition(item ?? null)} /></div>
      {position && <div className="rounded-xl border bg-muted/30 p-4 text-sm">
        <p><b>Cuenta:</b> {position.accountCode} · {position.accountName}</p>
        <p><b>Tercero:</b> {position.partyName} · {position.partyIdentification}</p>
        <p><b>Disponible:</b> {money.format(position.availableAmount)}</p>
        <Button variant="link" size="sm" onClick={() => setOriginOpen(true)}>Ver comprobante de origen</Button>
      </div>}
      <DialogFooter><Button variant="outline" disabled={busy} onClick={onClose}>Cancelar</Button>
        <Button disabled={!position || busy} onClick={() => void prepare()}>{busy ? "Preparando partidas…" : "Preparar anulación"}</Button></DialogFooter>
      {originOpen && position && <AccountingDocumentDialog documentId={position.sourceDocumentId} hasFiscalDocument onClose={() => setOriginOpen(false)} />}
    </DialogContent>
  </Dialog>;
}
