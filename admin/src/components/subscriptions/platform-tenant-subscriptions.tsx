"use client";

import { useState } from "react";
import Link from "next/link";
import { useQuery } from "@tanstack/react-query";
import { Building2, ChevronLeft, ChevronRight, Search } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { tenantsApi } from "@/services/api/tenants";

const money = new Intl.NumberFormat("es-CO", { style: "currency", currency: "COP", maximumFractionDigits: 0 });
const date = new Intl.DateTimeFormat("es-CO", { dateStyle: "medium" });

export function PlatformTenantSubscriptions() {
  const [page, setPage] = useState(1);
  const [search, setSearch] = useState("");
  const [status, setStatus] = useState("all");
  const query = useQuery({
    queryKey: ["platform", "tenant-subscriptions", page, search, status],
    queryFn: () => tenantsApi.subscriptions({ page, pageSize: 20, search: search || undefined,
      status: status === "all" ? undefined : status }),
  });
  const result = query.data;

  return <div className="mx-auto max-w-[1800px] space-y-6">
    <div><p className="mb-1 text-sm font-medium text-primary">PLATAFORMA AURALY</p>
      <h1 className="text-3xl font-semibold tracking-tight">Suscripciones de empresas</h1>
      <p className="mt-1 text-muted-foreground">Planes, cupos, vigencias y renovaciones de todas las empresas.</p>
    </div>
    <Card>
      <CardHeader><CardTitle>Suscripciones</CardTitle><CardDescription>{result ? `${result.totalCount.toLocaleString("es-CO")} empresas` : "Consultando suscripciones…"}</CardDescription></CardHeader>
      <CardContent className="space-y-4">
        <div className="flex flex-col gap-3 sm:flex-row">
          <div className="relative flex-1"><Search className="absolute left-3 top-3 h-4 w-4 text-muted-foreground"/><Input value={search} onChange={(event) => { setSearch(event.target.value); setPage(1); }} placeholder="Buscar empresa, identificador o correo" className="pl-9"/></div>
          <Select value={status} onValueChange={(value) => { setStatus(value); setPage(1); }}><SelectTrigger className="sm:w-52"><SelectValue/></SelectTrigger><SelectContent><SelectItem value="all">Todos los estados</SelectItem><SelectItem value="Missing">Sin suscripción</SelectItem><SelectItem value="Active">Activas</SelectItem><SelectItem value="PastDue">En gracia</SelectItem><SelectItem value="Suspended">Suspendidas</SelectItem><SelectItem value="Cancelled">Canceladas</SelectItem></SelectContent></Select>
        </div>
        <div className="overflow-x-auto rounded-xl border"><Table><TableHeader><TableRow><TableHead>Tenant</TableHead><TableHead>Plan</TableHead><TableHead>Estado</TableHead><TableHead>Vigencia</TableHead><TableHead>Cupos contratados</TableHead><TableHead>Documentos usados</TableHead><TableHead>Renovación</TableHead><TableHead/></TableRow></TableHeader>
          <TableBody>{query.isLoading && <TableRow><TableCell colSpan={8} className="h-28 text-center text-muted-foreground">Cargando suscripciones…</TableCell></TableRow>}
          {query.isError && <TableRow><TableCell colSpan={8} className="h-28 text-center text-destructive">No fue posible cargar las suscripciones.</TableCell></TableRow>}
          {result?.items.map((item) => <TableRow key={item.tenantId}>
            <TableCell><div className="flex items-center gap-3"><span className="rounded-lg bg-primary/10 p-2"><Building2 className="h-4 w-4 text-primary"/></span><div><p className="font-medium">{item.tenantName}</p><p className="text-xs text-muted-foreground">{item.tenantKey} · {item.tenantEmail}</p></div></div></TableCell>
            <TableCell>{item.subscriptionId ? <><p className="font-medium">{item.planName}</p><p className="text-xs text-muted-foreground">{item.billingPeriod === "Annual" ? "Anual" : "Mensual"}</p></> : <span className="text-muted-foreground">Pendiente de asignación</span>}</TableCell>
            <TableCell><Status value={item.status ?? "Missing"}/></TableCell>
            <TableCell className="whitespace-nowrap">{item.currentPeriodEnd ? `Hasta ${date.format(new Date(item.currentPeriodEnd))}` : "—"}</TableCell>
            <TableCell className="min-w-64 text-xs">{item.subscriptionId ? <><span className="font-medium">{item.fullUserLimit}</span> usuarios · <span className="font-medium">{item.sellerUserLimit}</span> vendedores<br/><span className="font-medium">{item.posDeviceLimit}</span> cajas · <span className="font-medium">{item.payrollEmployeeLimit}</span> empleados · <span className="font-medium">{item.dianDocumentMonthlyLimit?.toLocaleString("es-CO")}</span> DIAN</> : "—"}</TableCell>
            <TableCell>{item.subscriptionId ? `${item.dianDocumentsUsed?.toLocaleString("es-CO")} / ${item.dianDocumentMonthlyLimit?.toLocaleString("es-CO")}` : "—"}</TableCell>
            <TableCell>{item.renewalStatus ? <div><Status value={item.renewalStatus}/>{item.renewalPayableAmount != null && <p className="mt-1 text-xs font-medium">{money.format(item.renewalPayableAmount)}</p>}</div> : <span className="text-muted-foreground">Sin orden pendiente</span>}</TableCell>
            <TableCell><Button asChild variant="outline" size="sm"><Link href={`/dashboard/tenants/${item.tenantId}`}>Ver tenant</Link></Button></TableCell>
          </TableRow>)}
          {result && result.items.length === 0 && <TableRow><TableCell colSpan={8} className="h-28 text-center text-muted-foreground">No hay suscripciones con esos filtros.</TableCell></TableRow>}</TableBody>
        </Table></div>
        {result && result.totalPages > 1 && <div className="flex items-center justify-end gap-3"><span className="text-sm text-muted-foreground">Página {result.page} de {result.totalPages}</span><Button variant="outline" size="icon" disabled={!result.hasPreviousPage} onClick={() => setPage(value => value - 1)}><ChevronLeft className="h-4 w-4"/></Button><Button variant="outline" size="icon" disabled={!result.hasNextPage} onClick={() => setPage(value => value + 1)}><ChevronRight className="h-4 w-4"/></Button></div>}
      </CardContent>
    </Card>
  </div>;
}

function Status({ value }: { value: string }) {
  const variant = value === "Active" || value === "Activated" || value === "PaymentConfirmed" ? "secondary" : value === "Suspended" || value === "PaymentFailed" ? "destructive" : "outline";
  const labels: Record<string, string> = { Missing: "Sin suscripción", Active: "Activa", PastDue: "En gracia", Suspended: "Suspendida", Cancelled: "Cancelada", Draft: "Borrador", PendingPayment: "Pendiente", PaymentConfirmed: "Pagada", Invoicing: "Facturando", Activated: "Activada", Expired: "Vencida", PaymentFailed: "Pago fallido" };
  return <Badge variant={variant}>{labels[value] ?? value}</Badge>;
}
