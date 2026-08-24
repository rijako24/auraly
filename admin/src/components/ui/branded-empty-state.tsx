import Image from "next/image";
import type { ReactNode } from "react";

export function BrandedEmptyState({
  title,
  description,
  action,
}: {
  title: string;
  description: string;
  action?: ReactNode;
}) {
  return (
    <section className="grid min-h-[65dvh] place-items-center px-6 py-12 text-center">
      <div className="w-full max-w-xl rounded-[2rem] border bg-gradient-to-br from-white via-teal-50/40 to-cyan-50/70 p-10 shadow-xl shadow-teal-950/5">
        <span className="mx-auto grid h-24 w-24 place-items-center overflow-hidden rounded-[2rem] bg-white shadow-lg ring-1 ring-teal-900/10">
          <Image src="/brand/auraly-app-icon-192-v4.png" width={96} height={96} alt="Auraly" className="h-full w-full object-cover" />
        </span>
        <p className="mt-6 text-xs font-bold uppercase tracking-[.2em] text-teal-700">Auraly</p>
        <h1 className="mt-2 text-2xl font-black tracking-tight text-slate-950">{title}</h1>
        <p className="mx-auto mt-3 max-w-md text-sm leading-6 text-slate-600">{description}</p>
        {action && <div className="mt-6 flex justify-center">{action}</div>}
      </div>
    </section>
  );
}
