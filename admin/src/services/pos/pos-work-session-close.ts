export function workSessionClosurePreviewPath(workSessionId: string): string {
  return `/api/commerce/v1/work-sessions/${encodeURIComponent(workSessionId)}/closure-preview`;
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
  logoUrl?: string | null;
  businessName: string; warehouseName: string; userName: string; openedAt: string; closedAt: string;
  totalSales: number; totalRefunds: number; totalOther: number; netAmount: number;
  expectedCash: number; countedCash: number | null; cashDifference: number | null; note: string | null;
  paymentTotals: Array<{ paymentMethodCode: string; netAmount: number; countedAmount?: number | null; difference?: number | null }>;
};

export function workSessionClosureHtml(value: ClosureForPrint): string {
  const money = (amount: number | null) => amount == null ? "—" : new Intl.NumberFormat("es-CO", { style: "currency", currency: "COP", maximumFractionDigits: 0 }).format(amount);
  const row = (label: string, amount: number | null) => `<div><span>${escapeHtml(label)}</span><strong>${escapeHtml(money(amount))}</strong></div>`;
  const paymentRows = value.paymentTotals.map(item =>
    row(`${workSessionPaymentMethodName(item.paymentMethodCode)} esperado`, item.netAmount) +
    (workSessionPaymentMethodRequiresCount(item.paymentMethodCode)
      ? row(`${workSessionPaymentMethodName(item.paymentMethodCode)} contado`, item.countedAmount ?? null) +
        row(`${workSessionPaymentMethodName(item.paymentMethodCode)} diferencia`, item.difference ?? null)
      : `<div><span>${escapeHtml(workSessionPaymentMethodName(item.paymentMethodCode))} conciliación</span><strong>Automática</strong></div>`)).join("");
  const logo = value.logoUrl ? `<img src="${escapeHtml(value.logoUrl)}" alt="Logo" />` : "";
  return `<!doctype html><html lang="es"><head><meta charset="utf-8"><title>Cierre de sesión</title><style>@page{margin:10mm}body{font-family:Arial,sans-serif;color:#0f172a;max-width:720px;margin:auto}header{border-bottom:2px solid #0f766e;padding-bottom:12px}header img{display:block;max-height:72px;max-width:220px;object-fit:contain;margin-bottom:10px}h1{margin:0;color:#0f766e}p{margin:5px 0}.totals div,.payments div{display:flex;justify-content:space-between;gap:20px;padding:8px 0;border-bottom:1px solid #e2e8f0}.difference{font-size:20px}.note{margin-top:16px;padding:12px;background:#f8fafc}@media print{body{max-width:none}}</style></head><body><header>${logo}<h1>${escapeHtml(value.businessName)}</h1><p>Cierre de sesión de venta</p><p>${escapeHtml(value.warehouseName)} · ${escapeHtml(value.userName)}</p><p>${escapeHtml(new Date(value.openedAt).toLocaleString("es-CO"))} — ${escapeHtml(new Date(value.closedAt).toLocaleString("es-CO"))}</p></header><section class="totals">${row("Ventas", value.totalSales)}${row("Devoluciones", value.totalRefunds)}${row("Otros movimientos", value.totalOther)}${row("Neto", value.netAmount)}${row("Efectivo esperado", value.expectedCash)}${row("Efectivo contado", value.countedCash)}<div class="difference"><span>Diferencia de efectivo</span><strong>${escapeHtml(money(value.cashDifference))}</strong></div></section><h2>Consolidado por medio de pago</h2><section class="payments">${paymentRows || "<p>Sin movimientos</p>"}</section>${value.note ? `<p class="note"><strong>Observación:</strong> ${escapeHtml(value.note)}</p>` : ""}</body></html>`;
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
