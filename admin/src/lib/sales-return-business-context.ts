export function resolveSalesReturnBusinessId(
  businessIdOverride?: string | null,
  selectedBusinessId?: string | null,
) {
  return businessIdOverride || selectedBusinessId || null;
}
