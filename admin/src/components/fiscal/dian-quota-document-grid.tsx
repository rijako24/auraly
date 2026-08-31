"use client";

import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { AlertTriangle, CheckCircle2, Clock3, RefreshCw } from "lucide-react";
import { fiscalDocumentsApi } from "@/services/api/fiscal-documents";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";

const labels: Record<string, string> = {
  BlockedByQuota: "Esperando compra de cupo", PendingGeneration: "Preparando envío",
  PendingSubmission: "Pendiente de envío", PendingDianResult: "Esperando DIAN",
  DianAccepted: "Aceptado por DIAN", DianRejected: "Rechazado por DIAN",
  RetryScheduled: "Reintento programado", PermanentFailure: "Requiere atención",
};

export function DianQuotaDocumentGrid() {
  const [page, setPage] = useState(1);
  const [status, setStatus] = useState("all");
  const pageSize = 20;
  const query = useQuery({
    queryKey: ["fiscal", "quota-history", page, status],
    queryFn: () => fiscalDocumentsApi.quotaHistory(page, pageSize, status === "all" ? undefined : status),
    refetchInterval: 15_000,
  });
  const totalPages = Math.max(1, Math.ceil((query.data?.totalCount ?? 0) / pageSize));
  return <Card className="overflow-hidden rounded-3xl">
    <CardHeader className="border-b bg-amber-50/60">
      <div className="flex flex-wrap items-start justify-between gap-4"><div><CardTitle className="flex items-center gap-2"><AlertTriangle className="h-5 w-5 text-amber-600"/>Documentos afectados por falta de cupo</CardTitle><CardDescription className="mt-2 max-w-3xl">Aquí permanece la trazabilidad de los documentos que una caja generó sin conexión y no pudieron enviarse por falta de cupo. Al ampliar la capacidad, el motor fiscal los reanuda sin repetir inventario ni contabilidad.</CardDescription></div><div className="flex gap-2"><Select value={status} onValueChange={(value) => { setStatus(value); setPage(1); }}><SelectTrigger className="w-52 bg-background"><SelectValue/></SelectTrigger><SelectContent><SelectItem value="all">Todos los estados</SelectItem><SelectItem value="BlockedByQuota">Esperando cupo</SelectItem><SelectItem value="PendingGeneration">Preparando envío</SelectItem><SelectItem value="PendingDianResult">Esperando DIAN</SelectItem><SelectItem value="DianAccepted">Aceptados</SelectItem><SelectItem value="DianRejected">Rechazados</SelectItem></SelectContent></Select><Button variant="outline" size="icon" onClick={() => void query.refetch()} aria-label="Actualizar"><RefreshCw className={`h-4 w-4 ${query.isFetching ? "animate-spin" : ""}`}/></Button></div></div>
    </CardHeader>
    <CardContent className="p-0"><Table><TableHeader><TableRow><TableHead>Documento</TableHead><TableHead>Tipo</TableHead><TableHead>Bloqueado</TableHead><TableHead>Estado actual</TableHead><TableHead>Última actualización</TableHead></TableRow></TableHeader><TableBody>
      {query.data?.items.map((item) => <TableRow key={item.documentId}><TableCell><b>{item.auralyNumber}</b><small className="block text-muted-foreground">DIAN {item.dianNumber}</small></TableCell><TableCell>{item.fiscalDocumentType === "Invoice" ? "Factura electrónica" : item.fiscalDocumentType === "SupportDocument" ? "Documento soporte" : "Nómina electrónica"}</TableCell><TableCell>{item.quotaBlockedAt ? new Date(item.quotaBlockedAt).toLocaleString("es-CO") : "—"}</TableCell><TableCell><Badge variant={item.status === "DianAccepted" ? "default" : item.status === "BlockedByQuota" ? "destructive" : "secondary"} className="gap-1">{item.status === "DianAccepted" ? <CheckCircle2 className="h-3 w-3"/> : <Clock3 className="h-3 w-3"/>}{labels[item.status] ?? item.status}</Badge>{item.lastStatusDescription && <small className="mt-1 block max-w-sm text-muted-foreground">{item.lastStatusDescription}</small>}</TableCell><TableCell>{new Date(item.updatedAt).toLocaleString("es-CO")}</TableCell></TableRow>)}
      {!query.isLoading && !query.data?.items.length && <TableRow><TableCell colSpan={5} className="h-32 text-center text-muted-foreground">No hay documentos afectados por falta de cupo.</TableCell></TableRow>}
    </TableBody></Table><div className="flex items-center justify-between border-t p-4 text-sm"><span>{query.data?.totalCount ?? 0} documentos</span><div className="flex items-center gap-2"><Button variant="outline" size="sm" disabled={page <= 1} onClick={() => setPage((value) => value - 1)}>Anterior</Button><span>{page} de {totalPages}</span><Button variant="outline" size="sm" disabled={page >= totalPages} onClick={() => setPage((value) => value + 1)}>Siguiente</Button></div></div></CardContent>
  </Card>;
}
