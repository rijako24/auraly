"use client";

import { useMemo, useState, type FormEvent, type ReactNode } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { AlertTriangle, CheckCircle2, Download, FileUp, Link2, Loader2, RotateCcw, XCircle } from "lucide-react";
import { toast } from "sonner";
import { accountingApi, type BankAccount, type BankReconciliationDetail, type BankReconciliationDetailQuery, type BankReconciliationPage, type BankReconciliationQuery, type BankStatementLineImport } from "@/services/api/accounting";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { DatePicker } from "@/components/ui/date-picker";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Badge } from "@/components/ui/badge";
import { Skeleton } from "@/components/ui/skeleton";
import { DataTablePagination } from "@/components/tables/data-table-pagination";

const money = new Intl.NumberFormat("es-CO", { style: "currency", currency: "COP", maximumFractionDigits: 2 });
const today = new Date();
const monthStart = `${today.getFullYear()}-${String(today.getMonth() + 1).padStart(2, "0")}-01`;
const monthEnd = new Date(today.getFullYear(), today.getMonth() + 1, 0).toISOString().slice(0, 10);
const firstDetailPage:BankReconciliationDetailQuery={statementPage:1,bookPage:1,allocationPage:1,eventPage:1,pageSize:50};

export function BankReconciliationSection({ banks, contextKey }: { banks: BankAccount[]; contextKey: string }) {
  const client = useQueryClient();
  const activeBanks = banks.filter(value => value.isActive);
  const [listQuery,setListQuery]=useState<BankReconciliationQuery>({page:1,pageSize:20,sortBy:"periodTo",sortDirection:"desc"});
  const [listSearch,setListSearch]=useState("");
  const list = useQuery({ queryKey: ["bank-reconciliations",contextKey,listQuery], queryFn: ()=>accountingApi.bankReconciliations(listQuery),staleTime:Infinity });
  const [selectedId, setSelectedId] = useState<string>();
  const [detailQuery,setDetailQuery]=useState(firstDetailPage);
  const detail = useQuery({ queryKey: ["bank-reconciliation",contextKey, selectedId,detailQuery], queryFn: () => accountingApi.bankReconciliation(selectedId!,detailQuery), enabled: Boolean(selectedId),staleTime:Infinity });
  const [reconciliationId, setReconciliationId] = useState(() => crypto.randomUUID());
  const [selectedBankId, setBankAccountId] = useState<string|null>(null);
  const bankAccountId=selectedBankId??activeBanks.find(value=>value.isPrimary)?.bankAccountId??"";
  const [from, setFrom] = useState(monthStart);
  const [to, setTo] = useState(monthEnd);
  const [opening, setOpening] = useState("");
  const [closing, setClosing] = useState("");
  const [file, setFile] = useState<File | null>(null);
  const [preview, setPreview] = useState<BankStatementLineImport[] | null>(null);
  const [fileError, setFileError] = useState("");

  const refresh = async (value?: BankReconciliationDetail) => {
    if (value) {
      await client.invalidateQueries({queryKey:["bank-reconciliation",contextKey,value.summary.reconciliationId],refetchType:"none"});
      client.setQueryData(["bank-reconciliation",contextKey, value.summary.reconciliationId,firstDetailPage], value);
      client.setQueriesData<BankReconciliationPage>({queryKey:["bank-reconciliations",contextKey]},previous=>previous?{
        ...previous,items:previous.items.map(row=>row.reconciliationId===value.summary.reconciliationId?value.summary:row)
      }:previous);
      setDetailQuery(firstDetailPage);
      setSelectedId(value.summary.reconciliationId);
    }
  };
  const selectFile = async (selected: File | null) => {
    setFile(selected);
    setPreview(null);
    setFileError("");
    if (!selected) return;
    try {
      if(selected.size>10_485_760)throw new Error("El extracto supera el máximo de 10 MB.");
      setPreview(parseStatement(new TextDecoder("utf-8", { fatal: true }).decode(await selected.arrayBuffer())));
    } catch (error) {
      setFileError(error instanceof Error ? error.message : "No fue posible leer el extracto.");
    }
  };
  const importer = useMutation({
    mutationFn: async () => {
      if (!file || !preview) throw new Error("Selecciona y revisa el archivo CSV del extracto.");
      const openingBalance = parseInputMoney(opening);
      const closingBalance = parseInputMoney(closing);
      validatePreviewBalances(preview, openingBalance, closingBalance);
      const bytes = new Uint8Array(await file.arrayBuffer());
      const hash = [...new Uint8Array(await crypto.subtle.digest("SHA-256", bytes))].map(value => value.toString(16).padStart(2, "0")).join("");
      return accountingApi.importBankReconciliation({
        reconciliationId, bankAccountId, periodFrom: from, periodTo: to,
        openingBalance, closingBalance, fileName: file.name, fileSha256: hash,
        originalFileBase64: bytesToBase64(bytes), lines: preview,
      });
    },
    onSuccess: async value => {
      await refresh(value);
      // A new period can belong anywhere in the filtered server order. The command
      // returns its detail, so only the current list page needs an authoritative read.
      await client.invalidateQueries({queryKey:["bank-reconciliations",contextKey]});
      setReconciliationId(crypto.randomUUID());
      setFile(null);
      setPreview(null);
      toast.success("Extracto importado y listo para conciliar");
    },
    onError: showError,
  });
  const projectedClosing = preview ? parseInputMoney(opening) + preview.reduce((sum, line) => sum + line.amount, 0) : null;

  return <div className="space-y-5">
    <Card className="rounded-3xl">
      <CardHeader>
        <div className="flex flex-wrap items-center justify-between gap-3"><CardTitle>Conciliación bancaria</CardTitle><Button type="button" variant="outline" onClick={downloadBankTemplate}><Download className="mr-2 h-4 w-4"/>Descargar plantilla</Button></div>
        <p className="text-sm text-muted-foreground">Importa un CSV normalizado con fecha, descripción, referencia, valor y saldo. El valor usa punto decimal, sin separadores de miles: positivo para entradas y negativo para salidas. Se cruza contra todos los auxiliares bancarios vinculados, incluyendo todas las sedes y pendientes anteriores.</p>
      </CardHeader>
      <CardContent className="space-y-4">
        <form className="grid gap-4 md:grid-cols-2 xl:grid-cols-6" onSubmit={event => submit(event, importer.mutate)}>
          <Field label="Cuenta bancaria"><Select value={bankAccountId} onValueChange={value=>{
            // The native form control can emit an empty value while options load.
            // There is no user-facing clear option; preserve the chosen/default bank.
            if(value)setBankAccountId(value);
          }}><SelectTrigger><SelectValue placeholder="Seleccionar cuenta" /></SelectTrigger><SelectContent>{activeBanks.map(bank => <SelectItem key={bank.bankAccountId} value={bank.bankAccountId}>{bank.displayName} · {bank.auxiliaryCount===1?bank.accountingAccountCode:`${bank.auxiliaryCount} auxiliares`}</SelectItem>)}</SelectContent></Select></Field>
          <Field label="Desde"><DatePicker value={from} onChange={setFrom} /></Field>
          <Field label="Hasta"><DatePicker value={to} onChange={setTo} /></Field>
          <Field label="Saldo inicial"><Input inputMode="decimal" value={opening} onChange={event => setOpening(event.target.value)} required /></Field>
          <Field label="Saldo final"><Input inputMode="decimal" value={closing} onChange={event => setClosing(event.target.value)} required /></Field>
          <Field label="Extracto CSV"><Input type="file" accept=".csv,text/csv" onChange={event => void selectFile(event.target.files?.[0] ?? null)} required /></Field>
          <Button className="xl:col-span-6" disabled={importer.isPending || !bankAccountId || !file || !preview}>
            {importer.isPending ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : <FileUp className="mr-2 h-4 w-4" />}Importar extracto revisado
          </Button>
        </form>
        {fileError && <p role="alert" className="rounded-xl border border-red-300 bg-red-50 p-3 text-sm text-red-800">{fileError}</p>}
        {preview && <StatementPreview rows={preview} projectedClosing={projectedClosing ?? 0} declaredClosing={parseInputMoney(closing)} />}
      </CardContent>
    </Card>
    <div className="grid gap-5 xl:grid-cols-[22rem_1fr]">
      <Card className="h-fit rounded-3xl"><CardHeader><CardTitle>Periodos importados</CardTitle></CardHeader><CardContent className="space-y-2">
        <form className="flex gap-2" onSubmit={event=>{event.preventDefault();setListQuery({...listQuery,page:1,search:listSearch.trim()||undefined})}}><Input aria-label="Buscar periodos por banco o archivo" value={listSearch} onChange={event=>setListSearch(event.target.value)}/><Button type="submit" variant="outline">Buscar</Button></form>
        <Select value={listQuery.bankAccountId??"all"} onValueChange={value=>setListQuery({...listQuery,page:1,bankAccountId:value==="all"?undefined:value})}><SelectTrigger aria-label="Filtrar periodos por banco"><SelectValue/></SelectTrigger><SelectContent><SelectItem value="all">Todos los bancos</SelectItem>{banks.map(bank=><SelectItem key={bank.bankAccountId} value={bank.bankAccountId}>{bank.displayName}</SelectItem>)}</SelectContent></Select>
        <Button size="sm" variant="ghost" onClick={()=>setListQuery({...listQuery,page:1,sortDirection:listQuery.sortDirection==="asc"?"desc":"asc"})}>Fecha de corte {listQuery.sortDirection==="asc"?"↑":"↓"}</Button>
        {list.isLoading&&<Skeleton className="h-24 w-full"/>}
        {list.isError&&<div role="alert"><p>No fue posible cargar los periodos.</p><Button variant="outline" onClick={()=>void list.refetch()}>Reintentar periodos</Button></div>}
        {(list.data?.items ?? []).map(row => <button type="button" key={row.reconciliationId} onClick={() => {
          if(selectedId===row.reconciliationId){void detail.refetch();return}
          void client.invalidateQueries({queryKey:["bank-reconciliation",contextKey,row.reconciliationId],refetchType:"none"});
          setDetailQuery(firstDetailPage);setSelectedId(row.reconciliationId);
        }} className={`w-full rounded-2xl border p-3 text-left ${selectedId === row.reconciliationId ? "border-teal-500 bg-teal-50" : "hover:bg-muted/40"}`}>
          <strong className="block">{row.bankDisplayName}</strong><small className="block text-muted-foreground">{row.periodFrom} a {row.periodTo}</small>
          <span className="mt-2 flex items-center justify-between"><Status value={row.status} review={row.requiresReview} /><small>{row.matchedLineCount}/{row.statementLineCount} movimientos</small></span>
        </button>)}
        {!list.isLoading && !list.isError && !list.data?.totalCount && <p className="text-sm text-muted-foreground">{listQuery.search||listQuery.bankAccountId?"No hay periodos con estos filtros.":"Todavía no hay extractos importados."}</p>}
        {list.data&&<DataTablePagination pageIndex={listQuery.page-1} pageSize={listQuery.pageSize} pageCount={Math.ceil(list.data.totalCount/listQuery.pageSize)} totalItems={list.data.totalCount} onPageChange={page=>setListQuery({...listQuery,page:page+1})} onPageSizeChange={pageSize=>setListQuery({...listQuery,page:1,pageSize})}/>}
      </CardContent></Card>
      {selectedId ? <ReconciliationDetail key={selectedId} value={detail.data} loading={detail.isLoading} error={detail.error} retry={()=>void detail.refetch()} query={detailQuery} changeQuery={setDetailQuery} refresh={refresh} /> : <Card className="rounded-3xl"><CardContent className="p-12 text-center text-muted-foreground">Selecciona una conciliación para revisar sus cruces.</CardContent></Card>}
    </div>
  </div>;
}

