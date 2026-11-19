"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { CheckCircle2, CircleAlert, Clock3, FileCheck2, Loader2 } from "lucide-react";
import { accountingApi, type AccountingPosting } from "@/services/api/accounting";
import { fiscalDocumentsApi, type FiscalDocumentView } from "@/services/api/fiscal-documents";
import { accountingStatusLabel, fiscalStatusLabel } from "@/lib/accounting-labels";
import { useAuthStore } from "@/stores/auth-store";
import { useBusinessContextStore } from "@/stores/business-context-store";
import { useTenantContextStore } from "@/stores/tenant-context-store";
import { Button } from "@/components/ui/button";
import { Tooltip, TooltipContent, TooltipProvider, TooltipTrigger } from "@/components/ui/tooltip";

export type DocumentProcessingUpdate = {
  accountingStatus?: string; accountingErrorMessage?: string | null;
  fiscalStatus?: string; fiscalErrorMessage?: string | null;
};

// Page owners supply the snapshot; rows never fetch their own status. Commands
// return the authoritative stage and never imply DIAN acceptance or posting.
export function DocumentProcessingActions({ documentId, accountingStatus, accountingErrorMessage,
  fiscalStatus, fiscalErrorMessage, onChanged, onSendDraft, sendingDraft = false }: {
  documentId: string; accountingStatus?: string | null; accountingErrorMessage?: string | null;
  fiscalStatus?: string | null; fiscalErrorMessage?: string | null;
  onChanged: (value: DocumentProcessingUpdate) => void;
  onSendDraft?: () => void; sendingDraft?: boolean;
}) {
  const userPermissions = useAuthStore(state => state.user?.permissions);
  const permissions = userPermissions ?? [];
  const tenantId = useTenantContextStore(state => state.selectedTenantId);
  const businessId = useBusinessContextStore(state => state.selectedBusinessId);
  const client = useQueryClient();
  const retryPosting = useMutation({ mutationFn: () => accountingApi.retryPosting(documentId),
    onSuccess: (value: AccountingPosting) => {
      client.setQueryData(["accounting-posting", tenantId, businessId, documentId], value);
      onChanged({ accountingStatus: value.status, accountingErrorMessage: value.errorMessage });
    } });
  const fiscal = useMutation({ mutationFn: () => fiscalStatus === "AwaitingManualSubmission"
    ? fiscalDocumentsApi.sendManualSupport(documentId) : fiscalDocumentsApi.retry(documentId),
    onSuccess: (value: FiscalDocumentView) => {
      client.setQueryData(["fiscal-document", tenantId, businessId, documentId], value);
      onChanged({ fiscalStatus: value.status, fiscalErrorMessage: value.lastStatusDescription });
    } });
  const posted = accountingStatus === "Posted";
  const noPosting = accountingStatus === "CommercialEffectsApplied" || accountingStatus === "AccountingDisabled" || accountingStatus === "NotRequired";
  const postingBusy = accountingStatus === "Pending" || accountingStatus === "Processing" || accountingStatus === "RetryScheduled";
  const canRetryPosting = ["AccountingPendingConfiguration", "NeedsIntervention", "DeadLettered", "Failed"].includes(accountingStatus ?? "") && permissions.includes("accounting.postings.retry");
  const accepted = fiscalStatus === "DianAccepted";
  const fiscalFailure = ["SchemaValidationFailed", "SignatureFailed", "PermanentFailure", "DianRejected", "FiscalIntegrityConflict"].includes(fiscalStatus ?? "");
  const fiscalBlocked = fiscalStatus === "BlockedByQuota" || fiscalStatus === "MissingMandatoryFiscalData";
  const awaiting = fiscalStatus === "AwaitingManualSubmission";
  const canSend = awaiting ? posted && permissions.includes("fiscal.manual-support.send")
    : (fiscalFailure || fiscalStatus === "PendingDianResult" || fiscalStatus === "RetryScheduled") && permissions.includes("fiscal.retry");
  const postingError = retryPosting.error?.message ?? accountingErrorMessage;
  const fiscalError = fiscal.error?.message ?? fiscalErrorMessage;
  const processingClass = "text-amber-700 hover:text-amber-800";
  const completeClass = "text-emerald-700 hover:text-emerald-800";
  const errorClass = "text-red-700 hover:text-red-800";
  return <TooltipProvider><div className="flex items-center gap-1" onClick={event => event.stopPropagation()} onKeyDown={event => event.stopPropagation()}>
    {accountingStatus && <StatusAction label={`Contabilidad: ${accountingStatusLabel(accountingStatus)}`}
      description={[postingError, onSendDraft ? "Contabilizar comprobante" : canRetryPosting ? "Reintentar contabilización" : null].filter(Boolean).join(" · ")}
      className={posted ? completeClass : noPosting ? "text-muted-foreground" : postingBusy ? processingClass : errorClass}
      busy={retryPosting.isPending || sendingDraft} onClick={onSendDraft ?? (canRetryPosting ? () => retryPosting.mutate() : undefined)}
      icon={posted ? CheckCircle2 : postingBusy ? Clock3 : CircleAlert} />}
    {fiscalStatus && <StatusAction label={`DIAN: ${fiscalStatusLabel(fiscalStatus)}`}
      description={[fiscalError, accepted ? "Documento aceptado; no se vuelve a enviar." : canSend ? awaiting ? "Enviar a la DIAN" : "Reintentar proceso fiscal" : awaiting ? "Pendiente de envío autorizado." : fiscalBlocked || fiscalFailure ? "Revisa el documento fiscal antes de continuar." : "El motor fiscal procesa este documento."].filter(Boolean).join(" · ")}
      className={accepted ? completeClass : fiscalFailure || fiscalBlocked || awaiting ? errorClass : processingClass}
      busy={fiscal.isPending} onClick={canSend ? () => fiscal.mutate() : undefined}
      icon={accepted ? FileCheck2 : fiscalFailure || fiscalBlocked || awaiting ? CircleAlert : Clock3} />}
    {!accountingStatus && !fiscalStatus && <span className="text-muted-foreground">—</span>}
    {(retryPosting.isError || fiscal.isError) && <span role="alert" className="max-w-48 text-xs text-destructive">{retryPosting.error?.message ?? fiscal.error?.message}</span>}
  </div></TooltipProvider>;
}

function StatusAction({ label, description, className, busy, onClick, icon: Icon }: {
  label: string; description: string; className: string; busy: boolean; onClick?: () => void;
  icon: typeof CheckCircle2;
}) {
  return <Tooltip><TooltipTrigger asChild><Button type="button" variant="ghost" size="icon"
    className={className} aria-label={label} aria-disabled={!onClick || busy} title={`${label}${description ? ` · ${description}` : ""}`}
    onClick={onClick && !busy ? onClick : undefined}>
    {busy ? <Loader2 className="h-4 w-4 animate-spin" /> : <Icon className="h-4 w-4" />}
  </Button></TooltipTrigger><TooltipContent className="max-w-sm"><p>{label}</p>{description && <p>{description}</p>}</TooltipContent></Tooltip>;
}
