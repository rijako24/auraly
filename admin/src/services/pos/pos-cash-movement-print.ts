import type { PosCashMovementTicket } from "./pos-edge-client";
import { printPosHtmlDocument } from "./pos-browser-print";
import { posReceiptTypographyCss } from "./pos-receipt-style";

export function cashMovementTicketHtml(
  ticket: PosCashMovementTicket,
  companyName: string,
  businessName?: string | null,
  warehouseName?: string | null,
) {
  const title = ticket.direction === "In" ? "Entrada de dinero" : "Salida de dinero";
  const location = [businessName ? `Sede: ${businessName}` : "", warehouseName ?? ""]
    .filter(Boolean)
    .join(" · ");
  const optional = [
    ticket.reference ? row("Referencia", ticket.reference) : "",
    ticket.notes ? row("Observación", ticket.notes) : "",
  ].join("");
  return `<!doctype html><html lang="es"><head><meta charset="utf-8"><title>${title}</title><style>
@page{size:80mm auto;margin:4mm}*{box-sizing:border-box}${posReceiptTypographyCss}body{width:72mm;margin:0 auto;color:#111;font:12px/1.4 ui-monospace,Consolas,monospace}header{border-bottom:1px dashed #555;padding-bottom:8px}h1{margin:0;font:800 19px/1.2 Arial,sans-serif;text-transform:uppercase}h2{margin:6px 0 3px;font-size:13px;text-transform:uppercase}.scope{margin:2px 0;color:#333}.details{font-size:13px;padding-top:8px}.row{display:flex;justify-content:space-between;gap:10px;margin:6px 0}.row span:first-child{font-weight:700}.row span:last-child{text-align:right;overflow-wrap:anywhere}.amount{display:flex;justify-content:space-between;margin:12px 0 0;border-block:2px solid #111;padding:9px 0;font-size:17px;font-weight:800}.signature{margin-top:72px;border-top:1px solid #111;padding-top:4px;text-align:center}</style></head><body><header><h1>${escapeHtml(companyName || "Empresa")}</h1><h2>${title}</h2>${location ? `<p class="scope">${escapeHtml(location)}</p>` : ""}</header><section class="details">${row("Motivo", ticket.reasonName)}${optional}${row("Responsable", ticket.responsibleName)}${row("Fecha y hora", new Date(ticket.occurredAt).toLocaleString("es-CO", { dateStyle: "short", timeStyle: "short" }))}</section><div class="amount"><span>Valor</span><strong>${money(ticket.amount)}</strong></div><div class="signature">Firma</div><script>addEventListener('load',()=>setTimeout(()=>window.print(),150));</script></body></html>`;
}

export function printCashMovementTicket(html: string) {
  return printPosHtmlDocument(
    html,
    "No fue posible abrir el diálogo de impresión del movimiento de caja.",
  );
}

function row(label: string, value: string) {
  return `<div class="row"><span>${escapeHtml(label)}</span><span>${escapeHtml(value)}</span></div>`;
}

function money(value: number) {
  return new Intl.NumberFormat("es-CO", { style: "currency", currency: "COP", maximumFractionDigits: 2 }).format(value);
}

function escapeHtml(value: string) {
  return value.replace(/[&<>"']/g, character => ({
    "&": "&amp;", "<": "&lt;", ">": "&gt;", "\"": "&quot;", "'": "&#39;",
  })[character]!);
}
