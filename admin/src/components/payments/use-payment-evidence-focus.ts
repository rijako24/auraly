"use client";

import { useEffect, useRef } from "react";

export function focusPaymentEvidence(container: ParentNode | null) {
  const input = container?.querySelector<HTMLInputElement>("input[data-payment-evidence]:not(:disabled)");
  if (!input) return false;
  input.focus({ preventScroll: true });
  return input === input.ownerDocument.activeElement;
}

// Focus once when evidence becomes editable, after the selector restores its focus.
// Editing the value or changing franchise must not move the cursor again.
export function usePaymentEvidenceFocus(enabled: boolean, formKey?: string) {
  const inputRef = useRef<HTMLInputElement>(null);
  useEffect(() => {
    if (!enabled) return;
    const frame = requestAnimationFrame(() => inputRef.current?.focus({ preventScroll: true }));
    return () => cancelAnimationFrame(frame);
  }, [enabled, formKey]);
  return inputRef;
}
