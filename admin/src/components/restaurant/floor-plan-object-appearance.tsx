import type { RestaurantFloorPlanObject } from "@/services/api/restaurant-master";

export function FloorPlanObjectAppearance({ object }: { object: RestaurantFloorPlanObject }) {
  if (object.kind === "Wall") return <span className="block h-full w-full rounded-[2px] bg-[#64706a] shadow-[0_3px_0_#34484355] dark:bg-slate-500" style={{ backgroundColor: object.color ?? undefined }} />;
  if (object.kind === "Table") return null;
  if (object.kind === "Chair") return <span className="relative block h-full w-full rounded-[24%] border-2 border-[#79583e] bg-[#c99d6c] shadow-[0_4px_8px_#3d392d55] dark:border-[#d2b38b] dark:bg-[#98714f]" style={{ backgroundColor: object.color ?? undefined }}>
    <span className="absolute inset-x-[12%] top-[7%] h-[19%] rounded-t-md bg-[#765238] dark:bg-[#dfc19a]"/>
    <span className="absolute inset-x-[15%] bottom-[12%] top-[30%] rounded-md border border-[#be946b] bg-[#f5e7cc] dark:border-[#b18b67] dark:bg-[#e5cda8]"/>
  </span>;
  if (object.kind === "Bench") return <span className="relative block h-full w-full rounded-lg border-2 border-[#79583e] bg-[#c99d6c] shadow-[0_4px_8px_#3d392d55] dark:border-[#d2b38b] dark:bg-[#98714f]" style={{ backgroundColor: object.color ?? undefined }}>
    <span className="absolute inset-x-[5%] top-[8%] h-[22%] rounded-md bg-[#765238] dark:bg-[#dfc19a]"/>
    <span className="absolute inset-x-[7%] bottom-[10%] top-[34%] rounded-md border border-[#be946b] bg-[#f5e7cc] dark:border-[#b18b67] dark:bg-[#e5cda8]"/>
    <span className="absolute bottom-[8%] left-[30%] top-[35%] border-l border-[#b18b67] dark:border-[#98714f]"/>
    <span className="absolute bottom-[8%] right-[30%] top-[35%] border-l border-[#b18b67] dark:border-[#98714f]"/>
  </span>;
  if (object.kind === "Window") return <span className="absolute inset-x-0 top-1/2 h-2 -translate-y-1/2 border-y-2 border-[#607b80] bg-[#b8dce0] shadow-[0_0_0_3px_#f8f5ed] dark:border-sky-300 dark:bg-sky-700 dark:shadow-[0_0_0_3px_#202d29]">
    <span className="absolute inset-x-1 top-1/2 border-t border-white/90"/>
    <span className="absolute left-1/2 top-0 h-full border-l border-[#607b80] dark:border-sky-300"/>
  </span>;
  if (object.kind === "Door" || object.kind === "Entrance") return <svg aria-hidden="true" className="h-full w-full overflow-visible text-[#61716d] dark:text-slate-300" viewBox="0 0 100 100" preserveAspectRatio="none">
    <path d="M7 9 A82 82 0 0 1 89 91" fill="none" stroke="currentColor" strokeWidth="2" strokeDasharray="3 3"/>
    <path d="M7 91 V9" fill="none" stroke="currentColor" strokeWidth="5"/>
    <circle cx="7" cy="91" r="4" fill="currentColor"/>
  </svg>;
  if (object.kind === "Column") return <span className="absolute inset-[15%] rounded-[3px] border-[3px] border-[#7b8179] bg-[#b7b9aa] shadow-[inset_0_0_0_3px_#dddcd0,0_4px_8px_#34484333] dark:border-slate-400 dark:bg-slate-600 dark:shadow-[inset_0_0_0_3px_#475569]"/>;
  if (object.kind === "Plant") return <span className="relative block h-full w-full">
    <span className="absolute inset-[13%] rounded-full border-2 border-[#7d9273] bg-[#d5dfbf] shadow-[0_3px_8px_#35503833] dark:border-emerald-600 dark:bg-emerald-900"/>
    <span className="absolute inset-[24%] rounded-full border border-[#95ab79] bg-[radial-gradient(circle_at_30%_35%,#a3c18a_0_20%,transparent_22%),radial-gradient(circle_at_70%_30%,#6b9b67_0_24%,transparent_26%),radial-gradient(circle_at_48%_75%,#7eaa76_0_28%,transparent_30%)] dark:opacity-75"/>
    <span className="absolute left-1/2 top-1/2 h-[40%] -translate-x-1/2 -translate-y-1/2 border-l border-[#537650] rotate-45"/>
  </span>;
  if (object.kind === "Counter") return <span className="relative block h-full w-full rounded-[0.4rem] border-[3px] border-[#9f784d] bg-[#d9b884] shadow-[0_4px_0_#875d3f55] dark:border-amber-700 dark:bg-amber-900" style={{ backgroundColor: object.color ?? undefined }}>
    <span className="absolute inset-1 rounded-[0.2rem] border border-[#9f784d]/70 dark:border-amber-600"/>
    <span className="absolute inset-x-2 top-1/2 -translate-y-1/2 truncate text-center text-[10px] font-bold uppercase tracking-[0.16em] text-[#62462e] dark:text-amber-100">{object.label || "Barra"}</span>
  </span>;
  if (object.kind === "Kitchen") return <span className="relative block h-full w-full rounded-md border-[3px] border-[#8e9184] bg-[#e1e0d2] dark:border-slate-500 dark:bg-slate-700">
    <span className="absolute inset-x-[9%] top-[12%] h-[29%] rounded-sm border-2 border-[#8e9184] bg-[#f0eee4] dark:border-slate-400 dark:bg-slate-600"/>
    <span className="absolute left-[18%] top-[19%] size-[12%] rounded-full border-2 border-[#777f7a] dark:border-slate-300"/><span className="absolute right-[18%] top-[19%] size-[12%] rounded-full border-2 border-[#777f7a] dark:border-slate-300"/>
    <span className="absolute inset-x-[12%] bottom-[13%] truncate border-t-2 border-[#8e9184] pt-1 text-center text-[10px] font-bold uppercase tracking-[0.12em] text-[#5e675f] dark:border-slate-400 dark:text-slate-200">{object.label || "Cocina"}</span>
  </span>;
  if (object.kind === "Bathroom") return <span className="relative block h-full w-full rounded-sm border-[3px] border-[#8da5a6] bg-[#e6eeeb] dark:border-slate-500 dark:bg-slate-700">
    <span className="absolute left-[17%] top-[14%] h-[39%] w-[38%] rounded-[45%] border-2 border-[#8da5a6] bg-white dark:bg-slate-500"/>
    <span className="absolute right-[13%] top-[15%] h-[23%] w-[22%] rounded-sm border-2 border-[#8da5a6] bg-white dark:bg-slate-500"/>
    <span className="absolute inset-x-2 bottom-[9%] truncate text-center text-[10px] font-bold uppercase tracking-[0.12em] text-[#617b7d] dark:text-slate-200">{object.label || "Baño"}</span>
  </span>;
  if (object.kind === "Zone") return <span className="flex h-full w-full items-center justify-center rounded-lg border-2 border-dashed border-[#a99b7d] bg-[#ede8d633] px-1 text-center text-[10px] font-semibold uppercase tracking-widest text-[#80745f] dark:border-slate-500 dark:text-slate-300">{object.label || "Zona"}</span>;
  return <span className="flex h-full w-full items-center justify-center text-center text-xs font-semibold text-[#6d7068] dark:text-slate-200">{object.label || "Texto"}</span>;
}

