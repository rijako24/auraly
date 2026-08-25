"use client";

import { useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { AlertTriangle, Banknote, CheckCircle2, Loader2 } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { DatePicker } from "@/components/ui/date-picker";
import { Label } from "@/components/ui/label";
import { workSessionDifferencesApi } from "@/services/api/work-session-differences";

const money = new Intl.NumberFormat("es-CO", { style: "currency", currency: "COP", maximumFractionDigits: 0 });
const isoDate = (value: Date) => value.toISOString().slice(0, 10);

export default function CashDifferencesPage() {
  const today = useMemo(() => new Date(), []);
  const monthStart = useMemo(() => new Date(today.getFullYear(), today.getMonth(), 1), [today]);
  const [from, setFrom] = useState(isoDate(monthStart));
  const [to, setTo] = useState(isoDate(today));
  const differences = useQuery({
    queryKey: ["work-session-cash-differences", from, to],
    queryFn: () => workSessionDifferencesApi.list(from, to),
    enabled: Boolean(from && to && from <= to),
  });
  const rows = differences.data ?? [];
  const surplus = rows.filter((row) => row.difference > 0).reduce((sum, row) => sum + row.difference, 0);
  const shortage = rows.filter((row) => row.difference < 0).reduce((sum, row) => sum + Math.abs(row.difference), 0);

  return <div className="space-y-5">
    <header className="rounded-3xl bg-gradient-to-r from-slate-950 via-amber-950 to-orange-700 p-6 text-white shadow-lg">
      <p className="text-xs font-bold uppercase tracking-[.2em] text-amber-200">Control de cierres</p>
      <h1 className="mt-2 text-3xl font-black">Diferencias de efectivo</h1>
      <p className="mt-2 max-w-3xl text-sm text-amber-50/80">Cada sobrante o faltante se contabiliza al cerrar la sesión y permanece aquí con trazabilidad por cajero.</p>
    </header>

    <div className="grid gap-4 sm:grid-cols-3">
      <Summary title="Cierres con diferencia" value={String(rows.length)} icon={<Banknote className="h-5 w-5" />} />
      <Summary title="Sobrantes" value={money.format(surplus)} icon={<CheckCircle2 className="h-5 w-5 text-emerald-600" />} />
      <Summary title="Faltantes" value={money.format(shortage)} icon={<AlertTriangle className="h-5 w-5 text-red-600" />} />
    </div>

    <Card className="rounded-3xl">
      <CardHeader className="gap-4 border-b sm:flex-row sm:items-end sm:justify-between">
        <div><CardTitle>Historial auditable</CardTitle><p className="mt-1 text-sm text-muted-foreground">Consulta los cierres con diferencia dentro del periodo seleccionado.</p></div>
        <div className="grid gap-3 sm:grid-cols-2">
          <div className="min-w-0 space-y-1.5"><Label>Desde</Label><DatePicker value={from} onChange={setFrom} className="sm:w-56" /></div>
          <div className="min-w-0 space-y-1.5"><Label>Hasta</Label><DatePicker value={to} onChange={setTo} className="sm:w-56" /></div>
        </div>
      </CardHeader>
      <CardContent className="p-0">
        {differences.isLoading && <div className="flex items-center justify-center gap-2 p-10 text-muted-foreground"><Loader2 className="h-5 w-5 animate-spin" />Consultando cierres…</div>}
        {differences.isError && <p className="p-8 text-center text-sm text-destructive">No fue posible consultar las diferencias.</p>}
        {!differences.isLoading && !rows.length && <p className="p-10 text-center text-sm text-muted-foreground">No hay cierres con diferencia en el periodo.</p>}
        {rows.length > 0 && <div className="overflow-x-auto"><table className="w-full min-w-[980px] text-sm">
          <thead className="bg-muted/50"><tr><th className="p-3 text-left">Cierre</th><th className="text-left">Cajero</th><th className="text-left">Sede</th><th className="text-right">Esperado</th><th className="text-right">Contado</th><th className="text-right">Diferencia</th><th className="text-left">Tratamiento</th><th className="pr-3 text-left">Contabilidad</th></tr></thead>
          <tbody>{rows.map((row) => <tr key={row.workSessionClosureId} className="border-t">
            <td className="p-3 whitespace-nowrap">{new Date(row.closedAt).toLocaleString("es-CO")}</td>
            <td>{row.userName}</td><td>{row.businessName}<small className="block text-muted-foreground">{row.warehouseName}</small></td>
            <td className="text-right">{money.format(row.expectedCash)}</td><td className="text-right">{money.format(row.countedCash)}</td>
            <td className={`text-right font-bold ${row.difference > 0 ? "text-emerald-700" : "text-red-700"}`}>{row.difference > 0 ? "+" : "−"}{money.format(Math.abs(row.difference))}</td>
            <td><Badge variant={row.difference > 0 ? "secondary" : "destructive"}>{row.difference > 0 ? "Ingreso por sobrante" : "Gasto por faltante"}</Badge></td>
            <td className="pr-3"><AccountingStatus status={row.accountingStatus} entry={row.accountingEntryNumber} /></td>
          </tr>)}</tbody>
        </table></div>}
      </CardContent>
    </Card>
  </div>;
}

function Summary({ title, value, icon }: { title: string; value: string; icon: React.ReactNode }) {
  return <Card className="rounded-3xl"><CardContent className="flex items-center gap-3 p-5"><div className="rounded-2xl bg-muted p-3">{icon}</div><div><p className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">{title}</p><strong className="text-xl">{value}</strong></div></CardContent></Card>;
}

function AccountingStatus({ status, entry }: { status: string; entry: string | null }) {
  if (status === "Posted") return <><Badge variant="secondary">Contabilizado</Badge>{entry && <small className="mt-1 block font-mono text-muted-foreground">{entry}</small>}</>;
  if (status === "AccountingDisabled") return <Badge variant="outline">Contabilidad no activa</Badge>;
  if (status === "AccountingPendingConfiguration") return <Badge variant="destructive">Configuración pendiente</Badge>;
  return <Badge variant="outline">En proceso</Badge>;
}
