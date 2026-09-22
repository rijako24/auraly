export function completedOrderRecoveryPresentation(lineIds: readonly string[]) {
  return {
    error: null,
    selectedLineId: lineIds[0] ?? null,
    message: `Pedido recuperado · ${lineIds.length} líneas`,
  } as const;
}
