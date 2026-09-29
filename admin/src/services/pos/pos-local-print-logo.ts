export function localPrintLogoSource(
  branding: { logoUrl: string | null } | null,
  prepared: boolean,
): string | null {
  if (prepared) return null;
  return branding?.logoUrl?.startsWith("data:image/") ? branding.logoUrl : "";
}

export function localPrintCompanyName(
  branding: { displayName: string; legalName: string | null } | null,
  receiptCompanyName: string | null | undefined,
  businessName: string,
  prepared: boolean,
): string | null {
  if (prepared) return receiptCompanyName ?? null;
  return branding?.displayName?.trim() || branding?.legalName?.trim()
    || receiptCompanyName?.trim() || businessName.trim();
}
