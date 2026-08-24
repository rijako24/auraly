export type PosReceiptBranding = {
  displayName: string;
  legalName: string | null;
  logoUrl: string | null;
};

export function receiptBrandMarkup(branding: PosReceiptBranding | null) {
  const name = branding?.displayName || branding?.legalName || "Empresa";
  const label = escapeBrandHtml(name);
  return branding?.logoUrl
    ? `<img class="brand-logo" src="${escapeBrandHtml(branding.logoUrl)}" alt="Logo de ${label}"><p class="brand-name">${label}</p>`
    : `<p class="brand-name">${label}</p>`;
}

function escapeBrandHtml(value: string) {
  return value
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#39;");
}
