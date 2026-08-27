import { Activity, Boxes, Cloud, ShieldCheck } from "lucide-react";

import { AuralyLogo } from "@/components/brand/auraly-logo";

const capabilities = [
  { icon: Boxes, label: "Operación unificada" },
  { icon: Activity, label: "Datos en tiempo real" },
  { icon: ShieldCheck, label: "Acceso empresarial" },
];

export default function AuthLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  return (
    <main className="relative grid h-dvh min-h-0 overflow-hidden bg-[#06171b] lg:grid-cols-[minmax(0,1.08fr)_minmax(500px,0.92fr)]">
      <div
        aria-hidden="true"
        className="pointer-events-none absolute inset-0 opacity-40"
        style={{
          backgroundImage:
            "linear-gradient(rgba(166,241,234,.045) 1px, transparent 1px), linear-gradient(90deg, rgba(166,241,234,.045) 1px, transparent 1px)",
          backgroundSize: "56px 56px",
          maskImage: "linear-gradient(to right, black, transparent 72%)",
        }}
      />
      <div
        aria-hidden="true"
        className="pointer-events-none absolute -left-40 -top-48 h-[36rem] w-[36rem] rounded-full bg-[#2a7a82]/20 blur-[110px]"
      />
      <div
        aria-hidden="true"
        className="pointer-events-none absolute bottom-[-18rem] left-[28%] h-[34rem] w-[34rem] rounded-full bg-[#69d9d0]/10 blur-[120px]"
      />

      <section className="relative hidden min-h-0 flex-col justify-between overflow-hidden px-10 py-10 lg:flex xl:px-16 xl:py-12">
        <AuralyLogo
          className="[&>span]:text-[#f1fbfa]"
          markClassName="h-9 w-11"
        />

        <div className="max-w-2xl pb-8">
          <div className="mb-8 inline-flex items-center gap-2 rounded-full border border-[#69d9d0]/20 bg-[#69d9d0]/[0.07] px-3.5 py-2 text-xs font-medium tracking-wide text-[#a6f1ea] backdrop-blur-sm">
            <Cloud className="h-3.5 w-3.5" />
            Plataforma empresarial conectada
          </div>
          <h1 className="max-w-xl text-4xl font-semibold leading-[1.08] tracking-[-0.04em] text-[#f1fbfa] xl:text-6xl">
            Toda tu operación,
            <span className="block bg-gradient-to-r from-[#a6f1ea] via-[#69d9d0] to-[#4ec7a6] bg-clip-text text-transparent">
              en una sola señal.
            </span>
          </h1>
          <p className="mt-6 max-w-xl text-base leading-7 text-[#a7c7c5] xl:text-lg">
            Ventas, inventario, finanzas y clientes sincronizados para que tu
            equipo decida con información precisa.
          </p>

          <div className="mt-10 grid max-w-2xl grid-cols-3 gap-3">
            {capabilities.map(({ icon: Icon, label }) => (
              <div
                key={label}
                className="rounded-2xl border border-white/[0.08] bg-white/[0.035] p-4 backdrop-blur-sm"
              >
                <span className="mb-4 flex h-9 w-9 items-center justify-center rounded-xl bg-[#69d9d0]/10 text-[#69d9d0]">
                  <Icon className="h-4 w-4" />
                </span>
                <p className="text-sm font-medium leading-5 text-[#d9efed]">
                  {label}
                </p>
              </div>
            ))}
          </div>
        </div>

        <div className="flex items-center justify-between border-t border-white/[0.08] pt-6 text-xs text-[#789b99]">
          <span>Auraly</span>
          <span className="flex items-center gap-2">
            <span className="h-1.5 w-1.5 rounded-full bg-[#4ec7a6] shadow-[0_0_12px_rgba(78,199,166,.8)]" />
            Sistemas operativos
          </span>
        </div>
      </section>

      <section className="relative flex min-h-0 items-center justify-center overflow-y-auto overscroll-contain px-4 pb-[max(1.5rem,env(safe-area-inset-bottom))] pt-[max(1.5rem,env(safe-area-inset-top))] sm:px-8 lg:bg-[#f4f9f8] lg:px-12 lg:py-10 xl:px-20">
        <div className="absolute inset-x-0 top-0 h-px bg-gradient-to-r from-transparent via-[#69d9d0]/50 to-transparent lg:hidden" />
        <div className="w-full max-w-[470px]">
          <AuralyLogo
            className="mb-7 justify-center lg:hidden [&>span]:text-[#f1fbfa]"
            markClassName="h-9 w-11"
          />
          {children}
        </div>
      </section>
    </main>
  );
}
