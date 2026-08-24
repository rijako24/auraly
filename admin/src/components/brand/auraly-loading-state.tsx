import Image from "next/image";

export function AuralyLoadingState({
  title = "Cargando Auraly",
  description,
  overlay = false,
}: {
  title?: string;
  description?: string;
  overlay?: boolean;
}) {
  return (
    <div
      role="status"
      aria-live="polite"
      className={`${overlay ? "fixed inset-0 z-[2147483647] min-h-dvh" : "min-h-[65dvh]"} grid place-items-center bg-[#f8fafc] px-6 text-[#07161a]`}
    >
      <div className="flex w-full max-w-sm flex-col items-center text-center">
        <span className="grid h-24 w-24 place-items-center overflow-hidden rounded-[2rem] bg-gradient-to-br from-white via-teal-50 to-teal-100 shadow-2xl shadow-teal-950/10 ring-1 ring-teal-900/10">
          <Image
            src="/brand/auraly-app-icon-192-v4.png"
            width={96}
            height={96}
            alt="Auraly"
            priority
            className="h-full w-full object-cover"
          />
        </span>
        <h1 className="mt-5 text-lg font-black tracking-tight">{title}</h1>
        {description && (
          <p className="mt-2 text-sm leading-6 text-[#667f7d]">{description}</p>
        )}
        <span className="mt-6 h-1.5 w-32 overflow-hidden rounded-full bg-teal-100">
          <span className="block h-full w-1/2 animate-[app-loading_1.1s_ease-in-out_infinite] rounded-full bg-gradient-to-r from-teal-600 to-emerald-400" />
        </span>
        <span className="sr-only">Cargando</span>
      </div>
    </div>
  );
}
