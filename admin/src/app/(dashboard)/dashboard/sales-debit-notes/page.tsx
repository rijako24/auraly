"use client";

import { useState, type ComponentProps } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { FilePlus2, Loader2, Printer, Search } from "lucide-react";
import { toast } from "sonner";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input as BaseInput } from "@/components/ui/input";
import { DatePicker } from "@/components/ui/date-picker";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Textarea } from "@/components/ui/textarea";
import { salesReturnsApi, type ReturnableSaleListItem } from "@/services/api/sales-returns";
import { salesDebitNotesApi, type SalesDebitNoteDetail } from "@/services/api/sales-debit-notes";
import { tenantsApi } from "@/services/api/tenants";
import { useBusinessContextStore } from "@/stores/business-context-store";
import { formatCurrency, formatDateTime } from "@/lib/utils";

const concepts = {
  "1": "Intereses",
  "2": "Gastos por cobrar",
  "3": "Cambio del valor",
  "4": "Otros",
} as const;

function Input(props: ComponentProps<typeof BaseInput>) {
  if (props.type !== "date") return <BaseInput {...props} />;
  const { value, onChange, disabled, className, min, max, id } = props;
  return <DatePicker id={id} value={String(value ?? "")} disabled={disabled} className={className}
    min={typeof min === "string" ? min : undefined} max={typeof max === "string" ? max : undefined}
    onChange={next => onChange?.({ target: { value: next }, currentTarget: { value: next } } as never)} />;
}

export default function SalesDebitNotesPage() {
  const [search, setSearch] = useState("");
  const [createOpen, setCreateOpen] = useState(false);
  const [printId, setPrintId] = useState<string>();
  const list = useQuery({
    queryKey: ["sales-debit-notes", search],
    queryFn: () => salesDebitNotesApi.list({ page: 1, pageSize: 100, search: search.trim() || undefined }),
  });

  return <div className="space-y-6">
    <header className="flex flex-col justify-between gap-4 lg:flex-row lg:items-end">
      <div><p className="text-sm font-medium text-primary">Ventas</p><h1 className="text-3xl font-semibold tracking-tight">Notas débito</h1><p className="mt-1 max-w-3xl text-muted-foreground">Aumenta el valor de una factura electrónica por intereses, gastos o cambio de valor. La nota crea cartera, asiento contable y documento DIAN sin alterar la factura original.</p></div>
      <Button onClick={() => setCreateOpen(true)}><FilePlus2 className="mr-2 h-4 w-4" />Nueva nota débito</Button>
    </header>
    <Card><CardContent className="p-4"><div className="relative max-w-xl"><Search className="pointer-events-none absolute left-3 top-2.5 h-4 w-4 text-muted-foreground" /><Input className="pl-9" value={search} onChange={(event) => setSearch(event.target.value)} placeholder="Nota, factura, cliente o identificación" /></div></CardContent></Card>
    <div className="overflow-hidden rounded-2xl border bg-card">
      <div className="grid grid-cols-[1fr_1fr_1.4fr_10rem_10rem_5rem] gap-3 bg-muted/60 px-4 py-3 text-xs font-semibold uppercase tracking-wide text-muted-foreground"><span>Nota</span><span>Factura</span><span>Cliente</span><span>Valor</span><span>Estado DIAN</span><span /></div>
      {list.isLoading && <p className="p-6 text-muted-foreground">Consultando notas…</p>}
      {!list.isLoading && !list.data?.items.length && <p className="p-6 text-muted-foreground">Todavía no hay notas débito.</p>}
      {list.data?.items.map((item) => <div key={item.debitNoteId} className="grid grid-cols-[1fr_1fr_1.4fr_10rem_10rem_5rem] items-center gap-3 border-t px-4 py-3 text-sm"><div><p className="font-semibold">{item.documentNumber}</p><p className="text-xs text-muted-foreground">{formatDateTime(item.issuedAt)}</p></div><span>{item.originalDocumentNumber}</span><div><p>{item.customerName}</p><p className="text-xs text-muted-foreground">{item.customerIdentification}</p></div><span className="font-semibold">{formatCurrency(item.totalAmount)}</span><Badge variant="outline" className="w-fit">{item.fiscalStatus}</Badge><Button size="icon" variant="ghost" aria-label={`Imprimir ${item.documentNumber}`} onClick={() => setPrintId(item.debitNoteId)}><Printer className="h-4 w-4" /></Button></div>)}
    </div>
    <CreateDebitNote open={createOpen} onClose={() => setCreateOpen(false)} />
    <PrintDebitNote id={printId} onClose={() => setPrintId(undefined)} />
  </div>;
}

