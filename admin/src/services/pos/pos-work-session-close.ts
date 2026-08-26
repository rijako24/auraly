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
  Deposit: "Consignación",
  Credit: "Crédito / cartera",
  Voucher: "Bono / vale",
  Check: "Cheque",
  Withholding: "Retención",
};

export function workSessionPaymentMethodName(code: string): string {
  return paymentMethodNames[code] ?? code;
}

export function workSessionPaymentMethodRequiresCount(code: string): boolean {
  return code === "Cash" || code === "Card" || code === "DebitCard" || code === "CreditCard";
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
  businessName: string; warehouseName: string; userName: string; openedAt: string; closedAt: string;
  totalSales: number; totalRefunds: number; totalOther: number; netAmount: number;
  expectedCash: number; countedCash: number | null; cashDifference: number | null; note: string | null;
  paymentTotals: Array<{ paymentMethodCode: string; salesAmount?: number; refundAmount?: number; otherAmount?: number; netAmount: number; countedAmount?: number | null; difference?: number | null }>;
};

export function workSessionClosureHtml(value: ClosureForPrint): string {
  const money = (amount: number | null) => amount == null ? "—" : new Intl.NumberFormat("es-CO", { style: "currency", currency: "COP", maximumFractionDigits: 0 }).format(amount);
  const row = (label: string, amount: number | null) => `<div><span>${escapeHtml(label)}</span><strong>${escapeHtml(money(amount))}</strong></div>`;
  const paymentRows = value.paymentTotals.map(item => {
    const name = workSessionPaymentMethodName(item.paymentMethodCode);
    return `<h3>${escapeHtml(name)}</h3>` +
    row("Ventas", item.salesAmount ?? 0) +
    row("Devoluciones", item.refundAmount ?? 0) +
    row("Otros movimientos", item.otherAmount ?? 0) +
    row("Esperado", item.netAmount) +
    (workSessionPaymentMethodRequiresCount(item.paymentMethodCode)
      ? row("Contado", item.countedAmount ?? null) + row("Diferencia", item.difference ?? null)
      : `<div><span>Conciliación</span><strong>Automática</strong></div>`);
  }).join("");
  const logo = value.logoUrl ? `<img src="${escapeHtml(value.logoUrl)}" alt="Logo" />` : "";
  const closureId = value.workSessionClosureId ? `<p class="id">Cierre: ${escapeHtml(value.workSessionClosureId)}</p>` : "";
  const companyName = value.companyName || value.businessName;
  return `<!doctype html><html lang="es"><head><meta charset="utf-8"><title>Arqueo de caja</title><style>@page{size:80mm auto;margin:4mm}*{box-sizing:border-box}body{width:72mm;margin:0 auto;font:11px/1.35 ui-monospace,Consolas,monospace;color:#111}header{text-align:center;border-bottom:1px dashed #555;padding-bottom:8px}header img{display:block;max-height:18mm;max-width:48mm;object-fit:contain;margin:0 auto 3mm}h1{margin:3px 0;font:800 15px/1.2 Arial,sans-serif}h2{margin:10px 0 4px;font-size:11px;text-transform:uppercase;border-bottom:1px dashed #555;padding-bottom:4px}h3{margin:8px 0 2px;font-size:11px;text-transform:uppercase}p{margin:3px 0}.totals div,.payments div{display:flex;justify-content:space-between;gap:8px;padding:2px 0}.difference{margin-top:5px;border-top:1px dashed #555;padding-top:5px!important;font-size:13px}.note{margin-top:10px;padding:7px;border:1px dashed #555}.id{overflow-wrap:anywhere;border-top:1px dashed #555;margin-top:10px;padding-top:7px;font-size:9px}@media screen{body{margin:16px auto;padding:4mm;box-shadow:0 8px 30px #0002}}@media print{body{padding:0;box-shadow:none}}</style></head><body><header>${logo}<h1>${escapeHtml(companyName)}</h1><p><strong>ARQUEO DE CAJA · CIERRE CONFIRMADO</strong></p><p>Sede: ${escapeHtml(value.businessName)} · ${escapeHtml(value.warehouseName)}</p><p>Usuario que trabajó: <strong>${escapeHtml(value.userName)}</strong></p><p>${escapeHtml(new Date(value.openedAt).toLocaleString("es-CO"))}<br>${escapeHtml(new Date(value.closedAt).toLocaleString("es-CO"))}</p></header><h2>Resumen del turno</h2><section class="totals">${row("Ventas", value.totalSales)}${row("Devoluciones", value.totalRefunds)}${row("Otros movimientos", value.totalOther)}${row("Neto", value.netAmount)}${row("Efectivo esperado", value.expectedCash)}${row("Efectivo contado", value.countedCash)}<div class="difference"><span>Diferencia de efectivo</span><strong>${escapeHtml(money(value.cashDifference))}</strong></div></section><h2>Todos los medios de pago</h2><section class="payments">${paymentRows || "<p>Sin movimientos</p>"}</section>${value.note ? `<p class="note"><strong>Observación:</strong> ${escapeHtml(value.note)}</p>` : ""}${closureId}</body></html>`;
}

export function printWorkSessionClosure(html: string): Promise<void> {
  return new Promise((resolve, reject) => {
    const frame = document.createElement("iframe");
    frame.setAttribute("aria-hidden", "true");
    frame.style.position = "fixed";
    frame.style.width = "1px";
    frame.style.height = "1px";
    frame.style.opacity = "0";
    frame.style.pointerEvents = "none";
    const remove = () => frame.remove();
    frame.onload = () => {
      window.setTimeout(() => {
        const printWindow = frame.contentWindow;
        if (!printWindow) {
          remove();
          reject(new Error("No fue posible abrir el diálogo de impresión del arqueo."));
          return;
        }
        printWindow.addEventListener("afterprint", remove, { once: true });
        printWindow.focus();
        printWindow.print();
        window.setTimeout(remove, 60_000);
        resolve();
      }, 150);
    };
    frame.srcdoc = html;
    document.body.appendChild(frame);
  });
}

function escapeHtml(value: string): string {
  return value.replaceAll("&", "&amp;").replaceAll("<", "&lt;").replaceAll(">", "&gt;").replaceAll('"', "&quot;").replaceAll("'", "&#39;");
}
