"use client";

import { useEffect } from "react";

let locks = 0;
let previousOverflow = "";
let previousPaddingRight = "";

export function useBodyScrollLock(active: boolean) {
  useEffect(() => {
    if (!active) return;
    if (locks++ === 0) {
      const body = document.body;
      previousOverflow = body.style.overflow;
      previousPaddingRight = body.style.paddingRight;
      const scrollbarWidth = window.innerWidth - document.documentElement.clientWidth;
      const paddingRight = Number.parseFloat(window.getComputedStyle(body).paddingRight) || 0;
      body.style.overflow = "hidden";
      if (scrollbarWidth > 0) body.style.paddingRight = `${paddingRight + scrollbarWidth}px`;
    }
    return () => {
      if (--locks === 0) {
        document.body.style.overflow = previousOverflow;
        document.body.style.paddingRight = previousPaddingRight;
      }
    };
  }, [active]);
}
