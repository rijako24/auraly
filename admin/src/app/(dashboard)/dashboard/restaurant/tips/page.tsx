"use client";

import { useMemo, useRef, useState } from "react";
import Link from "next/link";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowLeft, Check, HandCoins, Trash2 } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { PartyRoleSelect } from "@/components/parties/party-role-select";
import { PagedEntitySelect } from "@/components/forms/paged-entity-select";
import { paymentMethodsApi, type PaymentMethod } from "@/services/api/payment-methods";
import { restaurantAccountApi, type RestaurantTipSettlement,
  type RestaurantTipSettlementRequest, type RestaurantTipPendingPage } from "@/services/api/restaurant-account";
import { useTenantContextStore } from "@/stores/tenant-context-store";
import { useAuthStore } from "@/stores/auth-store";
import { useBusinessContextStore } from "@/stores/business-context-store";
import { useBusinessDates } from "@/hooks/use-business-dates";

const money = new Intl.NumberFormat("es-CO", {
  style: "currency", currency: "COP", maximumFractionDigits: 0,
});
type Recipient = { partyId: string; name: string; amount: string };

export default function RestaurantTipsPage() {
  const queryClient = useQueryClient();
  const tenantId = useTenantContextStore(state => state.selectedTenantId);
  const businessId = useBusinessContextStore(state => state.selectedBusinessId);
  const dates = useBusinessDates(businessId);
  const permissions = useAuthStore(state => state.user?.permissions ?? []);
  const allowed = permissions.includes("cash.close");
  const pending = useQuery({ queryKey: ["restaurant-pending-tips", tenantId, businessId],
    queryFn: restaurantAccountApi.pendingTips, enabled: Boolean(businessId && dates.timeZone && allowed) });
  const recent = useQuery({ queryKey: ["restaurant-tip-settlements", tenantId, businessId],
    queryFn: restaurantAccountApi.recentTipSettlements,
    enabled: Boolean(businessId && dates.timeZone && allowed) });
  const [selected, setSelected] = useState<string[]>([]);
  const [recipients, setRecipients] = useState<Recipient[]>([]);
  const [role, setRole] = useState<"Employee" | "Waiter">("Employee");
  const [basis, setBasis] = useState<"EqualByLaw" | "WorkerAgreement">("EqualByLaw");
  const [agreementReference, setAgreementReference] = useState("");
  const [method, setMethod] = useState<"Cash" | "Transfer">("Cash");
  const [paymentSelection, setPaymentSelection] = useState<{ tenantId: string | null; form: PaymentMethod }>();
  const paymentForm = paymentSelection?.tenantId === tenantId ? paymentSelection?.form : undefined;
  const setPaymentForm = (form: PaymentMethod | undefined) => setPaymentSelection(form ? { tenantId, form } : undefined);
  const [paymentReference, setPaymentReference] = useState("");
  const [busy, setBusy] = useState(false);
  const [last, setLast] = useState<RestaurantTipSettlement | null>(null);
  const attempt = useRef<{ signature: string; id: string } | null>(null);
  const items = pending.data?.items ?? [];
  const visibleSelected = items.filter(item => selected.includes(item.collectionId));
  const total = visibleSelected.reduce((sum, item) => sum + item.amount, 0);
  const equalShares = useMemo(() => {
    if (!recipients.length) return new Map<string, number>();
    const sorted = [...recipients].sort((a, b) => a.partyId.localeCompare(b.partyId));
    const base = Math.floor(total / sorted.length);
    const remainder = Math.round(total - base * sorted.length);
    return new Map(sorted.map((item, index) =>
      [item.partyId, base + (index < remainder ? 1 : 0)]));
  }, [recipients, total]);
  const agreedTotal = recipients.reduce((sum, item) => sum + (Number(item.amount) || 0), 0);
  const selectedForm = paymentForm?.methodCode === method ? paymentForm : undefined;
  const canSubmit = allowed && !busy && selected.length > 0 && total > 0 &&
    recipients.length > 0 && (basis === "EqualByLaw"
      ? [...equalShares.values()].every(value => value > 0)
      : agreementReference.trim().length > 0 &&
        recipients.every(item => Number(item.amount) > 0) && agreedTotal === total) &&
    Boolean(selectedForm && (method === "Cash" || selectedForm.bankAccountId && paymentReference.trim()));

  async function settle() {
    if (!canSubmit) return;
    const requestWithoutId = {
      collectionIds: visibleSelected.map(item => item.collectionId),
      recipients: recipients.map(item => ({ partyId: item.partyId,
        amount: basis === "EqualByLaw" ? 0 : Number(item.amount) })),
      distributionBasis: basis, agreementReference: basis === "WorkerAgreement"
        ? agreementReference.trim() : null,
      paymentMethodCode: method, bankAccountId: method === "Transfer" ? selectedForm!.bankAccountId : null,
      paymentMethodId: selectedForm!.paymentMethodId, paymentMethodVersion: selectedForm!.version,
      paymentReference: method === "Transfer" ? paymentReference.trim() : null,
    } satisfies Omit<RestaurantTipSettlementRequest, "settlementId">;
    const signature = JSON.stringify(requestWithoutId);
    if (attempt.current?.signature !== signature)
      attempt.current = { signature, id: crypto.randomUUID() };
    setBusy(true);
    try {
      const result = await restaurantAccountApi.settleTips({
        settlementId: attempt.current.id, ...requestWithoutId,
      });
      attempt.current = null;
      setLast(result);
      setSelected([]);
      setRecipients([]);
      queryClient.setQueryData<RestaurantTipPendingPage>(["restaurant-pending-tips", tenantId, businessId], (current) => {
        if (!current) return current;
        const ids = new Set(requestWithoutId.collectionIds);
        const removed = current.items.filter((item) => ids.has(item.collectionId));
        return { items: current.items.filter((item) => !ids.has(item.collectionId)),
          totalCount: current.totalCount - removed.length,
          totalAmount: current.totalAmount - removed.reduce((sum, item) => sum + item.amount, 0) };
      });
      queryClient.setQueryData<RestaurantTipSettlement[]>(["restaurant-tip-settlements", tenantId, businessId],
        (current) => [result, ...(current ?? []).filter((item) => item.settlementId !== result.settlementId)].slice(0, 30));
      toast.success("Propinas liquidadas y registradas por separado de las ventas.");
    } catch (error) {
      toast.error(error instanceof Error ? error.message : "No se pudo liquidar la propina.");
    } finally { setBusy(false); }
  }

  if (businessId && allowed && !dates.timeZone)
    return <p role="alert" className="rounded-2xl border bg-card p-5">Configura la zona horaria de esta sede antes de consultar las propinas.</p>;
  return <div className="mx-auto max-w-5xl space-y-6 pb-12">
    <header><Link href="/dashboard/restaurant" className="inline-flex items-center gap-2 text-sm text-muted-foreground hover:text-foreground"><ArrowLeft className="h-4 w-4"/>Volver a restaurante</Link>
      <p className="mt-5 text-xs font-bold uppercase tracking-[.18em] text-primary">Restaurante y bar · personal</p>
      <h1 className="mt-1 text-3xl font-bold">Liquidación de propinas</h1>
      <p className="mt-2 text-muted-foreground">Entrega al personal lo recaudado. La propina conserva su cuenta por pagar y no se suma a las ventas.</p>
    </header>
    {!businessId ? <p className="rounded-2xl border bg-card p-5">Selecciona la sede del restaurante.</p>
      : !allowed ? <p className="rounded-2xl border bg-card p-5">Necesitas permiso de cierre de caja para liquidar propinas.</p>
      : <><section className="rounded-3xl border bg-card p-5 shadow-sm sm:p-7">
        <div className="flex flex-wrap items-start justify-between gap-4"><div><h2 className="text-xl font-bold">Propinas pendientes</h2><p className="mt-1 text-sm text-muted-foreground">Selecciona las capturas que vas a entregar en esta liquidación. Se muestran las 100 más antiguas.</p></div><div className="text-right"><p className="text-xs uppercase tracking-wide text-muted-foreground">Pendiente total</p><p className="text-2xl font-bold text-teal-700">{money.format(pending.data?.totalAmount ?? 0)}</p><p className="text-xs text-muted-foreground">{pending.data?.totalCount ?? 0} cobros</p></div></div>
        <div className="mt-5 flex flex-wrap gap-2"><Button variant="outline" className="min-h-11" disabled={!items.length || busy} onClick={() => setSelected(items.map(item => item.collectionId))}>Seleccionar visibles</Button><Button variant="ghost" disabled={busy || pending.isFetching} onClick={() => void pending.refetch()}>Actualizar pendientes</Button><Button variant="ghost" className="min-h-11" disabled={!selected.length || busy} onClick={() => setSelected([])}>Limpiar</Button></div>
        {pending.isError && <p role="alert" className="mt-4 text-sm text-destructive">No se pudieron cargar las propinas. <button className="underline" onClick={() => void pending.refetch()}>Reintentar</button></p>}
        <div className="mt-4 max-h-72 space-y-2 overflow-y-auto">{items.map(item => {
          const active = selected.includes(item.collectionId);
          return <button key={item.collectionId} type="button" disabled={busy} aria-pressed={active} onClick={() => setSelected(current => active ? current.filter(id => id !== item.collectionId) : [...current, item.collectionId])} className={`flex min-h-14 w-full items-center gap-3 rounded-xl border px-4 text-left ${active ? "border-teal-500 bg-teal-50" : "hover:bg-muted/40"}`}><span className={`grid h-7 w-7 shrink-0 place-items-center rounded-md border ${active ? "border-teal-600 bg-teal-600 text-white" : "border-slate-300"}`}>{active && <Check className="h-4 w-4"/>}</span><span className="min-w-0 flex-1"><strong className="block">{money.format(item.amount)}</strong><span className="text-xs text-muted-foreground">{dates.formatDateTime(item.capturedAt)} · Cuenta {item.accountId.slice(0, 8)}</span></span></button>;
        })}{!pending.isLoading && !items.length && <p className="rounded-xl bg-muted/30 p-4 text-sm text-muted-foreground">No hay propinas pendientes.</p>}</div>
        <p className="mt-4 text-right font-semibold">Seleccionado: {money.format(total)}</p>
      </section>
      <section className="rounded-3xl border bg-card p-5 shadow-sm sm:p-7"><h2 className="text-xl font-bold">Reparto al personal</h2><p className="mt-1 text-sm text-muted-foreground">El acuerdo lo establecen los trabajadores. Si no existe acuerdo, aplica el reparto equitativo entre quienes participaron en el servicio.</p>
        <div className="mt-5 flex flex-wrap gap-2"><Button type="button" variant={basis === "EqualByLaw" ? "default" : "outline"} className="min-h-11" onClick={() => setBasis("EqualByLaw")}>Equitativo</Button><Button type="button" variant={basis === "WorkerAgreement" ? "default" : "outline"} className="min-h-11" onClick={() => setBasis("WorkerAgreement")}>Acuerdo de trabajadores</Button></div>
        {basis === "WorkerAgreement" && <label className="mt-4 block text-sm font-semibold">Referencia del acuerdo<Input className="mt-2 h-11" maxLength={300} value={agreementReference} onChange={event => setAgreementReference(event.target.value)} placeholder="Acta, fecha o documento del acuerdo"/></label>}
        <div className="mt-5 grid gap-3 sm:grid-cols-[150px_1fr]"><Select value={role} onValueChange={value => setRole(value as "Employee" | "Waiter")}><SelectTrigger aria-label="Rol del trabajador" className="h-11"><SelectValue/></SelectTrigger><SelectContent><SelectItem value="Employee">Empleado</SelectItem><SelectItem value="Waiter">Mesero</SelectItem></SelectContent></Select><PartyRoleSelect key={role} role={role} includePartyId value="" ariaLabel="Seleccionar trabajador" placeholder="Buscar y agregar trabajador" onChange={(_value, party) => { if (party && !recipients.some(item => item.partyId === party.partyId)) setRecipients(current => [...current, { partyId: party.partyId, name: party.displayName, amount: "" }]); }}/></div>
        <div className="mt-4 space-y-2">{recipients.map(item => <div key={item.partyId} className="flex flex-wrap items-center gap-3 rounded-xl border p-3"><span className="min-w-32 flex-1 font-semibold">{item.name}</span>{basis === "EqualByLaw" ? <strong>{money.format(equalShares.get(item.partyId) ?? 0)}</strong> : <Input aria-label={`Valor para ${item.name}`} inputMode="decimal" className="h-11 w-40" value={item.amount} onChange={event => setRecipients(current => current.map(row => row.partyId === item.partyId ? { ...row, amount: event.target.value } : row))}/>}<Button variant="ghost" size="icon" aria-label={`Quitar ${item.name}`} onClick={() => setRecipients(current => current.filter(row => row.partyId !== item.partyId))}><Trash2 className="h-4 w-4"/></Button></div>)}{!recipients.length && <p className="text-sm text-muted-foreground">Agrega a quienes participaron en el servicio, incluida cocina o barra cuando corresponda.</p>}</div>
        {basis === "WorkerAgreement" && <p className={`mt-3 text-sm ${agreedTotal === total ? "text-teal-700" : "text-amber-700"}`}>Asignado: {money.format(agreedTotal)} de {money.format(total)}</p>}
      </section>
      <section className="rounded-3xl border bg-card p-5 shadow-sm sm:p-7"><h2 className="text-xl font-bold">Entrega</h2><div className="mt-4 grid gap-3 sm:grid-cols-2"><Button variant={method === "Cash" ? "default" : "outline"} className="min-h-12" onClick={() => setMethod("Cash")}>Efectivo</Button><Button variant={method === "Transfer" ? "default" : "outline"} className="min-h-12" onClick={() => setMethod("Transfer")}>Transferencia</Button></div><div className="mt-4"><PagedEntitySelect<PaymentMethod>
        queryKey={["payment-method-choices", tenantId, businessId, "TipPayments", method]}
        value={selectedForm?.paymentMethodId ?? ""}
        selectedOption={selectedForm ? { value: selectedForm.paymentMethodId, label: selectedForm.name } : null}
        onChange={(_value, _option, item) => setPaymentForm(item)}
        loadPage={async (search, page, pageSize) => {
          const result = await paymentMethodsApi.list({ page, pageSize, search, usage: "TipPayments", methodCode: method });
          return { ...result, totalPages: Math.ceil(result.totalCount / pageSize) };
        }}
        getOption={(item) => ({ value: item.paymentMethodId, label: item.name, description: item.code })}
        ariaLabel="Forma de entrega de propinas" placeholder="Selecciona la forma de pago"
        disabled={busy || !allowed || !businessId}
      /></div>{method === "Transfer" && <div className="mt-4 grid gap-3 sm:grid-cols-2"><Input aria-label="Referencia de transferencia" className="h-11" maxLength={160} value={paymentReference} onChange={event => setPaymentReference(event.target.value)} placeholder="Referencia de transferencia"/></div>}
        <Button className="mt-6 min-h-14 w-full text-base" disabled={!canSubmit} onClick={() => void settle()}><HandCoins className="mr-2 h-5 w-5"/>{busy ? "Registrando entrega…" : `Liquidar ${money.format(total)}`}</Button>
      </section>
      {last && <section role="status" className="rounded-3xl border border-teal-300 bg-teal-50 p-5"><h2 className="font-bold text-teal-900">Entrega registrada · {money.format(last.totalAmount)}</h2><p className="mt-1 text-sm text-teal-800">{last.recipients.length} trabajador(es) · {last.paymentMethodCode === "Cash" ? "Efectivo" : "Transferencia"} · {last.settlementId.slice(0, 8)}</p></section>}
      <section className="rounded-3xl border bg-card p-5 shadow-sm sm:p-7"><h2 className="text-xl font-bold">Últimas entregas</h2><div className="mt-4 space-y-2">{recent.data?.map(item => <div key={item.settlementId} className="rounded-xl border p-4"><div className="flex flex-wrap items-center justify-between gap-2"><strong>{money.format(item.totalAmount)}</strong><span className="text-sm text-muted-foreground">{dates.formatDateTime(item.createdAt)}</span></div><p className="mt-1 text-sm text-muted-foreground">{item.recipients.length} trabajador(es) · {item.distributionBasis === "EqualByLaw" ? "Equitativo" : `Acuerdo ${item.agreementReference}`} · {item.paymentMethodCode === "Cash" ? "Efectivo" : `Transferencia ${item.paymentReference ?? ""}`}</p><p className="mt-1 text-xs text-muted-foreground">Referencia {item.settlementId}</p></div>)}{!recent.isLoading && !recent.data?.length && <p className="text-sm text-muted-foreground">Todavía no hay entregas registradas.</p>}</div></section>
    </>}
  </div>;
}
