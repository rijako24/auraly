type PrintableReceiptLine = {
  productCode?: string;
  unitCode?: string;
  description: string;
  quantity: number;
  unitPrice: number;
  discount: number;
  total: number;
};

export function receiptLineMarkup(
  line: PrintableReceiptLine,
  currency: Pick<Intl.NumberFormat, "format">,
  lineNumber?: number,
) {
  const discount = line.discount > 0
    ? `<div class="discount"><span>Descuento</span><b>-${currency.format(line.discount)}</b></div>`
    : "";
  const identity = lineNumber == null
    ? ""
    : `${lineNumber}. `;
  const item = lineNumber != null && (line.productCode || line.unitCode)
    ? `<small>${escapeReceiptHtml(line.productCode ?? "")} · ${escapeReceiptHtml(line.unitCode ?? "EA")}</small>`
    : "";
  return `<div class="line"><b>${identity}${escapeReceiptHtml(line.description)}</b>${item}<div><span>${line.quantity} × ${currency.format(line.unitPrice)}</span><b>${currency.format(line.total)}</b></div>${discount}</div>`;
}

function escapeReceiptHtml(value: string) {
  return value
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#39;");
}
