"use client";

import { useEffect, useRef, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { Download, Loader2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { DataTablePagination } from "@/components/tables/data-table-pagination";
import { payrollApi, type PayrollPaymentEmployee, type PayrollPaymentBatch } from "@/services/api/payroll";
import { tenantsApi } from "@/services/api/tenants";
import { useTenantContextStore } from "@/stores/tenant-context-store";
import { useBusinessContextStore } from "@/stores/business-context-store";
import { useBusinessDates } from "@/hooks/use-business-dates";
import { payrollReceiptColumns, payrollReceiptRows } from "@/lib/payroll-receipt";
import { formatCurrency } from "@/lib/utils";

export function PayrollPaidEmployees({ batch, onClose }: { batch: PayrollPaymentBatch; onClose: () => void }) {
  const tenant = useTenantContextStore(state => state.selectedTenantId);
  const business = useBusinessContextStore(state => state.selectedBusinessId);
  const dates = useBusinessDates();
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(20);
  const [stage, setStage] = useState<"loading" | "pdf" | null>(null);
  const [error, setError] = useState<string | null>(null);
  const request = useRef<AbortController | null>(null);
  useEffect(() => () => request.current?.abort(), []);
  const employees = useQuery({ queryKey: ["payroll-payment-employees", tenant, business, batch.paymentBatchId, page, pageSize],
    queryFn: ({ signal }) => payrollApi.paymentEmployees(batch.paymentBatchId, page, pageSize, signal),
    staleTime: Infinity, refetchOnWindowFocus: false, refetchOnReconnect: false });

  async function download(employee: PayrollPaymentEmployee) {
    if (request.current || !employee.isPaid || !employee.employeeName) return;
    const controller = new AbortController(); request.current = controller;
    setStage("loading"); setError(null);
    try {
      const [result] = await Promise.all([
        payrollApi.report("payroll-receipt", employee.periodStart, employee.periodEnd, employee.partyId, employee.payrollRunEmployeeId, controller.signal),
        tenantsApi.getPrintBranding(),
      ]);
      if (controller.signal.aborted) return;
      const brand = tenantsApi.readyLocalPrintBranding();
      if (!brand || brand.tenantId.toLowerCase() !== tenant?.toLowerCase())
        throw new Error("La empresa cambió. Vuelve a abrir el pago para descargar su desprendible.");
      if (!result.rows.length || result.rows.some(row => row.payrollRunEmployeeId !== employee.payrollRunEmployeeId || !row.paidDate)
          || result.rows[0].netPayable !== employee.amount)
        throw new Error("El pago no tiene un resultado contabilizado coincidente. Actualiza los pagos antes de descargar el desprendible.");
      setStage("pdf");
      const { exportReportPdf } = await import("@/lib/report-export");
      if (controller.signal.aborted) return;
      await exportReportPdf({ title: "Desprendible de pago de nómina", brandName: brand.legalName || brand.displayName,
        brandIdentification: brand.nit ? `NIT ${brand.nit}${brand.verificationDigit ? `-${brand.verificationDigit}` : ""}` : undefined,
        logoSource: brand.logoUrl, fileName: `desprendible-${employee.employeeName}-${employee.periodStart}-${employee.periodEnd}`,
        description: `Pago ${dates.formatDate(employee.paymentDate)} · ${batch.paymentMethodName} · Referencia ${batch.referenceNumber}`,
        rows: payrollReceiptRows(result.rows, false), columns: payrollReceiptColumns, groupedHeaders: true, keepRowsTogether: true });
    } catch (failure) {
      if (!controller.signal.aborted) setError(failure instanceof Error ? failure.message : "No fue posible descargar el desprendible.");
    } finally {
      if (request.current === controller) request.current = null;
      if (!controller.signal.aborted) setStage(null);
    }
  }

  return <Dialog open onOpenChange={open => { if (!open) onClose(); }}><DialogContent className="flex max-h-[90dvh] flex-col overflow-hidden sm:max-w-3xl">
    <DialogHeader><DialogTitle>Desprendibles del pago</DialogTitle><DialogDescription>{dates.formatDate(batch.paymentDate)} · {batch.paymentMethodName} · {batch.referenceNumber}</DialogDescription></DialogHeader>
    <div className="min-h-0 space-y-3 overflow-y-auto">
      {employees.isFetching && <p role="status">Cargando trabajadores del pago…</p>}
      {employees.isError && <div role="alert"><p>No fue posible consultar los trabajadores.</p><Button variant="outline" onClick={() => void employees.refetch()}>Reintentar</Button></div>}
      {employees.data?.items.map(employee => <div key={employee.payrollRunEmployeeId} className="flex min-w-0 items-center gap-3 rounded-xl border p-3">
        <div className="min-w-0 flex-1"><strong className="block break-words">{employee.employeeName || "Identidad histórica no disponible"}</strong><p className="text-sm text-muted-foreground">{employee.identification || "Sin identificación histórica"}</p><p className="text-sm text-muted-foreground">{dates.formatDate(employee.periodStart)} — {dates.formatDate(employee.periodEnd)}</p><p className="mt-1 font-semibold tabular-nums">{formatCurrency(employee.amount)}{!employee.isPaid && <span className="ml-2 text-xs font-normal text-muted-foreground">Pendiente de contabilizar</span>}</p></div>
        <Button size="icon" variant="outline" disabled={stage !== null || !employee.isPaid || !employee.employeeName}
          aria-label={`Descargar desprendible de ${employee.employeeName || "trabajador sin identidad"}`} title="Descargar desprendible en PDF" onClick={() => void download(employee)}><Download className="size-4"/></Button>
      </div>)}
      {employees.isSuccess && !employees.data.items.length && <p>No hay trabajadores en este pago.</p>}
      {stage && <p role="status" className="flex items-center gap-2"><Loader2 className="size-4 animate-spin"/>{stage === "loading" ? "Preparando desprendible…" : "Generando PDF…"}</p>}
      {error && <p role="alert" className="text-sm text-destructive">{error}</p>}
    </div>
    {employees.data && employees.data.totalCount > 0 && <DataTablePagination pageIndex={page - 1} pageSize={pageSize} pageCount={employees.data.totalPages} totalItems={employees.data.totalCount}
      onPageChange={index => setPage(index + 1)} onPageSizeChange={size => { setPageSize(size); setPage(1); }}/>}
  </DialogContent></Dialog>;
}
