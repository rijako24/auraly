"use client";

import { useEffect, type RefObject } from "react";

export function useActiveProductOptionScroll(
  listRef: RefObject<HTMLElement | null>,
  activeIndex: number | null,
  open: boolean,
  itemCount: number,
) {
  useEffect(() => {
    if (!open || activeIndex === null || activeIndex < 0 || activeIndex >= itemCount) return;
    const frame = requestAnimationFrame(() => {
      listRef.current
        ?.querySelector<HTMLElement>(`[data-product-option-index="${activeIndex}"]`)
        ?.scrollIntoView({ block: "nearest" });
    });
    return () => cancelAnimationFrame(frame);
  }, [activeIndex, itemCount, listRef, open]);
}
