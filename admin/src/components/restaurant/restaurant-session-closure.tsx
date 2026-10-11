"use client";

import { useCallback, useRef, useState } from "react";
import { useRouter } from "next/navigation";
import { Banknote } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { PosCashClosureDialog } from "@/app/(pos)/pos/pos-cash-closure-dialog";
import { PosSupervisorApprovalDialog } from "@/app/(pos)/pos/pos-supervisor-approval-dialog";
import { forgetSalesWorkspace, OnlinePosClient } from "@/services/pos/online-pos-client";
import { posApprovalClient, type PosApprovalRequest } from "@/services/pos/pos-approval-client";
import type { PosAuthorizedClosurePreview, PosSensitiveAuthorization,
  PosWorkSessionPaymentCount } from "@/services/pos/pos-edge-client";

type Approval = {
  request: PosApprovalRequest;
  operationId: string;
  execute: (authorization: PosSensitiveAuthorization) => Promise<void>;
};

export function RestaurantSessionClosure({ client, workSessionId, businessId,
  businessName, warehouseName, canClose }: {
  client: OnlinePosClient;
  workSessionId: string;
  businessId: string;
  businessName: string;
  warehouseName: string;
  canClose: boolean;
}) {
  const router = useRouter();
  const [busy, setBusy] = useState(false);
  const [preview, setPreview] = useState<PosAuthorizedClosurePreview | null>(null);
  const [approval, setApproval] = useState<Approval | null>(null);
  const [approvalError, setApprovalError] = useState<string | null>(null);
  const [attempt, setAttempt] = useState<{ counts: PosWorkSessionPaymentCount[]; note: string | null } | null>(null);
  const authorization = useRef<PosSensitiveAuthorization | null>(null);
  const operationId = useRef("");

  const authorize = useCallback(async (
    execute: (value: PosSensitiveAuthorization) => Promise<void>,
  ) => {
    const id = operationId.current || crypto.randomUUID();
    operationId.current = id;
    if (canClose) {
      await execute({ operationId: id });
      return;
    }
    const request = await client.createApproval({
      businessId, deviceId: null, workSessionId, draftId: workSessionId,
      lineId: null, permissionResource: "work-sessions.close",
      contextJson: JSON.stringify({ action: preview ? "ConfirmWorkSessionClosure" :
        "OpenWorkSessionClosure", workSessionId, businessName, warehouseName }),
    });
    setApproval({ request, operationId: id, execute });
    setApprovalError(null);
  }, [businessId, businessName, canClose, client, preview, warehouseName, workSessionId]);

  async function openClosure() {
    if (busy) return;
    setBusy(true);
    try {
      operationId.current = crypto.randomUUID();
      await authorize(async (value) => {
        const result = await client.previewWorkSessionClosure(workSessionId, value);
        authorization.current = value;
        setPreview(result);
      });
    } catch (error) {
      toast.error(error instanceof Error ? error.message : "No fue posible preparar el cierre.");
    } finally { setBusy(false); }
  }

  async function confirm(counts: PosWorkSessionPaymentCount[], note: string | null) {
    if (!preview || busy) return;
    const submitted = attempt ?? { counts, note };
    setAttempt(submitted);
    setBusy(true);
    try {
      const finish = async (value: PosSensitiveAuthorization) => {
        await client.closeWorkSession({
          operationId: operationId.current,
          authorizationToken: preview.authorizationToken,
          draftId: workSessionId,
          authorization: value,
          countedCash: submitted.counts.find((item) => item.paymentMethodCode === "Cash")?.countedAmount ?? 0,
          paymentCounts: submitted.counts,
          note: submitted.note,
        });
        forgetSalesWorkspace();
        router.push("/dashboard");
      };
      const current = authorization.current;
      if (canClose || (current?.approvalRequestId && current.expiresAt &&
          Date.parse(current.expiresAt) > Date.now()))
        await finish(current ?? { operationId: operationId.current });
      else await authorize(finish);
    } catch (error) {
      toast.error(error instanceof Error ? error.message : "No fue posible cerrar la sesión.");
    } finally { setBusy(false); }
  }

  async function completeApproval(approvalRequestId: string) {
    if (!approval) return;
    const pending = approval;
    setApproval(null);
    setBusy(true);
    try {
      const value = { operationId: pending.operationId, approvalRequestId,
        expiresAt: pending.request.expiresAt };
      await pending.execute(value);
      authorization.current = value;
    } catch (error) {
      toast.error(error instanceof Error ? error.message : "No se completó la acción autorizada.");
    } finally { setBusy(false); }
  }

  return <>
    <Button variant="outline" size="sm" disabled={busy} onClick={() => void openClosure()}
      className="min-h-10 gap-2 rounded-xl" title="Cerrar sesión de venta">
      <Banknote className="h-4 w-4"/><span className="hidden sm:inline">Cerrar sesión de venta</span>
    </Button>
    {preview && <PosCashClosureDialog value={preview} busy={busy}
      submitted={Boolean(attempt)} onConfirm={confirm}
      onClose={() => { setPreview(null); setAttempt(null); authorization.current = null;
        operationId.current = ""; }} />}
    {approval && <PosSupervisorApprovalDialog approval={approval.request} allowRemote
      busy={busy} error={approvalError}
      loadApproval={(id) => client.approval(id) as Promise<PosApprovalRequest>}
      subscribeApprovals={(changed) => posApprovalClient.subscribe(changed)}
      onRemoteApproved={completeApproval}
      onLocalSecret={async (secret) => {
        try {
          await posApprovalClient.authorizeLocally(approval.request.approvalRequestId, secret);
          await completeApproval(approval.request.approvalRequestId);
        } catch (error) {
          setApprovalError(error instanceof Error ? error.message : "Clave no autorizada.");
        }
      }}
      onRetry={async () => { const pending = approval;
        try { await authorize(pending.execute); }
        catch (error) { setApprovalError(error instanceof Error ? error.message :
          "No fue posible solicitar la autorización."); } }}
      onCancel={() => { setApproval(null); setApprovalError(null);
        if (!preview) operationId.current = ""; }} />}
  </>;
}
