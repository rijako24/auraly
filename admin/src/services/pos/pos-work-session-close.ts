import { printPosHtmlDocument } from "./pos-browser-print";
import { posReceiptTypographyCss } from "./pos-receipt-style";

export function workSessionClosurePreviewPath(workSessionId: string): string {
  return `/api/commerce/v1/work-sessions/${encodeURIComponent(workSessionId)}/closure-preview`;
}

export function workSessionClosurePreviewRequest(
  workSessionId: string,
  draftId: string,
  approvalRequestId?: string,
  operationId?: string,
): { path: string; init: RequestInit } {
  return {
    path: workSessionClosurePreviewPath(workSessionId),
    init: {
      headers: {
        "X-Auraly-Draft-Id": draftId,
        ...(approvalRequestId ? { "X-Auraly-Approval-Id": approvalRequestId } : {}),
        ...(operationId ? { "X-Auraly-Operation-Id": operationId } : {}),
      },
    },
  };
}

export function workSessionCloseRequest(
  workSessionId: string,
  operationId: string,
  draftId: string,
  approvalRequestId: string | undefined,
  countedCash: number,
  paymentCounts: Array<{ paymentMethodCode: string; countedAmount: number }>,
  note: string | null,
): { path: string; init: RequestInit } {
  return {
    path: `/api/commerce/v1/work-sessions/${encodeURIComponent(workSessionId)}/close`,
    init: {
      method: "POST",
      headers: {
        "Idempotency-Key": operationId,
        "X-Auraly-Draft-Id": draftId,
        ...(approvalRequestId ? { "X-Auraly-Approval-Id": approvalRequestId } : {}),
      },
      body: JSON.stringify({ countedCash, paymentCounts, note }),
    },
  };
}

const paymentMethodNames: Record<string, string> = {
  Cash: "Efectivo",
  DebitCard: "Tarjeta débito",
  CreditCard: "Tarjeta crédito",
  Card: "Tarjeta",
  Transfer: "Transferencia",
  Credit: "Crédito / cartera",
  Voucher: "Bono / vale",
  Check: "Cheque",
  Withholding: "Retención",
};

export function workSessionPaymentMethodName(code: string): string {
  return paymentMethodNames[code] ?? code;
}

export function workSessionPaymentMethodRequiresCount(code: string): boolean {
  return code === "Cash" || code === "Card" || code === "Transfer";
}

export function normalizeWorkSessionCountInput(value: string): string {
  return value.replace(/\D/g, "").replace(/^0+(?=\d)/, "");
}

export function formatWorkSessionCountInput(value: string): string {
  if (!value) return "";
  return new Intl.NumberFormat("es-CO", { maximumFractionDigits: 0 })
    .format(Number(value));
}

type ClosureForPrint = {
  companyName?: string | null;
  logoUrl?: string | null;
  workSessionClosureId?: string;
  businessName: string; warehouseName: string | null; userName: string; openedAt: string; closedAt: string;
  totalSales: number; totalRefunds: number; totalOther: number; netAmount: number;
  salesCount?: number; creditSalesCount?: number; creditSalesAmount?: number; returnCount?: number;
  expectedCash: number; countedCash: number | null; cashDifference: number | null; note: string | null;
  paymentTotals: Array<{ paymentMethodCode: string; salesAmount?: number; refundAmount?: number; otherAmount?: number; netAmount: number; countedAmount?: number | null; difference?: number | null }>;
};