function StatementPreview({ rows, projectedClosing, declaredClosing }: { rows: BankStatementLineImport[]; projectedClosing: number; declaredClosing: number }) {
  const matches = Math.abs(projectedClosing - declaredClosing) < .0001;
  return <div className="overflow-hidden rounded-2xl border">
    <div className="flex flex-wrap items-center justify-between gap-2 bg-muted/40 p-3"><strong>Previsualización · {rows.length} movimientos</strong><span className={matches ? "text-emerald-700" : "text-red-700"}>Saldo calculado {money.format(projectedClosing)} · declarado {money.format(declaredClosing)}</span></div>
    {rows.length>50&&<p className="px-3 py-2 text-sm text-muted-foreground">Muestra de las primeras 50 filas. Se validan e importan todos los movimientos del archivo.</p>}
    <div className="max-h-56 overflow-auto"><table className="w-full text-sm"><thead className="sticky top-0 bg-background"><tr><th className="p-2 text-left">Fecha</th><th className="text-left">Descripción</th><th className="text-left">Referencia</th><th className="text-right">Valor</th><th className="pr-2 text-right">Saldo</th></tr></thead><tbody>{rows.slice(0,50).map(row => <tr key={row.lineNumber} className="border-t"><td className="whitespace-nowrap p-2">{row.transactionDate}</td><td>{row.description}</td><td>{row.reference ?? "—"}</td><td className="text-right">{money.format(row.amount)}</td><td className="pr-2 text-right">{row.balance == null ? "—" : money.format(row.balance)}</td></tr>)}</tbody></table></div>
  </div>;
}