export function FloorPlanTableSeats({ chairs }: { chairs: number | null | undefined }) {
  if (!chairs || chairs < 1) return null;
  const positions = [
    ["left", 50], ["right", 50], ["top", 50], ["bottom", 50],
    ["top", 25], ["bottom", 25], ["top", 75], ["bottom", 75],
    ["left", 25], ["right", 25], ["left", 75], ["right", 75],
    ["top", 10], ["bottom", 10], ["top", 90], ["bottom", 90],
  ] as const;
  return <>{positions.slice(0, Math.min(Math.floor(chairs), positions.length)).map(([side, offset], index) => {
    const horizontal = side === "top" || side === "bottom";
    const compact = chairs > 8;
    return <span key={index} aria-hidden="true"
      className={`pointer-events-none absolute z-10 block rounded-[8px] border-2 border-[#79583e] bg-[#c99d6c] shadow-[0_5px_9px_#3d392d55] dark:border-[#d2b38b] dark:bg-[#98714f] ${compact ? "h-5 w-5" : "h-7 w-7"} ${side === "top" ? "-translate-x-1/2" : side === "bottom" ? "-translate-x-1/2 rotate-180" : side === "left" ? "-translate-y-1/2 -rotate-90" : "-translate-y-1/2 rotate-90"}`}
      style={horizontal ? { [side]: compact ? "-26px" : "-36px", left: `${offset}%` } : { [side]: compact ? "-26px" : "-36px", top: `${offset}%` }}>
      <span className="absolute inset-x-[2px] top-[1px] h-[7px] rounded-t-[5px] bg-[#765238] shadow-[0_2px_2px_#59412b55] dark:bg-[#dfc19a]"/>
      <span className="absolute inset-x-[5px] bottom-[3px] top-[9px] rounded-[4px] border border-[#be946b] bg-[#f5e7cc] dark:border-[#b18b67] dark:bg-[#e5cda8]"/>
      <span className="absolute bottom-[3px] left-[1px] top-[8px] w-[3px] rounded-sm bg-[#886044] dark:bg-[#cba77c]"/>
      <span className="absolute bottom-[3px] right-[1px] top-[8px] w-[3px] rounded-sm bg-[#886044] dark:bg-[#cba77c]"/>
    </span>
  })}</>;
}