export function workSessionClosureHtml(value: ClosureForPrint): string {
  const money = (amount: number | null) => amount == null ? "—" : new Intl.NumberFormat("es-CO", { style: "currency", currency: "COP", maximumFractionDigits: 0 }).format(amount);
  const row = (label: string, amount: number | null) => `<div><span>${escapeHtml(label)}</span><strong>${escapeHtml(money(amount))}</strong></div>`;
  const differenceRow = (amount: number | null) => {
    if (amount == null) return `<div class="cash-difference"><span>Sin conteo</span><strong>—</strong></div>`;
    const result = amount > 0 ? "Sobrante" : amount < 0 ? "Faltante" : "Cuadra";
    return `<div class="cash-difference"><span>${result}</span><strong>${escapeHtml(money(Math.abs(amount)))}</strong></div>`;
  };
  const dateTime = (value: string) => new Intl.DateTimeFormat("es-CO", {
    day: "2-digit", month: "2-digit", year: "numeric", hour: "2-digit", minute: "2-digit", hour12: false,
  }).format(new Date(value));
  const duration = (() => {
    const totalMinutes = Math.max(0, Math.floor((new Date(value.closedAt).getTime() - new Date(value.openedAt).getTime()) / 60_000));
    const days = Math.floor(totalMinutes / 1440);
    const hours = Math.floor((totalMinutes % 1440) / 60);
    const minutes = totalMinutes % 60;
    return `${days}d ${String(hours).padStart(2, "0")}h ${String(minutes).padStart(2, "0")}m`;
  })();
  const cashEntries = value.paymentTotals.reduce((sum, item) => sum + Math.max(0, item.otherAmount ?? 0), 0);
  const cashExits = value.paymentTotals.reduce((sum, item) => sum + Math.abs(Math.min(0, item.otherAmount ?? 0)), 0);
  const paymentRows = value.paymentTotals.map(item => {
    const name = workSessionPaymentMethodName(item.paymentMethodCode);
    return `<section class="payment" data-payment-method="${escapeHtml(item.paymentMethodCode)}"><h3>${escapeHtml(name)}</h3>` +
    row("Ventas", item.salesAmount ?? 0) +
    row("Devoluciones", item.refundAmount ?? 0) +
    (item.paymentMethodCode === "Cash"
      ? row("Entradas", Math.max(0, item.otherAmount ?? 0)) +
        row("Salidas", Math.abs(Math.min(0, item.otherAmount ?? 0)))
      : "") + `</section>`;
  }).join("");
  const logo = value.logoUrl ? `<img src="${escapeHtml(value.logoUrl)}" alt="Logo" />` : "";
  const companyName = value.companyName || value.businessName;
  const location = value.warehouseName
    ? `${value.businessName} · ${value.warehouseName}`
    : value.businessName;
  return `<!doctype html><html lang="es"><head><meta charset="utf-8"><title>Arqueo de caja</title><style>@page{size:80mm auto;margin:4mm}*{box-sizing:border-box}${posReceiptTypographyCss}body{width:72mm;margin:0 auto;font:11px/1.35 ui-monospace,Consolas,monospace;color:#111}header{border-bottom:1px dashed #555;padding-bottom:8px}header img{display:block;max-height:18mm;max-width:48mm;object-fit:contain;margin:0 auto 3mm}h1{margin:3px 0;font:800 19px/1.2 Arial,sans-serif;text-transform:uppercase}.document-title{margin:7px 0 4px;font-size:12px;text-transform:uppercase}h2{margin:12px 0 5px;font-size:11px;border-bottom:1px dashed #555;padding-bottom:4px;text-transform:uppercase}h3{margin:0 0 3px;font-size:11px;text-transform:uppercase}.session-meta{text-align:left}.totals div,.payments div{display:flex;justify-content:space-between;gap:8px;padding:2px 0}.totals strong,.payments strong{margin-left:auto;text-align:right;font-variant-numeric:tabular-nums}.count-row{font-size:13px;font-weight:800}.payment{border-top:1px dashed #777;padding:7px 0 5px}.payment:last-child{border-bottom:1px dashed #777}.payment[data-payment-method="Cash"] h3{font-weight:800}.cash-difference{margin-top:10px;border:2px solid #111;padding:7px!important;font-size:15px;font-weight:800}.note{margin-top:10px;padding:7px;border:1px dashed #555}@media screen{body{margin:16px auto;padding:4mm;box-shadow:0 8px 30px #0002}}@media print{body{padding:0;box-shadow:none}}</style></head><body><header>${logo}<h1>${escapeHtml(companyName)}</h1><p class="document-title"><strong>Arqueo de caja · Cierre confirmado</strong></p><p>Sede: ${escapeHtml(location)}</p><p class="session-meta">Usuario que trabajó: <strong>${escapeHtml(value.userName)}</strong><br>Apertura: <strong>${escapeHtml(dateTime(value.openedAt))}</strong><br>Cierre: <strong>${escapeHtml(dateTime(value.closedAt))}</strong><br>Duración: <strong>${escapeHtml(duration)}</strong></p></header><h2>Actividad del turno</h2><section class="totals"><div class="count-row"><span>Número de ventas</span><strong>${value.salesCount ?? 0}</strong></div><div class="count-row"><span>Ventas a cartera</span><strong>${value.creditSalesCount ?? 0}</strong></div><div class="count-row"><span>Devoluciones</span><strong>${value.returnCount ?? 0}</strong></div></section><h2>Detalle por medio de pago</h2><section class="payments">${paymentRows || "<p>Sin movimientos</p>"}</section><h2>Totales del turno</h2><section class="totals">${row("Ventas", value.totalSales)}${row("Devoluciones", value.totalRefunds)}${row("Valor a cartera", value.creditSalesAmount ?? 0)}${row("Entradas de caja", cashEntries)}${row("Salidas de caja", cashExits)}${row("Efectivo esperado", value.expectedCash)}${row("Efectivo contado", value.countedCash)}${differenceRow(value.cashDifference)}</section>${value.note ? `<p class="note"><strong>Observación:</strong> ${escapeHtml(value.note)}</p>` : ""}</body></html>`;
}

