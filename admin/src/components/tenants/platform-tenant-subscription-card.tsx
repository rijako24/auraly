"use client";

import { useCallback, useEffect, useState } from "react";
import { CreditCard, FileCheck2, Loader2 } from "lucide-react";
import { toast } from "sonner";

import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Textarea } from "@/components/ui/textarea";
import { tenantsApi, type TenantCommercialSubscription, type TenantRenewalOrder, type TenantSubscriptionReceipt } from "@/services/api/tenants";
import { useAuthStore } from "@/stores/auth-store";

const money = new Intl.NumberFormat("es-CO", { style: "currency", currency: "COP", maximumFractionDigits: 0 });

export function PlatformTenantSubscriptionCard({ tenantId }: { tenantId: string }) {
  const canRecord = useAuthStore(state => state.user?.permissions.includes("tenants.billing.payment.confirm_manual") ?? false);
  const [subscription, setSubscription] = useState<TenantCommercialSubscription | null>(null);
  const [order, setOrder] = useState<TenantRenewalOrder | null>(null);
  const [receipt, setReceipt] = useState<TenantSubscriptionReceipt | null>(null);
  const [loading, setLoading] = useState(true);
  const [open, setOpen] = useState(false);
  const [saving, setSaving] = useState(false);
  const [method, setMethod] = useState<"Cash" | "Transfer" | "DebitCard" | "CreditCard">("Transfer");
  const [reference, setReference] = useState("");
  const [paidAt, setPaidAt] = useState(() => localDateTime(new Date()));
  const [note, setNote] = useState("");

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const [subscriptionValue, orderValue] = await Promise.all([
        tenantsApi.subscription(tenantId), tenantsApi.renewalOrder(tenantId),
      ]);
      setSubscription(subscriptionValue);
      setOrder(orderValue);
    } catch (error) {
      toast.error(error instanceof Error ? error.message : "No fue posible consultar la suscripción.");
    } finally { setLoading(false); }
  }, [tenantId]);

  useEffect(() => { void load(); }, [load]);

  async function recordPayment() {
    if (!order || reference.trim().length < 3) {
      toast.error("Ingresa la referencia bancaria o del comprobante.");
      return;
    }
    setSaving(true);
    try {
      const value = await tenantsApi.recordSubscriptionPayment(tenantId, order.renewalOrderId, {
        paymentMethodCode: method,
        reference: reference.trim(),
        paidAt: new Date(paidAt).toISOString(),
        note: note.trim() || null,
      });
      setReceipt(value);
      setOpen(false);
      toast.success(`Recaudo aplicado. Factura ${value.documentNumber} generada.`);
      await load();
    } catch (error) {
      toast.error(error instanceof Error ? error.message : "No fue posible aplicar el recaudo.");
    } finally { setSaving(false); }
  }

  if (loading) return <section className="rounded-2xl border bg-card p-6 text-sm text-muted-foreground">Consultando suscripción…</section>;
  if (!subscription) return <section className="rounded-2xl border bg-card p-6 shadow-sm">
    <p className="text-xs font-semibold uppercase tracking-wide text-primary">Suscripción</p>
    <h2 className="mt-1 text-xl font-semibold">Sin suscripción comercial</h2>
    <p className="mt-1 text-sm text-muted-foreground">Esta empresa fue creada antes del modelo comercial o todavía no tiene un plan asignado.</p>
  </section>;
  const payable = order?.status === "Draft" || order?.status === "PendingPayment";

  return <>
    <section className="rounded-2xl border bg-card p-6 shadow-sm">
      <div className="flex flex-col justify-between gap-4 sm:flex-row sm:items-start">
        <div><p className="text-xs font-semibold uppercase tracking-wide text-primary">Suscripción</p><h2 className="mt-1 text-xl font-semibold">{subscription.planName}</h2><p className="mt-1 text-sm text-muted-foreground">{subscription.billingPeriod === "Annual" ? "Cobro anual" : "Cobro mensual"} · vence {new Date(subscription.currentPeriodEnd).toLocaleDateString("es-CO")}</p></div>
        <div className="flex items-center gap-2"><Badge variant={subscription.status === "Active" ? "default" : "destructive"}>{subscription.status}</Badge>{canRecord && payable && <Button onClick={() => setOpen(true)}><CreditCard className="mr-2 h-4 w-4" />Registrar recaudo</Button>}</div>
      </div>
      {order && <div className="mt-5 grid gap-3 rounded-xl bg-muted/50 p-4 sm:grid-cols-3"><Value label="Orden vigente" value={`Rev. ${order.revision} · ${order.status}`} /><Value label="Periodo" value={`${new Date(order.targetPeriodStart).toLocaleDateString("es-CO")} – ${new Date(order.targetPeriodEnd).toLocaleDateString("es-CO")}`} /><Value label="Total exacto" value={money.format(order.quote.payableAmountCop)} /></div>}
      {receipt && <div className="mt-4 flex items-center gap-3 rounded-xl border border-emerald-200 bg-emerald-50 p-4 text-sm text-emerald-900"><FileCheck2 className="h-5 w-5" /><div><strong>Factura {receipt.documentNumber}</strong><p>{receipt.paymentMethod} · {receipt.paymentReference} · {money.format(receipt.totalAmount)}</p></div></div>}
    </section>

    <Dialog open={open} onOpenChange={setOpen}><DialogContent><DialogHeader><DialogTitle>Registrar pago recibido</DialogTitle><DialogDescription>Esto sustituye cualquier intento Wompi pendiente y ejecuta el mismo settlement: activa la suscripción y genera la factura electrónica. El valor no se puede editar.</DialogDescription></DialogHeader>
      <div className="grid gap-4 py-2">
        <div className="rounded-xl border bg-muted/40 p-4"><p className="text-xs text-muted-foreground">Total de la orden</p><p className="text-2xl font-semibold">{money.format(order?.quote.payableAmountCop ?? 0)}</p></div>
        <div className="grid gap-2"><Label>Medio de pago</Label><Select value={method} onValueChange={value => setMethod(value as typeof method)}><SelectTrigger aria-label="Medio de pago manual"><SelectValue /></SelectTrigger><SelectContent><SelectItem value="Transfer">Transferencia o consignación</SelectItem><SelectItem value="Cash">Efectivo</SelectItem><SelectItem value="DebitCard">Tarjeta débito</SelectItem><SelectItem value="CreditCard">Tarjeta crédito</SelectItem></SelectContent></Select></div>
        <div className="grid gap-2"><Label htmlFor="manual-reference">Referencia</Label><Input id="manual-reference" value={reference} maxLength={160} onChange={event => setReference(event.target.value)} placeholder="Número de consignación o comprobante" /></div>
        <div className="grid gap-2"><Label htmlFor="manual-paid-at">Fecha del recaudo</Label><Input id="manual-paid-at" type="datetime-local" value={paidAt} onChange={event => setPaidAt(event.target.value)} /></div>
        <div className="grid gap-2"><Label htmlFor="manual-note">Observación</Label><Textarea id="manual-note" value={note} maxLength={500} onChange={event => setNote(event.target.value)} placeholder="Banco, cuenta receptora o contexto de conciliación" /></div>
      </div>
      <DialogFooter><Button variant="outline" onClick={() => setOpen(false)} disabled={saving}>Cancelar</Button><Button onClick={() => void recordPayment()} disabled={saving}>{saving && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}Confirmar y facturar</Button></DialogFooter>
    </DialogContent></Dialog>
  </>;
}

function Value({ label, value }: { label: string; value: string }) { return <div><p className="text-xs text-muted-foreground">{label}</p><p className="mt-1 font-medium">{value}</p></div>; }
function localDateTime(value: Date) { const offset = value.getTimezoneOffset(); return new Date(value.getTime() - offset * 60_000).toISOString().slice(0, 16); }
