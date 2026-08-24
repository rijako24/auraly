"use client";

import { toast as sonnerToast, type ExternalToast } from "sonner";

/**
 * Wrapper around sonner toast for consistent API with shadcn/ui patterns.
 * Use the `toast` export from sonner or this module for triggering toasts.
 */
export const toast = sonnerToast;

export type { ExternalToast };
