"use client";

import { Loader2, Plus, Search, UserRound, UserRoundX, X } from "lucide-react";
import { type FormEvent, useCallback, useEffect, useRef, useState } from "react";

import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import type {
  PosAdministrativeDivision,
  PosCity,
  PosCountry,
  PosCreateCustomerInput,
  PosCustomer,
  PosCustomerSearchPage,
} from "@/services/pos/pos-edge-client";

export function PosCustomerSearchDialog({
  busy,
  connected,
  onSearch,
  onSelect,
  onCountries,
  onDivisions,
  onCities,
  onCreate,
  onCancel,
}: {
  busy: boolean;
  connected: boolean;
  onSearch: (term: string, skip: number) => Promise<PosCustomerSearchPage>;
  onSelect: (customer: PosCustomer | null) => Promise<void>;
  onCountries: () => Promise<PosCountry[]>;
  onDivisions: (countryId: string) => Promise<PosAdministrativeDivision[]>;
  onCities: (divisionId: string) => Promise<PosCity[]>;
  onCreate: (input: PosCreateCustomerInput) => Promise<PosCustomer>;
  onCancel: () => void;
}) {
  const [creating, setCreating] = useState(false);
  const [term, setTerm] = useState("");
  const [results, setResults] = useState<PosCustomer[]>([]);
  const [selected, setSelected] = useState(0);
  const [hasMore, setHasMore] = useState(false);
  const [nextOffset, setNextOffset] = useState<number | null>(null);
  const [loading, setLoading] = useState(true);
  const [loadingMore, setLoadingMore] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const requestVersion = useRef(0);
  const searchInput = useRef<HTMLInputElement>(null);
  const resultButtons = useRef(new Map<number, HTMLButtonElement>());

  const focusResult = (index: number) => {
    if (index < 0 || index >= results.length) {
      setSelected(0);
      searchInput.current?.focus();
      return;
    }
    setSelected(index);
    resultButtons.current.get(index)?.focus();
  };

  useEffect(() => {
    if (creating) return;
    const version = ++requestVersion.current;
    const timer = window.setTimeout(() => {
      setLoading(true);
      setError(null);
      void onSearch(term.trim(), 0)
        .then((page) => {
          if (requestVersion.current !== version) return;
          setResults(page.items);
          setSelected(0);
          setHasMore(page.hasMore);
          setNextOffset(page.nextOffset);
        })
        .catch(() => {
          if (requestVersion.current !== version) return;
          setResults([]);
          setHasMore(false);
          setNextOffset(null);
          setError("No fue posible consultar los clientes sincronizados.");
        })
        .finally(() => {
          if (requestVersion.current === version) setLoading(false);
        });
    }, term.trim() ? 180 : 0);
    return () => window.clearTimeout(timer);
  }, [creating, onSearch, term]);

  useEffect(() => {
    const close = (event: KeyboardEvent) => {
      if (event.key !== "Escape" || busy) return;
      event.preventDefault();
      if (creating) setCreating(false);
      else onCancel();
    };
    window.addEventListener("keydown", close);
    return () => window.removeEventListener("keydown", close);
  }, [busy, creating, onCancel]);

  const loadMore = useCallback(async () => {
    if (busy || loading || loadingMore || !hasMore || nextOffset === null) return;
    setLoadingMore(true);
    try {
      const page = await onSearch(term.trim(), nextOffset);
      setResults((current) => {
        const known = new Set(current.map((customer) => customer.customerId));
        return [...current, ...page.items.filter((customer) => !known.has(customer.customerId))];
      });
      setHasMore(page.hasMore);
      setNextOffset(page.nextOffset);
    } catch {
      setError("No fue posible cargar más clientes.");
    } finally {
      setLoadingMore(false);
    }
  }, [busy, hasMore, loading, loadingMore, nextOffset, onSearch, term]);

  return (
    <div className="fixed inset-0 z-50 grid place-items-center bg-slate-950/60 p-4">
      <section className="flex max-h-[90vh] w-full max-w-3xl flex-col overflow-hidden rounded-2xl bg-white shadow-2xl">
        <header className="flex items-start justify-between border-b border-slate-200 p-5">
          <div>
            <h2 className="flex items-center gap-2 text-xl font-semibold">
              <UserRound className="h-5 w-5 text-teal-700" />
              {creating ? "Crear cliente" : "Buscar cliente"}
            </h2>
            <p className="mt-1 text-sm text-slate-500">
              {creating ? "Se guarda en Auraly Server y queda disponible inmediatamente en esta venta." : "Busca por nombre o identificación. La selección recalcula los precios."}
            </p>
          </div>
          <button type="button" onClick={onCancel} aria-label="Cerrar búsqueda" className="grid h-10 w-10 place-items-center rounded-lg text-slate-500 hover:bg-slate-100">
            <X className="h-5 w-5" />
          </button>
        </header>

        {creating ? (
          <PosCustomerCreateForm
            busy={busy}
            onCountries={onCountries}
            onDivisions={onDivisions}
            onCities={onCities}
            onCreate={async (input) => {
              const customer = await onCreate(input);
              await onSelect(customer);
            }}
            onBack={() => setCreating(false)}
          />
        ) : (
          <>
            <div className="p-5 pb-3">
              <label className="relative block">
                <Search className="pointer-events-none absolute left-4 top-1/2 h-5 w-5 -translate-y-1/2 text-slate-400" />
                <input ref={searchInput} autoFocus value={term} onChange={(event) => setTerm(event.target.value)}
                  onKeyDown={(event) => {
                    if (event.key === "ArrowDown" && results.length) {
                      event.preventDefault();
                      focusResult(0);
                      return;
                    }
                    if (event.key === "ArrowUp" && results.length) {
                      event.preventDefault();
                      focusResult(results.length - 1);
                      return;
                    }
                    if (event.key === "Enter" && results[selected]) {
                      event.preventDefault();
                      void onSelect(results[selected]);
                    }
                  }}
                  className="h-14 w-full rounded-xl border-2 border-teal-700/25 bg-slate-50 pl-12 pr-12 text-lg font-medium outline-none focus:border-teal-600 focus:bg-white focus:ring-4 focus:ring-teal-600/10"
                  placeholder="Nombre o identificación" />
                {loading && <span className="pointer-events-none absolute right-4 top-1/2 grid h-5 w-5 -translate-y-1/2 place-items-center" role="status" aria-label="Cargando clientes"><Loader2 className="h-5 w-5 animate-spin text-teal-700" /></span>}
              </label>
              <div className="mt-3 grid gap-2 sm:grid-cols-2">
                <button type="button" disabled={busy} onClick={() => void onSelect(null)} className="flex h-11 items-center gap-3 rounded-xl border border-slate-200 px-4 text-left font-semibold text-slate-700 hover:bg-slate-50">
                  <UserRoundX className="h-5 w-5 text-slate-500" /> Consumidor final
                </button>
                <button type="button" disabled={busy || !connected} onClick={() => setCreating(true)} title={connected ? "Crear cliente" : "Requiere conexión con Auraly Server"} className="flex h-11 items-center gap-3 rounded-xl border border-teal-200 px-4 text-left font-semibold text-teal-800 hover:bg-teal-50 disabled:cursor-not-allowed disabled:opacity-50">
                  <Plus className="h-5 w-5" /> Nuevo cliente
                </button>
              </div>
              {!connected && <p className="mt-2 text-xs text-amber-700">Puedes buscar clientes sincronizados sin conexión. Para crear uno nuevo se requiere Auraly Server.</p>}
            </div>
            <div className="min-h-52 flex-1 overflow-auto px-5 pb-5" onScroll={(event) => {
              const list = event.currentTarget;
              if (list.scrollHeight - list.scrollTop - list.clientHeight < 100) void loadMore();
            }}>
              {results.map((customer, index) => (
                <button key={customer.customerId} type="button" disabled={busy}
                  ref={(element) => { if (element) resultButtons.current.set(index, element); else resultButtons.current.delete(index); }}
                  onFocus={() => { setSelected(index); if (index === results.length - 1) void loadMore(); }}
                  onKeyDown={(event) => {
                    if (event.key === "ArrowDown") {
                      event.preventDefault();
                      focusResult(index + 1);
                    } else if (event.key === "ArrowUp") {
                      event.preventDefault();
                      focusResult(index - 1);
                    } else if (event.key === "Enter") {
                      event.preventDefault();
                      void onSelect(customer);
                    }
                  }}
                  onMouseEnter={() => setSelected(index)} onClick={() => void onSelect(customer)}
                  className={`grid w-full grid-cols-[minmax(0,1fr)_180px] items-center gap-4 border-b border-slate-100 px-3 py-3 text-left outline-none ${selected === index ? "bg-teal-50 ring-2 ring-inset ring-teal-600/25" : "hover:bg-slate-50"}`}>
                  <span className="truncate font-semibold text-slate-900">{customer.name}</span>
                  <span className="text-right text-sm tabular-nums text-slate-600">{customer.identification}</span>
                </button>
              ))}
              {!loading && !results.length && !error && <p className="grid min-h-40 place-items-center text-sm text-slate-500">No encontramos clientes.</p>}
              {loadingMore && <p className="flex justify-center gap-2 py-4 text-sm text-teal-800"><Loader2 className="h-4 w-4 animate-spin" /> Cargando más clientes</p>}
              {error && <p className="py-5 text-center text-sm font-medium text-red-700">{error}</p>}
            </div>
          </>
        )}
      </section>
    </div>
  );
}

