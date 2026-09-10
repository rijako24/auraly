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

export function workSessionClosureReceiptRequest(
  workSessionId: string,
  companyName?: string | null,
  companyLogoSource?: string | null,
  paperWidthMillimeters = 80,
): { path: string; init: RequestInit } {
  return {
    path: `/api/commerce/v1/work-sessions/${encodeURIComponent(workSessionId)}/closure-receipt`,
    init: {
      method: "POST",
      body: JSON.stringify({ companyName, companyLogoSource, paperWidthMillimeters }),
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
  return new Intl.NumberFormat("es-CO", { maximumFractionDigits: 0 }).format(Number(value));
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
