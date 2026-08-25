export function pushApplicationServerKeyMatches(
  current: ArrayBuffer | null,
  expected: Uint8Array,
): boolean {
  if (!current || current.byteLength !== expected.byteLength) return false;
  const actual = new Uint8Array(current);
  return actual.every((value, index) => value === expected[index]);
}