export function printWorkSessionClosure(html: string): Promise<void> {
  return printPosHtmlDocument(
    html,
    "No fue posible abrir el diálogo de impresión del arqueo.",
  );
}

export function cashDenominationCountHtml(value: {
  businessName: string;
  userName: string;
  countedAt: string;
  lines: Array<{ label: string; value: number; quantity: number; subtotal: number }>;
  total: number;
}): string {
  const money = (amount: number) => new Intl.NumberFormat("es-CO", {
    style: "currency", currency: "COP", maximumFractionDigits: 0,
  }).format(amount);
  const rows = value.lines.map(line => `<tr><td>${escapeHtml(line.label)}</td><td>${line.quantity}</td><td>${escapeHtml(money(line.value))}</td><th>${escapeHtml(money(line.subtotal))}</th></tr>`).join("");
  const countedAt = new Intl.DateTimeFormat("es-CO", {
    day: "2-digit", month: "2-digit", year: "numeric",
    hour: "2-digit", minute: "2-digit", hour12: false,
  }).format(new Date(value.countedAt));
  return `<!doctype html><html lang="es"><head><meta charset="utf-8"><title>Conteo de efectivo</title><style>@page{size:80mm auto;margin:4mm}*{box-sizing:border-box}${posReceiptTypographyCss}body{width:72mm;margin:0 auto;font:11px/1.4 ui-monospace,Consolas,monospace;color:#111}header{border-bottom:1px dashed #555;padding-bottom:7px}h1{margin:0;font:800 15px/1.2 Arial,sans-serif}p{margin:3px 0}table{width:100%;border-collapse:collapse;margin-top:8px}th,td{padding:4px 1px;border-bottom:1px dashed #999;text-align:left}td:nth-child(n+2),th{text-align:right}.total{display:flex;justify-content:space-between;border-block:2px solid #111;margin-top:10px;padding:8px 0;font-size:14px;font-weight:800}@media screen{body{margin:16px auto;padding:4mm;box-shadow:0 8px 30px #0002}}</style></head><body><header><h1>${escapeHtml(value.businessName)}</h1><p><strong>Conteo de efectivo</strong></p><p>Responsable: <strong>${escapeHtml(value.userName)}</strong></p><p>Fecha: <strong>${escapeHtml(countedAt)}</strong></p></header><table><thead><tr><th>Denom.</th><th>Cant.</th><th>Valor</th><th>Subtotal</th></tr></thead><tbody>${rows || "<tr><td colspan=4>Sin valores contados</td></tr>"}</tbody></table><div class="total"><span>Total contado</span><strong>${escapeHtml(money(value.total))}</strong></div></body></html>`;
}

export function printCashDenominationCount(html: string): Promise<void> {
  return printPosHtmlDocument(
    html,
    "No fue posible abrir la impresión del conteo de efectivo.",
  );
}

function escapeHtml(value: string): string {
  return value.replaceAll("&", "&amp;").replaceAll("<", "&lt;").replaceAll(">", "&gt;").replaceAll('"', "&quot;").replaceAll("'", "&#39;");
}
