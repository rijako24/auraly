"use client";

import { type RefObject, useEffect, useRef } from "react";

type ModalStackEntry = { token: symbol; restoreFocus: () => void };
const modalStack: ModalStackEntry[] = [];

type PosModalBehaviorOptions = {
  modalRef: RefObject<HTMLElement | null>;
  initialFocusRef?: RefObject<HTMLElement | null>;
  onEscape: () => void;
  escapeDisabled?: boolean;
  focusRequest?: number;
};

/**
 * Owns the keyboard boundary for custom POS modals. The stack guarantees that
 * Escape only closes the top-most surface, while the delayed focus check wins
 * races with scanner focus restoration after React mounts a new popup.
 */
export function usePosModalBehavior({
  modalRef,
  initialFocusRef,
  onEscape,
  escapeDisabled = false,
  focusRequest = 0,
}: PosModalBehaviorOptions) {
  const token = useRef(Symbol("pos-modal"));
  const escapeRef = useRef(onEscape);
  const disabledRef = useRef(escapeDisabled);
  const focusInsideRef = useRef<() => void>(() => undefined);
  escapeRef.current = onEscape;
  disabledRef.current = escapeDisabled;

  useEffect(() => {
    const currentToken = token.current;
    const entry: ModalStackEntry = {
      token: currentToken,
      restoreFocus: () => focusInsideRef.current(),
    };
    modalStack.push(entry);
    const handleEscape = (event: KeyboardEvent) => {
      if (event.key !== "Escape" || modalStack.at(-1)?.token !== currentToken) return;
      event.preventDefault();
      event.stopImmediatePropagation();
      if (!disabledRef.current) escapeRef.current();
    };
    window.addEventListener("keydown", handleEscape, true);
    return () => {
      window.removeEventListener("keydown", handleEscape, true);
      let index = modalStack.length - 1;
      while (index >= 0 && modalStack[index].token !== currentToken) index--;
      if (index >= 0) modalStack.splice(index, 1);
      window.requestAnimationFrame(() => modalStack.at(-1)?.restoreFocus());
    };
  }, []);

  useEffect(() => {
    const focusInside = () => {
      const modal = modalRef.current;
      if (!modal || modal.contains(document.activeElement)) return;
      const target = initialFocusRef?.current ?? modal.querySelector<HTMLElement>(
        "[autofocus], input:not(:disabled), button:not(:disabled), [tabindex]:not([tabindex='-1'])",
      );
      (target ?? modal).focus({ preventScroll: true });
      if (target instanceof HTMLInputElement) target.select();
    };
    focusInsideRef.current = focusInside;
    const frame = window.requestAnimationFrame(focusInside);
    const timer = window.setTimeout(focusInside, 40);
    return () => {
      window.cancelAnimationFrame(frame);
      window.clearTimeout(timer);
    };
  }, [focusRequest, initialFocusRef, modalRef]);
}