function CreateDebitNote({ open, onClose }: { open: boolean; onClose: () => void }) {
  const queryClient = useQueryClient();
  const businessId = useBusinessContextStore((state) => state.selectedBusinessId);
  const [invoiceSearch, setInvoiceSearch] = useState("");
  const [invoice, setInvoice] = useState<ReturnableSaleListItem>();
  const [concept, setConcept] = useState<keyof typeof concepts>("3");
  const [reason, setReason] = useState("");
  const [description, setDescription] = useState("");
  const [base, setBase] = useState("");
  const [taxRate, setTaxRate] = useState("19");
  const [dueAt, setDueAt] = useState(() => new Date().toISOString().slice(0, 10));
  const [notes, setNotes] = useState("");
  const sales = useQuery({
    queryKey: ["debit-note-invoices", invoiceSearch],
    queryFn: () => salesReturnsApi.listSales({ page: 1, pageSize: 20, search: invoiceSearch.trim() || undefined }),
    enabled: open && !invoice,
  });
  const confirm = useMutation({ mutationFn: salesDebitNotesApi.confirm, onSuccess: () => queryClient.invalidateQueries({ queryKey: ["sales-debit-notes"] }) });
  const close = () => { setInvoice(undefined); setReason(""); setDescription(""); setBase(""); setNotes(""); onClose(); };
  const submit = async () => {
    const amount = Number(base);
    const rate = Number(taxRate);
    if (!businessId || !invoice) return;
    if (!invoice.customerId) { toast.error("La nota débito requiere un cliente identificado en la factura."); return; }
    if (!invoice.cufe) { toast.error("La nota débito requiere una factura electrónica con CUFE."); return; }
    if (!reason.trim() || !description.trim() || !Number.isFinite(amount) || amount <= 0 || !Number.isFinite(rate) || rate < 0) { toast.error("Completa motivo, concepto, valor e impuesto."); return; }
    const issuedAt = new Date();
    const due = new Date(`${dueAt}T23:59:59`);
    if (due < issuedAt) { toast.error("El vencimiento no puede ser anterior a la emisión."); return; }
    try {
      const result = await confirm.mutateAsync({ debitNoteId: crypto.randomUUID(), businessId, originalDocumentId: invoice.documentId, issuedAt: issuedAt.toISOString(), dueAt: due.toISOString(), conceptCode: concept, reasonDescription: reason.trim(), notes: notes.trim() || null, lines: [{ description: description.trim(), quantity: 1, unitPrice: amount, taxCode: "01", taxRate: rate }] });
      toast.success(`Nota débito ${result.documentNumber} aceptada. Se está generando el CUDE y la cartera.`);
      close();
    } catch { toast.error("No fue posible crear la nota débito. Verifica la factura y la configuración fiscal."); }
  };

  return <Dialog open={open} onOpenChange={(value) => { if (!value) close(); }}><DialogContent className="max-h-[94dvh] max-w-4xl overflow-y-auto"><DialogHeader><DialogTitle>Nueva nota débito de venta</DialogTitle><DialogDescription>No crea otra factura: referencia una factura electrónica existente, aumenta su saldo por cobrar y genera su propio documento DIAN y asiento contable.</DialogDescription></DialogHeader>
    {!invoice ? <div className="space-y-3"><div><p className="text-sm font-semibold text-primary">Paso 1 de 2 · Selecciona la factura origen</p><p className="text-sm text-muted-foreground">La nota quedará vinculada a esta factura y al mismo cliente.</p></div><div className="relative"><Search className="pointer-events-none absolute left-3 top-2.5 h-4 w-4 text-muted-foreground" /><Input className="pl-9" value={invoiceSearch} onChange={(event) => setInvoiceSearch(event.target.value)} placeholder="Busca factura, CUFE, cliente o identificación" /></div><div className="max-h-80 overflow-y-auto rounded-xl border">{sales.data?.items.map((item) => <button type="button" key={item.documentId} className="flex w-full items-center justify-between gap-4 border-b p-3 text-left hover:bg-muted/60 disabled:cursor-not-allowed disabled:opacity-50" disabled={!item.customerId || !item.cufe} onClick={() => setInvoice(item)}><div><p className="font-semibold">{item.documentNumber} · {item.customerName}</p><p className="text-xs text-muted-foreground">{item.customerIdentification} · CUFE {item.cufe || "no disponible"}</p></div><span>{formatCurrency(item.totalAmount)}</span></button>)}</div></div> : <div className="space-y-5"><div><p className="text-sm font-semibold text-primary">Paso 2 de 2 · Define el valor adicional</p><p className="text-sm text-muted-foreground">Este importe se sumará a la cartera del cliente sin modificar la factura original.</p></div><Card className="bg-primary/5"><CardContent className="flex items-center justify-between p-4"><div><p className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">Factura origen</p><p className="font-semibold">{invoice.documentNumber} · {invoice.customerName}</p><p className="text-xs text-muted-foreground">{invoice.customerIdentification} · {invoice.cufe}</p></div><Button variant="ghost" onClick={() => setInvoice(undefined)}>Cambiar factura</Button></CardContent></Card><div className="grid gap-4 md:grid-cols-2"><div className="space-y-2"><Label>Concepto DIAN</Label><Select value={concept} onValueChange={(value) => setConcept(value as keyof typeof concepts)}><SelectTrigger><SelectValue /></SelectTrigger><SelectContent>{Object.entries(concepts).map(([code, label]) => <SelectItem key={code} value={code}>{code}. {label}</SelectItem>)}</SelectContent></Select></div><div className="space-y-2"><Label>Vencimiento del valor adicional</Label><Input type="date" value={dueAt} onChange={(event) => setDueAt(event.target.value)} /></div><div className="space-y-2 md:col-span-2"><Label>Motivo</Label><Input maxLength={300} value={reason} onChange={(event) => setReason(event.target.value)} placeholder="Explica por qué aumenta el valor de la factura" /></div><div className="space-y-2 md:col-span-2"><Label>Descripción del cargo</Label><Input maxLength={300} value={description} onChange={(event) => setDescription(event.target.value)} placeholder="Intereses, gasto o ajuste facturado" /></div><div className="space-y-2"><Label>Valor antes de impuesto</Label><Input type="number" min="0.01" step="0.01" value={base} onChange={(event) => setBase(event.target.value)} /></div><div className="space-y-2"><Label>IVA</Label><Select value={taxRate} onValueChange={setTaxRate}><SelectTrigger><SelectValue /></SelectTrigger><SelectContent><SelectItem value="0">0%</SelectItem><SelectItem value="5">5%</SelectItem><SelectItem value="19">19%</SelectItem></SelectContent></Select></div><div className="space-y-2 md:col-span-2"><Label>Observaciones</Label><Textarea maxLength={1000} value={notes} onChange={(event) => setNotes(event.target.value)} /></div></div><div className="rounded-xl border border-primary/25 bg-primary/5 p-4"><div className="flex items-end justify-between gap-4"><div><p className="font-semibold">Impacto al confirmar</p><p className="text-sm text-muted-foreground">Aumenta la cuenta por cobrar del cliente por este valor.</p></div><div className="text-right"><p className="text-sm text-muted-foreground">Total nota débito</p><p className="text-2xl font-semibold">{formatCurrency((Number(base) || 0) * (1 + (Number(taxRate) || 0) / 100))}</p></div></div></div></div>}
    <DialogFooter><Button variant="outline" onClick={close}>Cancelar</Button><Button disabled={!invoice || confirm.isPending} onClick={submit}>{confirm.isPending && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}Crear y contabilizar nota débito</Button></DialogFooter></DialogContent></Dialog>;
}

function PrintDebitNote({ id, onClose }: { id?: string; onClose: () => void }) {
  const detail = useQuery({ queryKey: ["sales-debit-note", id], queryFn: () => salesDebitNotesApi.get(id!), enabled: Boolean(id) });
  const print = async () => {
    if (!detail.data) return;
    const branding = await tenantsApi.getBranding().catch(() => null);
    const preview = window.open("", "_blank", "noopener,noreferrer");
    if (!preview) { toast.error("El navegador bloqueó la vista de impresión."); return; }
    preview.document.write(debitNoteHtml(detail.data, branding?.displayName ?? "Auraly", branding?.logoUrl ?? null));
    preview.document.close();
    onClose();
  };
  return <Dialog open={Boolean(id)} onOpenChange={(value) => { if (!value) onClose(); }}><DialogContent><DialogHeader><DialogTitle>Imprimir nota débito</DialogTitle><DialogDescription>{detail.data ? `${detail.data.header.documentNumber} · ${detail.data.header.customerName}` : "Consultando documento…"}</DialogDescription></DialogHeader><DialogFooter><Button variant="outline" onClick={onClose}>Cancelar</Button><Button onClick={print} disabled={!detail.data}><Printer className="mr-2 h-4 w-4" />Imprimir / PDF</Button></DialogFooter></DialogContent></Dialog>;
}

function debitNoteHtml(value: SalesDebitNoteDetail, businessName: string, logoUrl: string | null) {
  const escape = (text: unknown) => String(text ?? "").replace(/[&<>"']/g, (character) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[character]!));
  const rows = value.lines.map((line) => `<tr><td>${escape(line.description)}</td><td>${escape(line.quantity)}</td><td>${escape(formatCurrency(line.unitPrice))}</td><td>${escape(line.taxRate)}%</td><td>${escape(formatCurrency(line.lineTotal))}</td></tr>`).join("");
  return `<!doctype html><html lang="es"><head><meta charset="utf-8"><title>${escape(value.header.documentNumber)}</title><style>@page{margin:14mm}body{font:13px Arial,sans-serif;color:#172033;max-width:900px;margin:auto}header{display:flex;gap:18px;align-items:center;border-bottom:2px solid #0f766e;padding-bottom:14px}img{max-width:180px;max-height:70px}h1{margin:0;color:#0f766e}.meta{display:grid;grid-template-columns:1fr 1fr;gap:8px;margin:18px 0}.box{border:1px solid #d7dee8;border-radius:8px;padding:10px}table{width:100%;border-collapse:collapse}th,td{padding:9px;border-bottom:1px solid #d7dee8;text-align:left}th{background:#f1f5f9}.totals{margin-left:auto;width:300px;margin-top:18px}.totals p{display:flex;justify-content:space-between}.cude{overflow-wrap:anywhere;font-size:10px;margin-top:18px}</style></head><body><header>${logoUrl ? `<img src="${escape(logoUrl)}" alt="Logo">` : ""}<div><h1>${escape(businessName)}</h1><h2>Nota débito ${escape(value.header.documentNumber)}</h2></div></header><section class="meta"><div class="box"><strong>Cliente</strong><br>${escape(value.header.customerName)}<br>${escape(value.header.customerIdentification)}</div><div class="box"><strong>Factura referenciada</strong><br>${escape(value.header.originalDocumentNumber)}<br>Emitida ${escape(formatDateTime(value.header.issuedAt))}</div><div class="box"><strong>Concepto DIAN</strong><br>${escape(concepts[value.header.conceptCode])}<br>${escape(value.header.reasonDescription)}</div><div class="box"><strong>Vencimiento</strong><br>${escape(formatDateTime(value.dueAt))}<br>Estado DIAN: ${escape(value.header.fiscalStatus)}</div></section><table><thead><tr><th>Descripción</th><th>Cantidad</th><th>Valor</th><th>IVA</th><th>Total</th></tr></thead><tbody>${rows}</tbody></table><section class="totals"><p><span>Base</span><strong>${escape(formatCurrency(value.untaxedAmount))}</strong></p><p><span>Impuesto</span><strong>${escape(formatCurrency(value.taxAmount))}</strong></p><p><span>Total</span><strong>${escape(formatCurrency(value.header.totalAmount))}</strong></p></section>${value.notes ? `<p><strong>Observaciones:</strong> ${escape(value.notes)}</p>` : ""}<p class="cude"><strong>CUDE:</strong> ${escape(value.header.cude ?? "Pendiente de generación")}</p><script>addEventListener('load',()=>setTimeout(()=>window.print(),150))<\/script></body></html>`;
}
