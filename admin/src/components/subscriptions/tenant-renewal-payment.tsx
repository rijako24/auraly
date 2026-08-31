"use client";

import Script from "next/script";
import { useState } from "react";
import { CreditCard, ExternalLink, Loader2, Printer, ReceiptText } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { tenantCommercialApi, type TenantSubscriptionReceipt } from "@/services/api/tenants";

type WidgetResult = { transaction?: { id?: string } };
type WidgetOptions = { currency: string; amountInCents: number; reference: string; publicKey: string;
  signature: { integrity: string }; expirationTime?: string; redirectUrl?: string };
type WompiWindow = Window & { WidgetCheckout?: new (options: WidgetOptions) =>
  { open(callback: (result: WidgetResult) => void): void } };

const money = new Intl.NumberFormat("es-CO", { style: "currency", currency: "COP", maximumFractionDigits: 0 });
const date = new Intl.DateTimeFormat("es-CO", { dateStyle: "medium" });

export function TenantRenewalPayment({ orderId, status }: { orderId: string; status: string }) {
  const [ready, setReady] = useState(false);
  const [busy, setBusy] = useState(false);
  const [receipt, setReceipt] = useState<TenantSubscriptionReceipt | null>(null);

  const pay = async () => {
    const Widget = (window as WompiWindow).WidgetCheckout;
    if (!Widget) return toast.error("El checkout seguro todavía está cargando.");
    setBusy(true);
    try {
      const checkout = await tenantCommercialApi.startRenewalCheckout(window.location.href);
      const value = checkout.widget;
      const options: WidgetOptions = { currency: value.currency, amountInCents: value.amountInCents,
        reference: value.reference, publicKey: value.publicKey, signature: { integrity: value.integritySignature } };
      if (value.expirationTime) options.expirationTime = value.expirationTime;
      if (value.redirectUrl) options.redirectUrl = value.redirectUrl;
      new Widget(options).open(async result => {
        const transactionId = result.transaction?.id;
        if (!transactionId) { setBusy(false); return; }
        try {
          const paid = await tenantCommercialApi.confirmRenewalCheckout(checkout.renewalOrderId, transactionId);
          setReceipt(paid);
          toast.success("Renovación pagada y facturada");
        } catch (error) {
          toast.error("Estamos validando el pago", { description: error instanceof Error ? error.message : "Consulta de nuevo en unos segundos." });
        } finally { setBusy(false); }
      });
    } catch (error) {
      toast.error("No fue posible iniciar el pago", { description: error instanceof Error ? error.message : "Intenta nuevamente." });
      setBusy(false);
    }
  };

  const showExisting = async () => {
    setBusy(true);
    try { setReceipt(await tenantCommercialApi.renewalReceipt(orderId)); }
    catch (error) { toast.error(error instanceof Error ? error.message : "La factura aún no está disponible."); }
    finally { setBusy(false); }
  };

  return <>
    <Script src="https://checkout.wompi.co/widget.js" strategy="afterInteractive" onLoad={() => setReady(true)} />
    {status === "Draft" || status === "PendingPayment" ? <Button disabled={!ready || busy} onClick={pay}>
      {busy ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : <CreditCard className="mr-2 h-4 w-4" />}
      {status === "PendingPayment" ? "Retomar pago pendiente" : "Pagar renovación"}
    </Button> : <Button variant="outline" disabled={busy} onClick={showExisting}>
      <ReceiptText className="mr-2 h-4 w-4" />Ver factura y ticket
    </Button>}
    <ReceiptDialog receipt={receipt} onClose={() => setReceipt(null)} />
  </>;
}

function ReceiptDialog({ receipt, onClose }: { receipt: TenantSubscriptionReceipt | null; onClose: () => void }) {
  if (!receipt) return null;
  return <Dialog open onOpenChange={open => !open && onClose()}><DialogContent className="max-h-[92dvh] max-w-md overflow-y-auto rounded-3xl">
    <DialogHeader><DialogTitle>Pago confirmado</DialogTitle><DialogDescription>Tu renovación quedó activa y su factura fue emitida.</DialogDescription></DialogHeader>
    <div className="rounded-[1.75rem] border bg-white p-6 font-mono text-[12px] text-slate-900 shadow-sm">
      <div className="border-b border-dashed pb-4 text-center"><strong className="block text-lg tracking-[.22em]">AURALY</strong><span>Servicios de software</span><small className="mt-2 block">FACTURA {receipt.documentNumber}</small></div>
      <div className="space-y-1 border-b border-dashed py-4"><Row label="Fecha" value={date.format(new Date(receipt.issuedAt))}/><Row label="Periodo" value={`${date.format(new Date(receipt.periodStart))} — ${date.format(new Date(receipt.periodEnd))}`}/><Row label="Pago" value={receipt.paymentMethod}/><Row label="Referencia" value={receipt.paymentReference}/></div>
      <div className="space-y-3 border-b border-dashed py-4">{receipt.lines.map(line => <div key={line.code}><div className="flex justify-between gap-3"><strong>{line.name}</strong><strong>{money.format(line.totalAmount)}</strong></div><small>{line.quantity} × {money.format(line.unitPrice)} · IVA {line.taxRate}%</small></div>)}</div>
      <div className="space-y-1 py-4"><Row label="Subtotal" value={money.format(receipt.subtotal)}/><Row label="IVA" value={money.format(receipt.taxAmount)}/><div className="mt-2 flex justify-between border-t pt-2 text-base font-bold"><span>TOTAL</span><span>{money.format(receipt.totalAmount)}</span></div></div>
      <p className="border-t border-dashed pt-4 text-center text-[10px]">Estado DIAN: {receipt.fiscalStatus}{receipt.cufe && <><br/>CUFE {receipt.cufe}</>}</p>
    </div>
    <div className="grid grid-cols-2 gap-2"><Button variant="outline" onClick={() => openInvoiceReport(receipt)}><ExternalLink className="mr-2 h-4 w-4"/>Ver factura</Button><Button variant="outline" onClick={() => printReceipt(receipt)}><Printer className="mr-2 h-4 w-4"/>Imprimir ticket</Button></div>
  </DialogContent></Dialog>;
}

