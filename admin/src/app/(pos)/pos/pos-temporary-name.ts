export function temporaryNameForCustomer(
  customer: { name: string } | null | undefined,
): string | null {
  const name = customer?.name.trim();
  return name ? name : null;
}
