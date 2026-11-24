"use client";

import { useEffect, useRef } from "react";

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
