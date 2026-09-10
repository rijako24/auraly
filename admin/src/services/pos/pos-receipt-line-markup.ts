type PrintableReceiptLine = {
  description: string;
  quantity: number;
  unitPrice: number;
  discount: number;
  total: number;
};

export function receiptLineMarkup(
  line: PrintableReceiptLine,
  currency: Pick<Intl.NumberFormat, "format">,
) {
  const discount = line.discount > 0
    ? `<div class="discount"><span>Descuento</span><b>-${currency.format(line.discount)}</b></div>`
    : "";
  return `<div class="line"><b>${escapeReceiptHtml(line.description)}</b><div><span>${line.quantity} × ${currency.format(line.unitPrice)}</span><b>${currency.format(line.total)}</b></div>${discount}</div>`;
}

function escapeReceiptHtml(value: string) {
  return value
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#39;");
}