function ReconciliationDetail({ value, loading, error, retry, query, changeQuery, refresh }: { value?: BankReconciliationDetail; loading: boolean; error:Error|null; retry:()=>void; query:BankReconciliationDetailQuery;changeQuery:(query:BankReconciliationDetailQuery)=>void; refresh: (value?: BankReconciliationDetail) => Promise<void> }) {
  const [statementId, setStatementId] = useState("");
  const [bookKey, setBookKey] = useState("");
  const [amount, setAmount] = useState("");
  const [reason, setReason] = useState("");
  const [statementSearch,setStatementSearch]=useState(query.statementSearch??"");
  const [bookSearch,setBookSearch]=useState(query.bookSearch??"");
  const movePage=(field:"statementPage"|"bookPage"|"allocationPage"|"eventPage",page:number)=>{
    setStatementId("");setBookKey("");setAmount("");changeQuery({...query,[field]:page});
  };
  const pager=(field:"statementPage"|"bookPage"|"allocationPage"|"eventPage",count:number)=>count>0&&<DataTablePagination pageIndex={query[field]-1} pageSize={query.pageSize} pageCount={Math.ceil(count/query.pageSize)} totalItems={count} onPageChange={page=>movePage(field,page+1)} onPageSizeChange={pageSize=>{setStatementId("");setBookKey("");changeQuery({...query,...firstDetailPage,pageSize})}}/>;
  const statement = value?.statementLines.find(line => line.statementLineId === statementId);
  const book = value?.bookLines.find(line => `${line.entryId}:${line.entryLineNumber}` === bookKey);
  const availableStatement = statement ? Math.abs(statement.amount) - statement.allocatedAmount : 0;
  const availableBook = book?.availableAmount ?? 0;
  const allocate = useMutation({ mutationFn: () => accountingApi.allocateBankReconciliation(value!.summary.reconciliationId, { matchId: crypto.randomUUID(), statementLineId: statementId, entryId: book!.entryId, entryLineNumber: book!.entryLineNumber, amount: parseInputMoney(amount), rowVersion: value!.summary.rowVersion }), onSuccess: async result => { await refresh(result); setStatementId(""); setBookKey(""); setAmount(""); toast.success("Movimiento conciliado"); }, onError: showError });
  const close = useMutation({ mutationFn: () => accountingApi.closeBankReconciliation(value!.summary.reconciliationId, value!.summary.rowVersion), onSuccess: async result => { await refresh(result); toast.success("Conciliación cerrada y cuadrada"); }, onError: showError });
  const reopen = useMutation({ mutationFn: () => accountingApi.reopenBankReconciliation(value!.summary.reconciliationId, value!.summary.rowVersion, reason), onSuccess: async result => { await refresh(result); setReason(""); toast.success("Conciliación reabierta con trazabilidad"); }, onError: showError });
  const reverse = useMutation({ mutationFn: (matchId: string) => accountingApi.reverseBankReconciliationAllocation(value!.summary.reconciliationId, matchId, "Corrección del cruce", value!.summary.rowVersion), onSuccess: refresh, onError: showError });
  const exactCandidates = useMemo(() => {
    // A unique match on one page may be ambiguous elsewhere in the same bank.
    // Only offer this bounded convenience when both complete sets fit in the response.
    if (!value || query.statementSearch || query.bookSearch ||
      value.paging.statementCount!==value.statementLines.length || value.paging.bookCount!==value.bookLines.length) return [];
    const used = new Set<string>();
    return value.statementLines.filter(line => !line.isMatched).flatMap(line => {
      const matches = value.bookLines.filter(bookLine => bookLine.availableAmount === Math.abs(line.amount) && Math.sign(bookLine.amount) === Math.sign(line.amount) && !used.has(`${bookLine.entryId}:${bookLine.entryLineNumber}`));
      if (matches.length !== 1) return [];
      const match = matches[0]; used.add(`${match.entryId}:${match.entryLineNumber}`); return [{ line, match }];
    });
  }, [value,query.statementSearch,query.bookSearch]);
  const auto = useMutation({ mutationFn: async () => {
    let current=value!;
    try {
      // At most 100 explicit, independent matches; each command returns its committed state.
      for(const pair of exactCandidates)current=await accountingApi.allocateBankReconciliation(current.summary.reconciliationId,{matchId:crypto.randomUUID(),statementLineId:pair.line.statementLineId,entryId:pair.match.entryId,entryLineNumber:pair.match.entryLineNumber,amount:Math.abs(pair.line.amount),rowVersion:current.summary.rowVersion});
      return current;
    }catch(error){await refresh(current);throw error;}
  },onSuccess:async result=>{await refresh(result);toast.success("Cruces exactos aplicados");},onError:showError});
  if (loading) return <Card><CardContent className="space-y-4 p-6"><Skeleton className="h-12 w-full"/><Skeleton className="h-64 w-full"/></CardContent></Card>;
  if (error||!value) return <Card><CardContent role="alert" className="space-y-3 p-12 text-center"><p>{error?.message??"No fue posible cargar la conciliación."}</p><Button variant="outline" onClick={retry}>Reintentar conciliación</Button></CardContent></Card>;
  const open = value.summary.status !== "Closed";
  const complete = value.summary.matchedLineCount===value.summary.statementLineCount && value.paging.unmatchedBookCount===0 && Math.abs(value.summary.difference) < .0001;
  return <Card className="overflow-hidden rounded-3xl"><CardHeader className="border-b">
    <div className="flex flex-wrap items-start justify-between gap-3"><div><CardTitle>{value.summary.bankDisplayName}</CardTitle><p className="text-sm text-muted-foreground">{value.summary.accountingAccountCode?`${value.summary.accountingAccountCode} · ${value.summary.accountingAccountName}`:"Todos los auxiliares bancarios vinculados"} · {value.summary.periodFrom} a {value.summary.periodTo}</p></div><Status value={value.summary.status} review={value.summary.requiresReview} /></div>
    {value.summary.requiresReview && <p role="alert" className="mt-3 flex gap-2 rounded-xl border border-amber-300 bg-amber-50 p-3 text-sm text-amber-900"><AlertTriangle className="h-4 w-4 shrink-0" />Existen movimientos contables registrados después del cierre con fecha dentro de este corte. Reabre y revisa la conciliación.</p>}
    <div className="mt-3 grid gap-2 sm:grid-cols-4"><Total label="Saldo inicial" value={value.summary.openingBalance} /><Total label="Movimientos extracto" value={value.summary.statementMovementTotal} /><Total label="Saldo final" value={value.summary.closingBalance} /><Total label="Diferencia con libros" value={value.summary.difference} alert={Math.abs(value.summary.difference) > .0001} /></div>
  </CardHeader><CardContent className="space-y-5 p-5">
    {open && <div className="rounded-2xl border bg-muted/20 p-4"><div className="grid gap-3 lg:grid-cols-[1fr_1fr_10rem_auto]"><Field label="Movimiento del extracto"><Select value={statementId} onValueChange={id => { setStatementId(id); const line = value.statementLines.find(item => item.statementLineId === id); if (line) setAmount(String(Math.abs(line.amount) - line.allocatedAmount)); }}><SelectTrigger><SelectValue placeholder="Seleccionar pendiente" /></SelectTrigger><SelectContent>{value.statementLines.filter(line => !line.isMatched).map(line => <SelectItem key={line.statementLineId} value={line.statementLineId}>{line.transactionDate} · {money.format(line.amount)} · {line.description}</SelectItem>)}</SelectContent></Select></Field><Field label="Movimiento contable"><Select value={bookKey} onValueChange={setBookKey}><SelectTrigger><SelectValue placeholder="Seleccionar partida" /></SelectTrigger><SelectContent>{value.bookLines.filter(line => line.availableAmount > 0 && (!statement || Math.sign(line.amount) === Math.sign(statement.amount))).map(line => <SelectItem key={`${line.entryId}:${line.entryLineNumber}`} value={`${line.entryId}:${line.entryLineNumber}`}>{line.businessName} · {line.entryNumber} · {money.format(line.amount)} · {line.description}</SelectItem>)}</SelectContent></Select></Field><Field label="Valor a cruzar"><Input inputMode="decimal" value={amount} onChange={event => setAmount(event.target.value)} /></Field><Button className="self-end" disabled={allocate.isPending || !statement || !book || parseInputMoney(amount) <= 0 || parseInputMoney(amount) > Math.min(availableStatement, availableBook)} onClick={() => allocate.mutate()}><Link2 className="mr-2 h-4 w-4" />Cruzar</Button></div>{exactCandidates.length > 0 && <Button className="mt-3" variant="outline" disabled={auto.isPending} onClick={() => auto.mutate()}>{auto.isPending && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}Cruzar {exactCandidates.length} coincidencias exactas</Button>}</div>}
    <div className="grid gap-5 2xl:grid-cols-2"><div className="min-w-0 space-y-2"><form className="flex gap-2" onSubmit={event=>{event.preventDefault();setStatementId("");changeQuery({...query,statementPage:1,statementSearch:statementSearch.trim()||undefined})}}><Input aria-label="Buscar en todo el extracto" value={statementSearch} onChange={event=>setStatementSearch(event.target.value)}/><Button type="submit" variant="outline">Buscar extracto</Button></form><MovementTable title="Extracto bancario" rows={value.statementLines.map(line => ({ id: line.statementLineId, date: line.transactionDate, description: line.description, amount: line.amount, matched: line.isMatched }))} />{pager("statementPage",value.paging.statementCount)}</div>
    <div className="min-w-0 space-y-2"><form className="flex gap-2" onSubmit={event=>{event.preventDefault();setBookKey("");changeQuery({...query,bookPage:1,bookSearch:bookSearch.trim()||undefined})}}><Input aria-label="Buscar en todo el libro bancario" value={bookSearch} onChange={event=>setBookSearch(event.target.value)}/><Button type="submit" variant="outline">Buscar libro</Button></form><MovementTable title="Libro auxiliar de todas las sedes" rows={value.bookLines.map(line => ({ id: `${line.entryId}:${line.entryLineNumber}`, date: new Date(line.occurredAt).toLocaleDateString("es-CO"), description: `${line.businessName} · ${line.entryNumber} · ${line.description}`, amount: line.amount, matched: line.availableAmount === 0 }))} />{pager("bookPage",value.paging.bookCount)}</div></div>
    {value.paging.allocationCount > 0 && <div><h3 className="mb-2 font-semibold">Cruces vigentes</h3><div className="space-y-2">{value.allocations.map(item => <div key={item.matchId} className="flex items-center justify-between rounded-xl border p-3 text-sm"><span>{money.format(item.amount)} · {new Date(item.createdAt).toLocaleString("es-CO")}</span>{open && <Button size="sm" variant="ghost" disabled={reverse.isPending} onClick={() => reverse.mutate(item.matchId)}><XCircle className="mr-2 h-4 w-4" />Reversar</Button>}</div>)}</div>{pager("allocationPage",value.paging.allocationCount)}</div>}
    {value.paging.eventCount > 0 && <div><h3 className="mb-2 font-semibold">Historial de cierre</h3><div className="space-y-2">{value.statusEvents.map(event => <div key={event.statusEventId} className="rounded-xl border p-3 text-sm"><strong>{event.status === "Closed" ? "Cierre" : "Reapertura"}</strong> · {new Date(event.occurredAt).toLocaleString("es-CO")} · {event.actorName}{event.reason ? ` · ${event.reason}` : ""}{event.bookClosingBalance != null ? ` · libro ${money.format(event.bookClosingBalance)}` : ""}</div>)}</div>{pager("eventPage",value.paging.eventCount)}</div>}
    <div className="flex flex-wrap items-end justify-end gap-3">{value.summary.status === "Closed" ? <><Field label="Motivo de reapertura"><Input value={reason} onChange={event => setReason(event.target.value)} placeholder="Explica la corrección" /></Field><Button variant="outline" disabled={reopen.isPending || !reason.trim()} onClick={() => reopen.mutate()}><RotateCcw className="mr-2 h-4 w-4" />Reabrir</Button></> : <Button disabled={!complete || close.isPending} onClick={() => close.mutate()}><CheckCircle2 className="mr-2 h-4 w-4" />Cerrar conciliación</Button>}</div>
  </CardContent></Card>;
}

