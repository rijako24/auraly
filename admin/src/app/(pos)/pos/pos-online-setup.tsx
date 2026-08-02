"use client";

import { Building2, Loader2, MonitorSmartphone, Warehouse } from "lucide-react";
import { useMemo, useState } from "react";

import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import type { SalesWorkspaceOption } from "@/services/pos/online-pos-client";

type Props = {
  options: SalesWorkspaceOption[];
  loading: boolean;
  error: string | null;
  tenantName: string;
  userDisplayName: string;
  onSelect: (option: SalesWorkspaceOption) => Promise<void>;
  edgeCapable?: boolean;
  onEnroll?: (option: SalesWorkspaceOption) => Promise<void>;
};

export function PosOnlineSetup({
  options,
  loading,
  error,
  tenantName,
  userDisplayName,
  onSelect,
  edgeCapable = false,
  onEnroll,
}: Props) {
  const businesses = useMemo(
    () =>
      Array.from(
        new Map(options.map((option) => [option.businessId, option.businessName])),
      ),
    [options],
  );
  const [businessId, setBusinessId] = useState("");
  const [warehouseId, setWarehouseId] = useState("");
  const [selecting, setSelecting] = useState(false);
  const [enrolling, setEnrolling] = useState(false);

  const warehouses = useMemo(
    () => options.filter((option) => option.businessId === businessId),
    [businessId, options],
  );
  const selected = warehouses.find(
    (option) => option.warehouseId === warehouseId,
  );

  async function submit(event: React.FormEvent) {
    event.preventDefault();
    if (!selected || selecting || enrolling) return;
    setSelecting(true);
    try {
      await onSelect(selected);
    } finally {
      setSelecting(false);
    }
  }

  async function enroll() {
    if (!selected || !onEnroll || selecting || enrolling) return;
    setEnrolling(true);
    try {
      await onEnroll(selected);
    } finally {
      setEnrolling(false);
    }
  }

  return (
    <main className="relative grid min-h-screen place-items-center overflow-hidden bg-[#071a1d] p-5 text-white">
      <div className="absolute -left-40 -top-40 h-[34rem] w-[34rem] rounded-full bg-teal-400/10 blur-3xl" />
      <div className="absolute -bottom-48 -right-32 h-[38rem] w-[38rem] rounded-full bg-cyan-300/10 blur-3xl" />
      <form
        onSubmit={submit}
        className="relative w-full max-w-3xl overflow-hidden rounded-[2rem] border border-white/10 bg-[#0b2428]/95 shadow-2xl"
      >
        <div className="grid gap-0 md:grid-cols-[0.85fr_1.4fr]">
          <section className="border-b border-white/10 bg-gradient-to-br from-teal-400/20 to-transparent p-7 md:border-b-0 md:border-r">
            <div className="grid h-12 w-12 place-items-center rounded-2xl bg-teal-300 text-[#071a1d]">
              <MonitorSmartphone className="h-6 w-6" />
            </div>
            <p className="mt-8 inline-flex rounded-full border border-teal-200/20 bg-teal-300/10 px-3 py-1 text-xs font-bold uppercase tracking-[0.14em] text-teal-100">
              Empresa · {tenantName || "Auraly"}
            </p>
            <p className="mt-5 text-xs font-bold uppercase tracking-[0.18em] text-teal-200">
              {edgeCapable ? "Preparación del equipo" : "Facturación en línea"}
            </p>
            <h1 className="mt-2 text-3xl font-black tracking-tight">
              Elige dónde vas a trabajar
            </h1>
            <p className="mt-3 text-sm leading-6 text-slate-300">
              Hola, {userDisplayName}. Selecciona la sede y la bodega que
              recibirán los movimientos. Si enrolas este equipo, Auraly
              preparará automáticamente la
              operación offline.
            </p>
          </section>

          <section className="p-7">
            {loading ? (
              <div className="grid min-h-64 place-items-center text-center">
                <div>
                  <Loader2 className="mx-auto h-9 w-9 animate-spin text-teal-300" />
                  <p className="mt-4 font-semibold">Preparando facturación</p>
                  <p className="mt-1 text-sm text-slate-400">
                    Validando sedes, bodegas, series y permisos…
                  </p>
                </div>
              </div>
            ) : (
              <div className="space-y-4">
                <SelectField
                  icon={Building2}
                  label="Sede"
                  value={businessId}
                  onChange={(value) => {
                    setBusinessId(value);
                    setWarehouseId("");
                  }}
                  options={businesses.map(([value, label]) => ({ value, label }))}
                />
                <SelectField
                  icon={Warehouse}
                  label="Bodega"
                  value={warehouseId}
                  disabled={!businessId}
                  onChange={setWarehouseId}
                  options={warehouses.map((option) => ({
                    value: option.warehouseId,
                    label: option.warehouseName + " · " + option.warehouseCode,
                  }))}
                />

                {selected && (
                  <div className="flex items-center gap-3 rounded-2xl border border-teal-300/20 bg-teal-300/10 p-3 text-sm">
                    <Warehouse className="h-5 w-5 shrink-0 text-teal-200" />
                    <div>
                      <p className="font-semibold">
                        {selected.businessName} · {selected.warehouseName}
                      </p>
                      <p className="text-xs text-slate-300">
                        {selected.warehouseAllowsNegativeStockSales
                          ? "Permite confirmar ventas con inventario negativo"
                          : "Valida disponibilidad antes de agregar cantidades"}
                      </p>
                    </div>
                  </div>
                )}

                {error && (
                  <p
                    role="alert"
                    className="rounded-xl border border-red-300/20 bg-red-400/10 p-3 text-sm text-red-100"
                  >
                    {error}
                  </p>
                )}
                {!options.length && !error && (
                  <p className="rounded-xl border border-amber-300/20 bg-amber-300/10 p-3 text-sm text-amber-100">
                    No tienes sedes y bodegas habilitadas para facturar. Revisa
                    permisos, series operativas y resolución fiscal.
                  </p>
                )}

                <div className={edgeCapable ? "grid gap-2 sm:grid-cols-2" : ""}>
                  <button
                    type="submit"
                    disabled={!selected || selecting || enrolling}
                    className="flex h-12 w-full items-center justify-center gap-2 rounded-xl bg-teal-300 font-bold text-[#071a1d] transition hover:bg-teal-200 disabled:cursor-not-allowed disabled:opacity-35"
                  >
                    {selecting && <Loader2 className="h-4 w-4 animate-spin" />}
                    Trabajar en línea
                  </button>
                  {edgeCapable && (
                    <button
                      type="button"
                      disabled={!selected || selecting || enrolling}
                      onClick={() => void enroll()}
                      className="flex h-12 w-full items-center justify-center gap-2 rounded-xl border border-teal-200/30 bg-teal-200/10 font-bold text-teal-100 transition hover:bg-teal-200/20 disabled:cursor-not-allowed disabled:opacity-35"
                    >
                      {enrolling && <Loader2 className="h-4 w-4 animate-spin" />}
                      Preparar modo offline
                    </button>
                  )}
                </div>
              </div>
            )}
          </section>
        </div>
      </form>
    </main>
  );
}

function SelectField({
  icon: Icon,
  label,
  value,
  options,
  disabled,
  onChange,
}: {
  icon: typeof Building2;
  label: string;
  value: string;
  options: Array<{ value: string; label: string }>;
  disabled?: boolean;
  onChange: (value: string) => void;
}) {
  return (
    <div>
      <span className="mb-1.5 flex items-center gap-2 text-sm font-semibold text-slate-200">
        <Icon className="h-4 w-4 text-teal-200" />
        {label}
      </span>
      <Select
        value={value || undefined}
        disabled={disabled}
        onValueChange={onChange}
      >
        <SelectTrigger className="h-12 w-full rounded-xl border-white/15 bg-[#102e33] px-3 text-sm text-white shadow-none focus:border-teal-300 focus:ring-2 focus:ring-teal-300/15 disabled:opacity-40">
          <SelectValue placeholder={"Selecciona " + label.toLocaleLowerCase("es")} />
        </SelectTrigger>
        <SelectContent className="rounded-xl border-slate-200 shadow-2xl">
          {options.map((option) => (
            <SelectItem key={option.value} value={option.value} className="py-2.5">
              {option.label}
            </SelectItem>
          ))}
        </SelectContent>
      </Select>
    </div>
  );
}