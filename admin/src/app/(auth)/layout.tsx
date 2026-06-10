"use client";

import { AuralyLogo } from "@/components/brand/auraly-logo";
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
          "bg-[radial-gradient(circle_at_50%_15%,rgba(105,217,208,0.18),transparent_30%),linear-gradient(135deg,#07161A_0%,#0F2C33_56%,#1A5860_100%)]",
          "relative overflow-hidden"
        )}
      >
        <div className="absolute inset-x-12 bottom-12 h-px auraly-logo-gradient opacity-80" />
        <div className="relative z-10 px-12 text-center">
          <AuralyLogo
            className="mb-7 justify-center"
            markClassName="h-20 w-24"
          />
        </div>
      </div>

      {/* Right side - form */}
      <div
        className={cn(
          "flex-1 flex flex-col justify-center items-center",
          "p-6 sm:p-8 lg:p-12",
          "bg-white text-[#151515]"
        )}
      >
        <div className="w-full max-w-md">{children}</div>
      </div>
    </div>
  );
}
