import type { PosCashMovementTicket } from "./pos-edge-client";

export function cashMovementTicketHtml(ticket: PosCashMovementTicket, companyName: string) {
  const title = ticket.direction === "In" ? "ENTRADA DE DINERO" : "SALIDA DE DINERO";
  const optional = [
    ticket.reference ? row("Referencia", ticket.reference) : "",
    ticket.notes ? row("Observación", ticket.notes) : "",
  ].join("");
  return `<!doctype html><html lang="es"><head><meta charset="utf-8"><title>${title}</title><style>
@page{size:80mm auto;margin:4mm}*{box-sizing:border-box}body{width:72mm;margin:0 auto;color:#111;font:12px/1.4 ui-monospace,Consolas,monospace}header{text-align:center;border-bottom:1px dashed #555;padding-bottom:8px}h1{margin:0;font:800 19px/1.2 Arial,sans-serif}h2{margin:5px 0 0;font-size:13px}.amount{margin:12px 0;border:2px solid #111;padding:9px;text-align:center;font-size:18px;font-weight:800}.details{border-bottom:1px dashed #555;padding-bottom:8px}.row{display:flex;justify-content:space-between;gap:10px;margin:4px 0}.row span:last-child{text-align:right;overflow-wrap:anywhere}.signature{margin-top:34px;border-top:1px solid #111;padding-top:4px;text-align:center}.id{margin-top:10px;text-align:center;font-size:9px;color:#555}</style></head><body><header><h1>${escapeHtml(companyName || "Empresa")}</h1><h2>${title}</h2></header><div class="amount">${money(ticket.amount)}</div><section class="details">${row("Motivo", ticket.reasonName)}${optional}${row("Responsable", ticket.responsibleName)}${row("Fecha y hora", new Date(ticket.occurredAt).toLocaleString("es-CO", { dateStyle: "short", timeStyle: "short" }))}</section><div class="signature">Firma</div><div class="id">${escapeHtml(ticket.documentId)}</div><script>addEventListener('load',()=>setTimeout(()=>window.print(),150));</script></body></html>`;
}

export function printCashMovementTicket(html: string) {
  const preview = window.open("", "_blank", "noopener,noreferrer");
  if (!preview) return Promise.reject(new Error("El navegador bloqueó la ventana de impresión."));
  preview.document.open();
  preview.document.write(html);
  preview.document.close();
  return Promise.resolve();
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