function PosCustomerCreateForm({ busy, onCountries, onDivisions, onCities, onCreate, onBack }: {
  busy: boolean;
  onCountries: () => Promise<PosCountry[]>;
  onDivisions: (countryId: string) => Promise<PosAdministrativeDivision[]>;
  onCities: (divisionId: string) => Promise<PosCity[]>;
  onCreate: (input: PosCreateCustomerInput) => Promise<void>;
  onBack: () => void;
}) {
  const [countries, setCountries] = useState<PosCountry[]>([]);
  const [divisions, setDivisions] = useState<PosAdministrativeDivision[]>([]);
  const [cities, setCities] = useState<PosCity[]>([]);
  const [countryId, setCountryId] = useState("");
  const [divisionId, setDivisionId] = useState("");
  const [cityId, setCityId] = useState("");
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [form, setForm] = useState({ identificationTypeCode: "CC", identification: "", displayName: "", phone: "", email: "", address: "", neighborhood: "" });

  useEffect(() => { void onCountries().then(setCountries).catch(() => setError("No fue posible cargar los países.")); }, [onCountries]);
  useEffect(() => { setDivisionId(""); setCityId(""); setDivisions([]); setCities([]); if (countryId) void onDivisions(countryId).then(setDivisions).catch(() => setError("No fue posible cargar los departamentos.")); }, [countryId, onDivisions]);
  useEffect(() => { setCityId(""); setCities([]); if (divisionId) void onCities(divisionId).then(setCities).catch(() => setError("No fue posible cargar las ciudades.")); }, [divisionId, onCities]);
  const set = (key: keyof typeof form, value: string) => setForm((current) => ({ ...current, [key]: value }));

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (!countryId || !divisionId || !cityId || !form.identification.trim() || !form.displayName.trim() || !form.address.trim()) {
      setError("Completa identificación, nombre, ubicación y dirección."); return;
    }
    setSaving(true); setError(null);
    try {
      await onCreate({
        partyType: "NaturalPerson", identificationCountryId: countryId,
        identificationTypeCode: form.identificationTypeCode.trim().toUpperCase(), identification: form.identification.trim(),
        verificationDigit: null, displayName: form.displayName.trim(), legalName: null,
        firstName: form.displayName.trim(), lastName: null, email: form.email.trim() || null, phone: form.phone.trim() || null,
        primarySite: { code: "PRINCIPAL", name: "Principal", countryId, administrativeDivisionId: divisionId, cityId,
          addressLine: form.address.trim(), neighborhood: form.neighborhood.trim() || null, postalCode: null,
          email: form.email.trim() || null, phone: form.phone.trim() || null, isPrimary: true },
      });
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "No fue posible crear el cliente.");
    } finally { setSaving(false); }
  }

  return <form onSubmit={submit} className="min-h-0 flex-1 overflow-auto p-5">
    <div className="grid gap-4 sm:grid-cols-2">
      <Field label="Tipo de identificación"><Select value={form.identificationTypeCode} onValueChange={value=>set("identificationTypeCode",value)}><SelectTrigger autoFocus><SelectValue/></SelectTrigger><SelectContent><SelectItem value="CC">Cédula</SelectItem><SelectItem value="NIT">NIT</SelectItem><SelectItem value="CE">Cédula de extranjería</SelectItem><SelectItem value="PP">Pasaporte</SelectItem></SelectContent></Select></Field>
      <Field label="Número"><Input value={form.identification} onChange={(e) => set("identification", e.target.value)} /></Field>
      <Field label="Nombre completo"><Input value={form.displayName} onChange={(e) => set("displayName", e.target.value)} /></Field>
      <Field label="Teléfono"><Input value={form.phone} onChange={(e) => set("phone", e.target.value)} /></Field>
      <Field label="Correo"><Input type="email" value={form.email} onChange={(e) => set("email", e.target.value)} /></Field>
      <Field label="País"><Select value={countryId} onValueChange={setCountryId}><SelectTrigger><SelectValue placeholder="Selecciona" /></SelectTrigger><SelectContent>{countries.map((item) => <SelectItem key={item.countryId} value={item.countryId}>{item.name}</SelectItem>)}</SelectContent></Select></Field>
      <Field label="Departamento / estado"><Select value={divisionId} onValueChange={setDivisionId}><SelectTrigger><SelectValue placeholder="Selecciona" /></SelectTrigger><SelectContent>{divisions.map((item) => <SelectItem key={item.administrativeDivisionId} value={item.administrativeDivisionId}>{item.name}</SelectItem>)}</SelectContent></Select></Field>
      <Field label="Ciudad"><Select value={cityId} onValueChange={setCityId}><SelectTrigger><SelectValue placeholder="Selecciona" /></SelectTrigger><SelectContent>{cities.map((item) => <SelectItem key={item.cityId} value={item.cityId}>{item.name}</SelectItem>)}</SelectContent></Select></Field>
      <Field label="Dirección"><Input value={form.address} onChange={(e) => set("address", e.target.value)} /></Field>
      <Field label="Barrio (escrito)"><Input value={form.neighborhood} onChange={(e) => set("neighborhood", e.target.value)} /></Field>
    </div>
    {error && <p className="mt-4 rounded-lg bg-red-50 p-3 text-sm font-medium text-red-700">{error}</p>}
    <div className="mt-5 flex justify-end gap-2"><Button type="button" variant="outline" onClick={onBack}>Volver</Button><Button type="submit" disabled={busy || saving}>{saving && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}Crear y seleccionar</Button></div>
  </form>;
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return <div className="space-y-2"><Label>{label}</Label>{children}</div>;
}