function Row({ label, value }: { label: string; value: string }) { return <div className="flex justify-between gap-4"><span>{label}</span><span className="text-right">{value}</span></div>; }

function printReceipt(receipt: TenantSubscriptionReceipt) {
  const popup = window.open("", "_blank", "width=420,height=720");
  if (!popup) return toast.error("Permite las ventanas emergentes para imprimir.");
  const lineHtml = receipt.lines.map(line => `<tr><td>${escapeHtml(line.name)}<small>${line.quantity} × ${money.format(line.unitPrice)} · IVA ${line.taxRate}%</small></td><td>${money.format(line.totalAmount)}</td></tr>`).join("");
  popup.document.write(`<!doctype html><html><head><title>${escapeHtml(receipt.documentNumber)}</title><style>body{font:12px ui-monospace,monospace;width:76mm;margin:8mm auto;color:#111}h1,p{text-align:center}hr{border:0;border-top:1px dashed #555}table{width:100%;border-collapse:collapse}td{padding:7px 0;vertical-align:top}td:last-child{text-align:right}small{display:block;color:#555}.total{font-size:17px;font-weight:800}</style></head><body><h1>AURALY</h1><p>Servicios de software<br>FACTURA ${escapeHtml(receipt.documentNumber)}</p><hr><p>${date.format(new Date(receipt.periodStart))} — ${date.format(new Date(receipt.periodEnd))}<br>Wompi · ${escapeHtml(receipt.paymentReference)}</p><hr><table>${lineHtml}</table><hr><table><tr><td>Subtotal</td><td>${money.format(receipt.subtotal)}</td></tr><tr><td>IVA</td><td>${money.format(receipt.taxAmount)}</td></tr><tr class="total"><td>TOTAL</td><td>${money.format(receipt.totalAmount)}</td></tr></table><hr><p>Estado DIAN: ${escapeHtml(receipt.fiscalStatus)}</p><script>window.onload=()=>{window.print();window.onafterprint=()=>window.close()}<\/script></body></html>`);
  popup.document.close();
}

function openInvoiceReport(receipt: TenantSubscriptionReceipt) {
  const popup = window.open("", "_blank");
  if (!popup) return toast.error("Permite las ventanas emergentes para ver la factura.");
  const rows = receipt.lines.map(line => `<tr><td>${escapeHtml(line.code)}</td><td>${escapeHtml(line.name)}</td><td>${line.quantity}</td><td>${money.format(line.unitPrice)}</td><td>${line.taxRate}%</td><td>${money.format(line.totalAmount)}</td></tr>`).join("");
  popup.document.write(`<!doctype html><html><head><title>Factura ${escapeHtml(receipt.documentNumber)}</title><style>body{font:14px Arial,sans-serif;max-width:960px;margin:35px auto;padding:25px;color:#14202b}header{display:flex;justify-content:space-between;border-bottom:3px solid #0faaa5;padding-bottom:20px}h1{margin:0;color:#063b48}.meta{text-align:right}table{width:100%;border-collapse:collapse;margin-top:28px}th,td{padding:11px 8px;border-bottom:1px solid #dbe3e8;text-align:right}th:nth-child(-n+2),td:nth-child(-n+2){text-align:left}.totals{margin:28px 0 0 auto;width:320px}.totals div{display:flex;justify-content:space-between;padding:7px}.total{background:#063b48;color:white;font-size:18px;font-weight:bold}.cufe{margin-top:40px;padding:16px;background:#f2f7f7;overflow-wrap:anywhere;font-size:11px}@media print{button{display:none}}</style></head><body><header><div><h1>AURALY</h1><p>Servicios de software</p></div><div class="meta"><strong>FACTURA ELECTRÓNICA</strong><br>${escapeHtml(receipt.documentNumber)}<br>${date.format(new Date(receipt.issuedAt))}</div></header><p><strong>Periodo facturado:</strong> ${date.format(new Date(receipt.periodStart))} — ${date.format(new Date(receipt.periodEnd))}<br><strong>Pago:</strong> Wompi · ${escapeHtml(receipt.paymentReference)}</p><table><thead><tr><th>Código</th><th>Servicio</th><th>Cant.</th><th>Valor</th><th>IVA</th><th>Total</th></tr></thead><tbody>${rows}</tbody></table><section class="totals"><div><span>Subtotal</span><strong>${money.format(receipt.subtotal)}</strong></div><div><span>IVA</span><strong>${money.format(receipt.taxAmount)}</strong></div><div class="total"><span>Total</span><span>${money.format(receipt.totalAmount)}</span></div></section><p class="cufe"><strong>Estado DIAN:</strong> ${escapeHtml(receipt.fiscalStatus)}<br><strong>CUFE:</strong> ${escapeHtml(receipt.cufe ?? "En proceso de validación")}</p><button onclick="window.print()">Imprimir / guardar PDF</button></body></html>`);
  popup.document.close();
}

function escapeHtml(value: string) { return value.replace(/[&<>'"]/g, character => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", "'": "&#39;", '"': "&quot;" }[character]!)); }
