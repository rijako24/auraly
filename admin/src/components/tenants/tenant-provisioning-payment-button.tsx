"use client";

import Script from "next/script";
import { useState } from "react";
import { CreditCard, Loader2 } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { tenantCommercialApi, type ProvisionTenantRequest, type TenantProvisioningCheckoutStatus, type TenantQuoteRequest } from "@/services/api/tenants";

type WidgetResult = { transaction?: { id?: string } };
type WidgetOptions = {
  currency: string; amountInCents: number; reference: string; publicKey: string;
  signature: { integrity: string }; expirationTime?: string; redirectUrl?: string;
};
declare global { interface Window { WidgetCheckout?: new (options: WidgetOptions) => { open(callback: (result: WidgetResult) => void): void }; } }

export function TenantProvisioningPaymentButton({ tenant, quote, onComplete }: {
  tenant: ProvisionTenantRequest; quote: TenantQuoteRequest;
  onComplete: (status: TenantProvisioningCheckoutStatus) => void;
}) {
  const [ready, setReady] = useState(false);
  const [busy, setBusy] = useState(false);

  const pay = async () => {
    if (!window.WidgetCheckout) { toast.error("El checkout seguro todavía está cargando."); return; }
    setBusy(true);
    try {
      const checkout = await tenantCommercialApi.startCheckout(
        { ...tenant, provisioningRequestId: tenant.provisioningRequestId || crypto.randomUUID() },
        quote,
        `${window.location.origin}/register`);
      const options: WidgetOptions = {
        currency: checkout.widget.currency,
        amountInCents: checkout.widget.amountInCents,
        reference: checkout.widget.reference,
        publicKey: checkout.widget.publicKey,
        signature: { integrity: checkout.widget.integritySignature },
      };
      if (checkout.widget.expirationTime) options.expirationTime = checkout.widget.expirationTime;
      if (checkout.widget.redirectUrl) options.redirectUrl = checkout.widget.redirectUrl;
      const widget = new window.WidgetCheckout(options);
      widget.open(async (result) => {
        const transactionId = result.transaction?.id;
        if (!transactionId) { setBusy(false); return; }
        try {
          const status = await tenantCommercialApi.confirmCheckout(
            checkout.draftId, checkout.accessToken, transactionId);
          if (status.status === "Provisioned") onComplete(status);
          else await pollUntilProvisioned(checkout.draftId, checkout.accessToken, onComplete);
        } catch (error) {
          toast.error("Estamos validando tu pago", { description: error instanceof Error ? error.message : "Consulta de nuevo en unos segundos." });
        } finally { setBusy(false); }
      });
    } catch (error) {
      toast.error("No fue posible iniciar el pago", { description: error instanceof Error ? error.message : "Intenta nuevamente." });
      setBusy(false);
    }
  };

  return <><Script src="https://checkout.wompi.co/widget.js" strategy="afterInteractive" onLoad={() => setReady(true)}/><Button disabled={!ready || busy} onClick={pay}>{busy ? <><Loader2 className="mr-2 h-4 w-4 animate-spin"/>Validando pago…</> : <><CreditCard className="mr-2 h-4 w-4"/>Pagar y crear empresa</>}</Button></>;
}

async function pollUntilProvisioned(draftId: string, accessToken: string,
  onComplete: (status: TenantProvisioningCheckoutStatus) => void) {
  for (let attempt = 0; attempt < 10; attempt += 1) {
    await new Promise((resolve) => setTimeout(resolve, 1500));
    const status = await tenantCommercialApi.checkoutStatus(draftId, accessToken);
    if (status.status === "Provisioned") { onComplete(status); return; }
    if (status.status === "Failed") throw new Error(status.errorMessage || "El aprovisionamiento requiere revisión.");
  }
  throw new Error("El pago fue recibido y el aprovisionamiento continúa en segundo plano.");
}
