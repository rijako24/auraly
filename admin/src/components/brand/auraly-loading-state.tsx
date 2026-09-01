import { AuralyMark } from "./auraly-mark";

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
        <AuralyMark className="h-20 w-20" />
        <h1 className="mt-5 text-lg font-semibold tracking-tight">{title}</h1>
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
