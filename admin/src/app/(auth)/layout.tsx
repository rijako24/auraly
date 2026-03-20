"use client";

import { cn } from "@/lib/utils";

export default function AuthLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  return (
    <div className="min-h-screen flex">
      {/* Left side - gradient/illustration - hidden on mobile */}
      <div
        className={cn(
          "hidden lg:flex lg:w-1/2 flex-col justify-center items-center",
          "bg-gradient-to-br from-primary via-primary/90 to-primary/80",
          "relative overflow-hidden"
        )}
      >
        <div className="absolute inset-0 bg-[radial-gradient(ellipse_at_top_right,_var(--tw-gradient-stops))] from-white/10 to-transparent" />
        <div className="relative z-10 px-12 text-center">
          <h2 className="text-3xl font-bold text-primary-foreground mb-4">
            Quantix AI Admin
          </h2>
          <p className="text-primary-foreground/90 text-lg max-w-sm">
            Gestiona tu negocio con inteligencia artificial. Reservas,
            pagos y conversaciones automatizadas en un solo lugar.
          </p>
          <div className="mt-12 w-64 h-40 mx-auto rounded-xl bg-white/10 backdrop-blur-sm border border-white/20" />
        </div>
      </div>

      {/* Right side - form */}
      <div
        className={cn(
          "flex-1 flex flex-col justify-center items-center",
          "p-6 sm:p-8 lg:p-12",
          "bg-background"
        )}
      >
        <div className="w-full max-w-md">{children}</div>
      </div>
    </div>
  );
}
