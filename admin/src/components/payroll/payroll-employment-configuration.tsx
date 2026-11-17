"use client";

import { useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { PartySelect } from "@/components/parties/party-role-select";
import { PagedEntitySelect } from "@/components/forms/paged-entity-select";
import { CostCenterSelect } from "@/components/accounting/cost-center-select";
import { type AccountingCostCenter } from "@/services/api/accounting";
import { payrollApi, type PayrollEmploymentConfiguration, type PayrollEmploymentConfigurationView, type PayrollProfile } from "@/services/api/payroll";
import { useTenantContextStore } from "@/stores/tenant-context-store";

export const emptyPayrollConfiguration: PayrollEmploymentConfiguration = {
  accountingProfileId:null,healthPartyId:null,pensionPartyId:null,severancePartyId:null,
  riskPartyId:null,compensationPartyId:null,costCenterId:null,
};

export function PayrollEmploymentConfigurationFields({ value, initial, onChange, disabled }: {
  value:PayrollEmploymentConfiguration;initial?:PayrollEmploymentConfigurationView|null;
  onChange:(value:PayrollEmploymentConfiguration)=>void;disabled:boolean;
}) {
  const tenant=useTenantContextStore(state=>state.selectedTenantId);
  const client=useQueryClient();
  const [labels,setLabels]=useState<Record<string,string|null|undefined>>({
    accountingProfileId:initial?.profileName,costCenterId:initial?.costCenterName,
    healthPartyId:initial?.healthName,pensionPartyId:initial?.pensionName,
    severancePartyId:initial?.severanceName,riskPartyId:initial?.riskName,compensationPartyId:initial?.compensationName,
  });
  const institutions=[['healthPartyId','EPS'],['pensionPartyId','Fondo de pensiones'],
    ['severancePartyId','Fondo de cesantías'],['riskPartyId','ARL'],['compensationPartyId','Caja de compensación']] as const;
  return <fieldset disabled={disabled} className="grid gap-4 rounded-2xl border p-4 sm:col-span-2 sm:grid-cols-2">
    <legend className="px-2 font-medium">Afiliaciones y configuración contable</legend>
    <label className="space-y-2"><span className="text-sm">Parámetro de nómina</span>
      <PagedEntitySelect<PayrollProfile> queryKey={["payroll-profiles",tenant,"active"]} value={value.accountingProfileId??"_general"}
        disabled={disabled} ariaLabel="Parámetro de nómina" leadingOptions={[{value:"_general",label:"Sin parámetro (pendiente de configurar)"}]}
        selectedOption={value.accountingProfileId?{value:value.accountingProfileId,label:labels.accountingProfileId??"Perfil seleccionado"}:null}
        loadPage={(search,page,pageSize)=>payrollApi.profiles({search,page,pageSize,isActive:true})}
        getOption={item=>item.isActive?{value:item.profileId,label:`${item.code} · ${item.name}`}:null}
        onChange={(id,option)=>{onChange({...value,accountingProfileId:id==="_general"?null:id});setLabels(current=>({...current,accountingProfileId:option.label}));}}/>
    </label>
    <label className="space-y-2"><span className="text-sm">Centro de costo contractual</span>
      <CostCenterSelect value={value.costCenterId} selectedName={labels.costCenterId} ariaLabel="Centro de costo contractual" disabled={disabled}
        onChange={id=>{onChange({...value,costCenterId:id});setLabels(current=>({...current,costCenterId:id?client.getQueryData<AccountingCostCenter>(["accounting-cost-center",tenant,id])?.name:null}));}}/>
    </label>
    {institutions.map(([key,label])=><label key={key} className="space-y-2"><span className="text-sm">{label}{value.accountingProfileId?" *":""}</span>
      <PartySelect partyType="Organization" value={value[key]??""} disabled={disabled} ariaLabel={label}
        selectedOption={value[key]?{value:value[key]!,label:labels[key]??"Entidad seleccionada"}:null}
        onChange={(id,party)=>{onChange({...value,[key]:id||null});setLabels(current=>({...current,[key]:party?.displayName}));}}/>
    </label>)}
    <p className="text-xs text-muted-foreground sm:col-span-2">El parámetro reúne los porcentajes vigentes y las cuentas por concepto. Se aplica al liquidar sin volver a elegir reglas. Si asignas un perfil, completa las cinco afiliaciones con las entidades del contrato.</p>
  </fieldset>;
}
