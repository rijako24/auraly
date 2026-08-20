import { AuralyLogo } from "@/components/brand/auraly-logo";

export default function AppLoading() {
  return (
    <div className="grid min-h-dvh place-items-center bg-[#f8fafc] px-6 text-[#07161a]">
      <div className="flex flex-col items-center">
        <span className="grid h-24 w-24 place-items-center rounded-[2rem] bg-gradient-to-br from-white via-teal-50 to-teal-100 shadow-2xl shadow-teal-950/10 ring-1 ring-teal-900/10">
          <AuralyLogo collapsed markClassName="h-12 w-16" />
        </span>
        <p className="mt-5 text-sm font-semibold uppercase tracking-[0.32em]">Auraly</p>
        <span className="mt-7 h-1.5 w-24 overflow-hidden rounded-full bg-teal-100">
          <span className="block h-full w-1/2 animate-[app-loading_1.1s_ease-in-out_infinite] rounded-full bg-teal-600" />
        </span>
        <span className="sr-only">Cargando Auraly</span>
      </div>
    </div>
  );
}
