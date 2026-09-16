export function shouldShowServerSearchSpinner({
  draft,
  value,
  isSearching,
}: {
  draft: string;
  value: string;
  isSearching: boolean;
}) {
  const normalizedDraft = draft.trim();
  const normalizedValue = value.trim();
  return normalizedDraft !== normalizedValue ||
    (isSearching && (normalizedDraft.length > 0 || normalizedValue.length > 0));
}
