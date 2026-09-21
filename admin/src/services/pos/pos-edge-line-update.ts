export type EdgeLineUpdate = {
  lineId: string;
  description: string;
  publicUnitPrice: number;
  discount: number;
  documentUnitCost: number;
};

export type CompatibleEdgeLineUpdate = EdgeLineUpdate & {
  unitPrice: number;
};

export function buildCompatibleEdgeLineUpdates(
  lines: EdgeLineUpdate[],
): CompatibleEdgeLineUpdate[] {
  return lines.map((line) => ({
    ...line,
    // rc129 and earlier called this field unitPrice. Keep it on the wire while
    // installed clients move to the canonical publicUnitPrice contract.
    unitPrice: line.publicUnitPrice,
  }));
}
