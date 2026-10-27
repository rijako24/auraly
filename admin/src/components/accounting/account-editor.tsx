"use client";

import { useState } from "react";
import { useMutation, useQuery } from "@tanstack/react-query";
import { toast } from "sonner";
import { accountingApi, type AccountingAccount } from "@/services/api/accounting";
import { useReferenceOptions } from "@/hooks/use-reference-options";
import { useBusinessContextStore } from "@/stores/business-context-store";
import { PagedEntitySelect } from "@/components/forms/paged-entity-select";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";

export function AccountEditor({ tenantId, account, types, typesLoading, typesError, onRetryTypes, onClose, onSaved }: {
  tenantId: string; account: AccountingAccount | null; types: Array<{code:string;label:string}>;
  typesLoading: boolean; typesError: Error | null; onRetryTypes: () => void;
  onClose: () => void; onSaved: (account: AccountingAccount, created: boolean) => Promise<void>;
}) {
  const [accountId] = useState(() => crypto.randomUUID());
  const [code, setCode] = useState(account?.code ?? ""), [name, setName] = useState(account?.name ?? "");
  const [type, setType] = useState(account?.accountType ?? ""), [party, setParty] = useState(account?.requiresParty ?? false);
  const [grouping, setGrouping] = useState(false);
  const [properties, setProperties] = useState(account?.semantics ?? null);
  const [parent, setParent] = useState<AccountingAccount | null>(null);
  const functions = useReferenceOptions("accounting-financial-function");
  const balances = useReferenceOptions("accounting-balance-side");
  const payments = useReferenceOptions("payment-method");
  const businesses = useBusinessContextStore(state => state.businesses);
  const businessId = useBusinessContextStore(state => state.selectedBusinessId);
  const locked = account?.configurationLocked ?? false;
  const functionCode = properties?.financialFunction ?? "";
  const treasury = ["Cash", "Bank", "CardClearing", "CreditCardLiability"].includes(functionCode);
  const bankApplicable = ["Bank", "CardClearing", "CreditCardLiability"].includes(functionCode);
  const bankKey = ["accounting", tenantId, businessId, "bank-accounts"];
  const banks = useQuery({ queryKey: bankKey, queryFn: () => accountingApi.bankAccounts(true), enabled: bankApplicable, staleTime: 300_000 });
  const parentId = properties?.parentAccountId ?? "";
  const resolvedParent = useQuery({
    queryKey: ["account-parent", tenantId, parentId],
    queryFn: () => accountingApi.accountOptions({ page: 1, pageSize: 1, accountId: parentId, groupingOnly: true }),
    enabled: !!parentId && !parent, staleTime: 300_000,
  });
  const currentParent = parent ?? resolvedParent.data?.items[0];
  const allowsPosting = account?.allowsPosting ?? (code.length >= 6 && !grouping);
  const setProperty = (value: Partial<NonNullable<AccountingAccount["semantics"]>>) => setProperties(current => ({
    parentAccountId: null, financialFunction: "", balanceSide: "", businessId: null, bankAccountId: null,
    paymentMethodOptionId: null, ...current, ...value,
  }));
  const save = useMutation({
    mutationFn: () => account
      ? accountingApi.updateAccount(account.accountId, { name: name.trim(), requiresParty: party, rowVersion: account.rowVersion ?? "",
          ...(properties ? { semantics: properties } : {}) })
      : accountingApi.createAccount({ accountId, tenantId, code, name: name.trim(), accountType: type,
          allowsPosting, requiresParty: allowsPosting && party, semantics: properties }),
    onSuccess: value => onSaved(value, !account), onError: error => toast.error(error.message),
  });
  const catalogs = [functions, balances, ...(treasury ? [payments] : [])];
  const failed = catalogs.find(query => query.isError);
  const loading = catalogs.some(query => query.isPending);
  const configurationDisabled = locked || save.isPending;
  const incomplete = (!account || !!properties) && (!properties?.financialFunction || !properties.balanceSide || (code.length > 6 && !parentId));
  return <Dialog open onOpenChange={open => { if (!open && !save.isPending) onClose(); }}>
    <DialogContent className="max-h-[90dvh] overflow-y-auto sm:max-w-2xl">
      <DialogHeader><DialogTitle>{account ? "Editar cuenta PUC" : "Nueva cuenta PUC"}</DialogTitle>
        <DialogDescription>Define el auxiliar con tu propia numeración. La sede, el banco y el medio de pago no determinan su código.</DialogDescription></DialogHeader>
      <form className="space-y-4" onSubmit={event => { event.preventDefault(); if (!save.isPending) save.mutate(); }}>
        <div className="space-y-2"><Label htmlFor="puc-code">Código PUC</Label><Input id="puc-code" value={code} maxLength={32} inputMode="numeric" disabled={!!account || save.isPending} onChange={event => setCode(event.target.value.replace(/\D/g, ""))} required/>
          <p className="text-xs text-muted-foreground">Conserva el prefijo de la cuenta padre y agrega los dígitos que necesites, hasta 32 en total.</p></div>
        <div className="space-y-2"><Label htmlFor="puc-name">Nombre</Label><Input id="puc-name" value={name} maxLength={200} disabled={save.isPending} onChange={event => setName(event.target.value)} required autoFocus={!!account}/></div>
        <div className="space-y-2"><Label htmlFor="puc-type">Tipo de cuenta</Label><Select value={type} disabled={!!account || save.isPending || typesLoading} onValueChange={setType}><SelectTrigger id="puc-type"><SelectValue placeholder="Seleccionar tipo"/></SelectTrigger><SelectContent>{types.map(item => <SelectItem key={item.code} value={item.code}>{item.label}</SelectItem>)}</SelectContent></Select>
          {typesError && <div role="alert"><p>{typesError.message}</p><Button type="button" variant="outline" onClick={onRetryTypes}>Reintentar tipos de cuenta</Button></div>}
          {!typesLoading && !typesError && types.length === 0 && <p>No hay tipos de cuenta activos.</p>}
        </div>
        {!account && code.length >= 6 && <label className="flex items-center gap-2 text-sm"><Checkbox checked={grouping} disabled={save.isPending} onCheckedChange={checked => setGrouping(checked === true)}/>Cuenta de agrupación para auxiliares</label>}
        <div><label className="flex items-center gap-2 text-sm"><Checkbox checked={allowsPosting && party} disabled={!allowsPosting || save.isPending} onCheckedChange={checked => setParty(checked === true)}/>Exigir tercero en cada partida</label>
          <p className="mt-1 text-xs text-muted-foreground">Disponible para cuentas con movimientos. Es independiente del tercero principal del comprobante; las cuentas por cobrar, por pagar y de anticipos lo requieren.</p></div>
        {locked && <p className="text-sm text-muted-foreground">Esta cuenta ya fue utilizada. Su código y propiedades financieras se conservan; puedes actualizar su nombre.</p>}
        {loading ? <p>Cargando propiedades contables…</p> : failed ? <div role="alert"><p>{failed.error.message}</p><Button type="button" variant="outline" onClick={() => void failed.refetch()}>Reintentar</Button></div> : <>
          <div className="space-y-2"><Label>Cuenta padre</Label><PagedEntitySelect<AccountingAccount>
            queryKey={["account-parent-options", tenantId]} value={parentId} disabled={configurationDisabled}
            loadPage={(search, page, pageSize) => accountingApi.accountOptions({ search, page, pageSize, groupingOnly: true })}
            getOption={item => ({ value: item.accountId, label: `${item.code} · ${item.name}` })}
            selectedOption={currentParent ? { value: currentParent.accountId, label: `${currentParent.code} · ${currentParent.name}` } : parentId ? { value: parentId, label: "Cuenta padre guardada" } : null}
            onChange={(id, _option, item) => { setParent(item ?? null); setProperty({ parentAccountId: id }); }}
            onClear={() => { setParent(null); setProperty({ parentAccountId: null }); }} ariaLabel="Cuenta padre" searchPlaceholder="Buscar agrupación por código o nombre…" openOnTyping/>
          </div>
          <div className="space-y-2"><Label htmlFor="puc-function">Función financiera</Label><Select value={functionCode} disabled={configurationDisabled} onValueChange={financialFunction => setProperty({ financialFunction, bankAccountId: null, paymentMethodOptionId: null })}><SelectTrigger id="puc-function"><SelectValue placeholder="Seleccionar función"/></SelectTrigger><SelectContent>{functionCode && !functions.data?.some(item => item.code === functionCode) && <SelectItem value={functionCode} disabled>{functionCode} · no disponible para nuevas selecciones</SelectItem>}{functions.data?.map(item => <SelectItem key={item.code} value={item.code}>{item.label}</SelectItem>)}</SelectContent></Select></div>
          <div className="space-y-2"><Label htmlFor="puc-balance">Naturaleza del saldo</Label><Select value={properties?.balanceSide ?? ""} disabled={configurationDisabled} onValueChange={balanceSide => setProperty({ balanceSide })}><SelectTrigger id="puc-balance"><SelectValue placeholder="Débito o crédito"/></SelectTrigger><SelectContent>{balances.data?.map(item => <SelectItem key={item.code} value={item.code}>{item.label}</SelectItem>)}</SelectContent></Select></div>
          <div className="space-y-2"><Label htmlFor="puc-business">Sede del auxiliar</Label><Select value={properties?.businessId ?? "all"} disabled={configurationDisabled} onValueChange={value => setProperty({ businessId: value === "all" ? null : value })}><SelectTrigger id="puc-business"><SelectValue/></SelectTrigger><SelectContent><SelectItem value="all">Compartido entre sedes</SelectItem>{businesses.map(item => <SelectItem key={item.businessId} value={item.businessId}>{item.name}</SelectItem>)}</SelectContent></Select></div>
          {bankApplicable && <div className="space-y-2"><Label htmlFor="puc-bank">Cuenta bancaria vinculada</Label>{banks.isError ? <div role="alert"><p>{banks.error.message}</p><Button type="button" variant="outline" onClick={() => void banks.refetch()}>Reintentar bancos</Button></div> : <Select value={properties?.bankAccountId ?? "none"} disabled={configurationDisabled || banks.isPending} onValueChange={value => setProperty({ bankAccountId: value === "none" ? null : value })}><SelectTrigger id="puc-bank"><SelectValue placeholder="Seleccionar cuenta bancaria"/></SelectTrigger><SelectContent><SelectItem value="none">Sin cuenta bancaria</SelectItem>{banks.data?.filter(item => item.isActive || item.bankAccountId === properties?.bankAccountId).map(item => <SelectItem key={item.bankAccountId} value={item.bankAccountId} disabled={!item.isActive}>{item.displayName}{!item.isActive ? " · inactiva" : ""}</SelectItem>)}</SelectContent></Select>}</div>}
          {treasury && <div className="space-y-2"><Label htmlFor="puc-payment">Medio de pago del auxiliar</Label><Select value={properties?.paymentMethodOptionId ?? "none"} disabled={configurationDisabled} onValueChange={value => setProperty({ paymentMethodOptionId: value === "none" ? null : value })}><SelectTrigger id="puc-payment"><SelectValue/></SelectTrigger><SelectContent><SelectItem value="none">Sin medio específico</SelectItem>{payments.data?.map(item => <SelectItem key={item.id} value={item.id}>{item.label}</SelectItem>)}</SelectContent></Select></div>}
        </>}
        {save.isError && <p role="alert" className="text-sm text-destructive">{save.error.message}</p>}
        <DialogFooter><Button type="button" variant="outline" disabled={save.isPending} onClick={onClose}>Cancelar</Button><Button disabled={save.isPending || !code || !name.trim() || !type || (!!account && !account.rowVersion) || incomplete || loading || !!failed || (bankApplicable && (banks.isPending || banks.isError))}>{save.isPending ? "Guardando…" : account ? "Guardar cambios" : "Crear cuenta"}</Button></DialogFooter>
      </form>
    </DialogContent>
  </Dialog>;
}
