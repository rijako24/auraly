"use client";

import { Toaster as Sonner } from "sonner";

import { cn } from "@/lib/utils";

type ToasterProps = React.ComponentProps<typeof Sonner>;

const Toaster = ({ className, toastOptions, ...props }: ToasterProps) => {
  return (
    <Sonner
      className={cn("toaster group", className)}
      toastOptions={{
        ...toastOptions,
        classNames: {
          toast:
            "pointer-events-none group toast group-[.toaster]:bg-background group-[.toaster]:text-foreground group-[.toaster]:border-border group-[.toaster]:shadow-lg [&_[data-button]]:pointer-events-auto [&_[data-close-button]]:pointer-events-auto",
          description: "group-[.toast]:text-muted-foreground",
          actionButton:
            "group-[.toast]:bg-primary group-[.toast]:text-primary-foreground",
          cancelButton:
            "group-[.toast]:bg-muted group-[.toast]:text-muted-foreground",
          success:
            "group-[.toast]:border-green-500/50 group-[.toast]:text-green-600 dark:group-[.toast]:text-green-400",
          error:
            "group-[.toast]:border-destructive/50 group-[.toast]:text-destructive",
          ...toastOptions?.classNames,
        },
      }}
      {...props}
    />
  );
};

Toaster.displayName = "Toaster";

export { Toaster };
