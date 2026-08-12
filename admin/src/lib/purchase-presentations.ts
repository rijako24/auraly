export const purchasePresentationOptions = [
  "Unidad",
  "Caja",
  "Bulto",
  "Paquete",
  "Bolsa",
  "Rollo",
  "Canasta",
] as const;

export function normalizedPurchasePresentation(value?: string | null) {
  const normalized = value?.trim();
  if (!normalized) return "Unidad";
  return purchasePresentationOptions.find(
    (option) => option.toLocaleLowerCase("es-CO") === normalized.toLocaleLowerCase("es-CO"),
  ) ?? "Unidad";
}
