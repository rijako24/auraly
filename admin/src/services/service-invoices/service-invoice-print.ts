import type { ServiceInvoiceDetail } from "@/services/api/service-invoices";
import type { PosPrintTemplateFormat } from "@/services/pos/pos-edge-client";

const currency = new Intl.NumberFormat("es-CO", {
  style: "currency", currency: "COP", minimumFractionDigits: 0,
});

export function openServiceInvoicePrintPreview(): Window | null {
  return window.open("", "_blank", "noopener,noreferrer,width=920,height=820");
}

export function renderServiceInvoice(
  preview: Window | null,
  invoice: ServiceInvoiceDetail,
  format: PosPrintTemplateFormat,
  receiptWidth: 58 | 80 = 80,
): void {
  if (!preview) throw new Error("El navegador bloqueó la vista de impresión.");
  const isReceipt = format === "Receipt";
  const pageSize = format === "HalfLetter"
    ? "140mm 216mm"
    : format === "HalfLegal"
      ? "108mm 356mm"
      : format === "Letter" ? "216mm 279mm" : `${receiptWidth}mm auto`;
  const qrUrl = `/api/commerce/v1/service-invoices/${invoice.documentId}/qr?businessId=${invoice.businessId}`;
  const lines = isReceipt
    ? invoice.lines.map((line) => `<section class="line"><b>${escapeHtml(line.description)}</b><div><span>${line.quantity} × ${money(line.unitPrice)}</span><b>${money(line.lineTotal)}</b></div>${line.discountAmount > 0 ? `<small>Descuento ${money(line.discountAmount)}</small>` : ""}</section>`).join("")
    : `<table><thead><tr><th>Servicio</th><th>Cant.</th><th>Precio</th><th>IVA</th><th>Total</th></tr></thead><tbody>${invoice.lines.map((line) => `<tr><td><b>${escapeHtml(line.description)}</b><small>${escapeHtml(line.serviceCode)}</small></td><td class="number">${line.quantity}</td><td class="number">${money(line.unitPrice)}</td><td class="number">${line.taxRate}%</td><td class="number">${money(line.lineTotal)}</td></tr>`).join("")}</tbody></table>`;
  const payments = invoice.payments.length
    ? invoice.payments.map((payment) => `<div><span>${escapeHtml(paymentName(payment.methodCode))}</span><b>${money(payment.amount)}</b></div>`).join("")
    : `<div><span>Crédito</span><b>${money(invoice.creditAmount)}</b></div>`;
  const html = `<!doctype html><html lang="es"><head><meta charset="utf-8"><title>${escapeHtml(invoice.documentNumber)}</title><style>
    @page{size:${pageSize};margin:${isReceipt ? "4mm" : "10mm"}}*{box-sizing:border-box}body{margin:0 auto;color:#0f172a;font:${isReceipt ? "11px/1.35 ui-monospace,Consolas,monospace" : "12px/1.45 Arial,sans-serif"};max-width:${isReceipt ? `${receiptWidth - 8}mm` : "196mm"}}header{text-align:${isReceipt ? "center" : "left"};border-bottom:1px solid #0f766e;padding-bottom:10px;margin-bottom:10px}h1{margin:2px 0;font-size:${isReceipt ? "18px" : "24px"}}h2{margin:3px 0;font-size:${isReceipt ? "11px" : "15px"};text-transform:uppercase;color:#0f766e}.meta{display:grid;grid-template-columns:repeat(${isReceipt ? 1 : 2},1fr);gap:6px;padding:8px 0;border-bottom:1px dashed #94a3b8}.meta div,.totals div,.payments div,.line div{display:flex;justify-content:space-between;gap:12px}.line{padding:8px 0;border-bottom:1px dashed #cbd5e1}.line small,td small{display:block;color:#64748b}.totals,.payments{padding-top:9px}.grand{margin-top:6px;padding-top:6px;border-top:2px solid #0f172a;font-size:16px}.cufe{overflow-wrap:anywhere;font-size:8px}.qr{display:block;width:${isReceipt ? "38mm" : "34mm"};height:${isReceipt ? "38mm" : "34mm"};margin:10px auto}table{width:100%;border-collapse:collapse;margin:12px 0}th,td{padding:7px;border-bottom:1px solid #cbd5e1;text-align:left}th{background:#f0fdfa;color:#115e59}.number{text-align:right}footer{text-align:center;border-top:1px dashed #94a3b8;margin-top:10px;padding-top:8px;font-size:9px}@media screen{body{padding:12px;box-shadow:0 8px 30px #0002}}@media print{body{padding:0;box-shadow:none}}</style></head><body><header><h1>${escapeHtml(invoice.businessName)}</h1><h2>Factura electrónica de servicios</h2><b>${escapeHtml(invoice.documentNumber)}</b><br><span>DIAN ${escapeHtml(invoice.fiscalNumber)}</span><br><span>${escapeHtml(new Date(invoice.issuedAt).toLocaleString("es-CO"))}</span></header><section class="meta"><div><span>Cliente</span><b>${escapeHtml(invoice.customerName)}</b></div><div><span>Identificación</span><b>${escapeHtml(invoice.customerIdentification)}</b></div>${invoice.customerEmail ? `<div><span>Correo</span><b>${escapeHtml(invoice.customerEmail)}</b></div>` : ""}<div><span>Estado fiscal</span><b>${escapeHtml(invoice.fiscalStatus)}</b></div></section>${lines}<section class="totals"><div><span>Subtotal</span><b>${money(invoice.untaxedAmount)}</b></div><div><span>IVA</span><b>${money(invoice.taxAmount)}</b></div>${invoice.creditAmount > 0 ? `<div><span>Crédito</span><b>${money(invoice.creditAmount)}</b></div>` : ""}<div class="grand"><span>Total</span><b>${money(invoice.payableAmount)}</b></div></section><section class="payments"><b>Medios de pago</b>${payments}</section><p class="cufe"><b>CUFE</b><br>${escapeHtml(invoice.cufe)}</p><img class="qr" src="${qrUrl}" alt="QR DIAN"><footer>Representación gráfica de factura electrónica de servicios<br><b>www.auralyapp.co</b></footer><script>addEventListener('load',()=>setTimeout(()=>window.print(),180));</script></body></html>`;
  preview.document.open();
  preview.document.write(html);
  preview.document.close();
}

function money(value: number): string { return currency.format(value); }

function paymentName(code: string): string {
  return ({ Cash: "Efectivo", Transfer: "Transferencia", DebitCard: "Tarjeta débito", CreditCard: "Tarjeta crédito" } as Record<string, string>)[code] ?? code;
}

function escapeHtml(value: string): string {
  return value.replace(/[&<>'"]/g, (character) => ({
    "&": "&amp;", "<": "&lt;", ">": "&gt;", "'": "&#39;", '"': "&quot;",
  })[character] ?? character);
}
