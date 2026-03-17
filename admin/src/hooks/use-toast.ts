"use client";

import { toast as sonnerToast } from "sonner";

/**
 * Hook for toast notifications using Sonner.
 * Provides a consistent API compatible with shadcn/ui useToast pattern.
 */
export function useToast() {
  return {
    toast: ({
      title,
      description,
      variant = "default",
      ...options
    }: {
      title?: string;
      description?: string;
      variant?: "default" | "destructive" | "success";
    } & Parameters<typeof sonnerToast>[1]) => {
      const message = description ? `${title}\n${description}` : title ?? "";
      switch (variant) {
        case "destructive":
          return sonnerToast.error(message, options);
        case "success":
          return sonnerToast.success(message, options);
        default:
          return sonnerToast(message, options);
      }
    },
    dismiss: sonnerToast.dismiss,
    promise: sonnerToast.promise,
    success: sonnerToast.success,
    error: sonnerToast.error,
    info: sonnerToast.info,
    warning: sonnerToast.warning,
    loading: sonnerToast.loading,
  };
}
