export function localPrintLogoSource(
  branding: { logoUrl: string | null } | null,
  prepared: boolean,
): string | null {
  if (prepared) return null;
  return branding?.logoUrl?.startsWith("data:image/") ? branding.logoUrl : "";
}
