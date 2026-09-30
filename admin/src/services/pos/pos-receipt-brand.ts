export type PosReceiptBranding = {
  displayName: string;
  legalName: string | null;
  logoUrl: string | null;
};

export function hasPrintIdentity(
  branding: Pick<PosReceiptBranding, "displayName" | "legalName"> | null,
): boolean {
  return Boolean(branding?.displayName?.trim() || branding?.legalName?.trim());
}

export function resolveReceiptCompanyName(
  branding: Pick<PosReceiptBranding, "displayName" | "legalName"> | null,
  receiptName?: string | null,
  tenantName?: string | null,
  businessName?: string | null,
): string {
  return [branding?.displayName, branding?.legalName, receiptName, tenantName, businessName]
    .find(value => value?.trim())?.trim() ?? "Empresa";
}

export function receiptBrandMarkup(branding: PosReceiptBranding | null) {
  const name = resolveReceiptCompanyName(branding);
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
