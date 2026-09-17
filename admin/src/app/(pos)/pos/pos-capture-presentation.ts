type IdentifiedLine = { lineId: string };
type PricedLine = { allowsDocumentCostOverride: boolean };

export function capturedLineAfterAddition<T extends IdentifiedLine>(
  previousLines: readonly IdentifiedLine[],
  currentLines: readonly T[],
): T | undefined {
  const previousIds = new Set(previousLines.map((line) => line.lineId));
  for (let index = currentLines.length - 1; index >= 0; index -= 1) {
    const line = currentLines[index];
    if (!previousIds.has(line.lineId)) return line;
  }

  return currentLines.at(-1);
}

export function shouldOpenGenericProductPricing(
  line: PricedLine | undefined,
): boolean {
  return line?.allowsDocumentCostOverride === true;
}