function MovementTable({ title, rows }: { title: string; rows: Array<{ id: string; date: string; description: string; amount: number; matched: boolean }> }) { return <div className="overflow-hidden rounded-2xl border"><h3 className="bg-muted/50 p-3 font-semibold">{title}</h3><div className="max-h-80 overflow-auto"><table className="w-full text-sm"><thead className="sticky top-0 bg-background"><tr><th className="p-2 text-left">Fecha</th><th className="text-left">Descripción</th><th className="text-right">Valor</th><th className="pr-2 text-right">Estado</th></tr></thead><tbody>{rows.map(row => <tr key={row.id} className="border-t"><td className="whitespace-nowrap p-2">{row.date}</td><td>{row.description}</td><td className="text-right">{money.format(row.amount)}</td><td className="pr-2 text-right">{row.matched ? <span className="text-emerald-700">Conciliado</span> : <span className="text-amber-700">Pendiente</span>}</td></tr>)}</tbody></table>{!rows.length && <p className="p-5 text-center text-muted-foreground">Sin movimientos.</p>}</div></div>; }
function Status({ value, review = false }: { value: string; review?: boolean }) { const label = review ? "Requiere revisión" : value === "Closed" ? "Cerrada" : value === "Reopened" ? "Reabierta" : "En preparación"; return <Badge variant={value === "Closed" && !review ? "secondary" : "outline"}>{label}</Badge>; }
function Total({ label, value, alert = false }: { label: string; value: number; alert?: boolean }) { return <div className={`rounded-xl border p-3 ${alert ? "border-amber-400 bg-amber-50" : ""}`}><small className="text-muted-foreground">{label}</small><strong className="block">{money.format(value)}</strong></div>; }
function Field({ label, children }: { label: string; children: ReactNode }) { return <div className="space-y-2"><Label>{label}</Label>{children}</div>; }
function submit(event: FormEvent, mutate: () => void) { event.preventDefault(); mutate(); }
function showError(error: unknown) { toast.error(error instanceof Error ? error.message : "No fue posible completar la operación"); }
function bytesToBase64(bytes: Uint8Array) { let binary = ""; for (let index = 0; index < bytes.length; index += 0x8000) binary += String.fromCharCode(...bytes.subarray(index, index + 0x8000)); return btoa(binary); }
function downloadBankTemplate(){downloadCsv("plantilla-conciliacion-bancaria.csv","fecha;descripcion;referencia;valor;saldo\r\n");}
function downloadCsv(name:string,value:string){const url=URL.createObjectURL(new Blob(["\uFEFF",value],{type:"text/csv;charset=utf-8"}));const link=document.createElement("a");link.href=url;link.download=name;link.click();URL.revokeObjectURL(url);}
function parseInputMoney(value: string) { const clean = value.trim().replace(/\s/g, ""); if (!clean) return 0; if (clean.includes(",") && clean.includes(".")) { const decimal = clean.lastIndexOf(",") > clean.lastIndexOf(".") ? "," : "."; return Number(clean.replace(decimal === "," ? /\./g : /,/g, "").replace(decimal, ".")); } return Number(clean.replace(",", ".")); }
function parseCsvMoney(value: string, lineNumber: number, field: string) { const clean = value.trim(); if (!/^-?\d+(?:\.\d{1,4})?$/.test(clean)) throw new Error(`La fila ${lineNumber} tiene ${field} ambiguo. Usa punto decimal y no uses separadores de miles.`); const parsed = Number(clean); if (!Number.isFinite(parsed)) throw new Error(`La fila ${lineNumber} tiene ${field} inválido.`); return parsed; }
function parseStatement(text: string): BankStatementLineImport[] {
  const rows = text.replace(/^\uFEFF/, "").split(/\r?\n/).filter(Boolean);
  if(rows.length>100_001)throw new Error("El extracto admite hasta 100.000 movimientos.");
  if (rows.length < 2) throw new Error("El CSV debe contener encabezado y al menos un movimiento.");
  const delimiter = rows[0].includes(";") ? ";" : ",";
  const header = splitCsv(rows[0], delimiter).map(value => value.trim().toLocaleLowerCase("es-CO"));
  const pick = (names: string[]) => names.map(name => header.indexOf(name)).find(index => index >= 0) ?? -1;
  const fecha = pick(["fecha", "date"]), descripcion = pick(["descripción", "descripcion", "description"]), referencia = pick(["referencia", "reference"]), valor = pick(["valor", "importe", "amount"]), saldo = pick(["saldo", "balance"]);
  if (fecha < 0 || descripcion < 0 || valor < 0) throw new Error("El CSV requiere las columnas fecha, descripción y valor.");
  return rows.slice(1).map((row, index) => {
    const cells = splitCsv(row, delimiter), sourceLine = index + 2;
    const transactionDate = normalizeDate(cells[fecha]);
    const description = (cells[descripcion] ?? "").trim();
    const amount = parseCsvMoney(cells[valor] ?? "", sourceLine, "un valor");
    if (!transactionDate || !description || amount === 0) throw new Error(`La fila ${sourceLine} tiene fecha, descripción o valor inválido.`);
    return { lineNumber: index + 1, transactionDate, description, reference: referencia >= 0 ? (cells[referencia] ?? "").trim() || null : null, amount, balance: saldo >= 0 && cells[saldo]?.trim() ? parseCsvMoney(cells[saldo], sourceLine, "un saldo") : null };
  });
}
function validatePreviewBalances(rows: BankStatementLineImport[], opening: number, closing: number) { if (!Number.isFinite(opening) || !Number.isFinite(closing)) throw new Error("Los saldos inicial y final deben ser valores numéricos válidos."); let running = opening; const hasBalances = rows.some(row => row.balance != null); if (hasBalances && rows.some(row => row.balance == null)) throw new Error("El saldo por movimiento debe venir completo o no incluirse."); for (const row of rows) { if (!Number.isFinite(row.amount) || (row.balance != null && !Number.isFinite(row.balance))) throw new Error(`La línea ${row.lineNumber} contiene un valor inválido.`); running += row.amount; if (row.balance != null && Math.abs(row.balance - running) > .0001) throw new Error(`El saldo de la línea ${row.lineNumber} no corresponde al movimiento.`); } if (Math.abs(running - closing) > .0001) throw new Error("El saldo inicial, los movimientos y el saldo final no cuadran."); }
function splitCsv(row: string, delimiter: string) { const result: string[] = [], current: string[] = []; let quoted = false; for (let index = 0; index < row.length; index++) { const char = row[index]; if (char === '"') { if (quoted && row[index + 1] === '"') { current.push('"'); index++; } else quoted = !quoted; } else if (char === delimiter && !quoted) { result.push(current.join("")); current.length = 0; } else current.push(char); } if (quoted) throw new Error("El CSV contiene una comilla sin cerrar."); result.push(current.join("")); return result; }
function normalizeDate(value: string | undefined) { const text = value?.trim() ?? ""; if (/^\d{4}-\d{2}-\d{2}$/.test(text)) return text; const match = text.match(/^(\d{1,2})[\/-](\d{1,2})[\/-](\d{4})$/); return match ? `${match[3]}-${match[2].padStart(2, "0")}-${match[1].padStart(2, "0")}` : ""; }
