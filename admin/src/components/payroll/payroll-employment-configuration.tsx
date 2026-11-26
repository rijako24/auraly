"use client";

import { useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { PartySelect, PartyRoleSelect } from "@/components/parties/party-role-select";
import { Input } from "@/components/ui/input";
import { DatePicker } from "@/components/ui/date-picker";
import { useBusinessDates } from "@/hooks/use-business-dates";
import { PagedEntitySelect } from "@/components/forms/paged-entity-select";
import { CostCenterSelect } from "@/components/accounting/cost-center-select";
import { type AccountingCostCenter } from "@/services/api/accounting";
import { payrollApi, type PayrollEmploymentConfiguration, type PayrollEmploymentConfigurationView, type PayrollEmploymentDetails, type PayrollProfile } from "@/services/api/payroll";
import { useTenantContextStore } from "@/stores/tenant-context-store";

export const emptyPayrollConfiguration: PayrollEmploymentConfiguration = {
  accountingProfileId:null,healthPartyId:null,pensionPartyId:null,severancePartyId:null,
  riskPartyId:null,compensationPartyId:null,costCenterId:null,
};

const emptyDetails:PayrollEmploymentDetails={jobTitle:null,workSite:null,plannedEndDate:null,withdrawalReason:null,
  trialReviewDate:null,arlPilaCode:null,supervisorPartyId:null,vacationCutoffDate:null,vacationDaysUsed:null};

export function PayrollEmploymentDetailsFields({value,supervisorName,onChange,disabled}:{
  value:PayrollEmploymentDetails|null|undefined;supervisorName?:string|null;
  onChange:(value:PayrollEmploymentDetails)=>void;disabled:boolean;
}){
  const today=useBusinessDates().today(),details=value??emptyDetails;
  const [supervisor,setSupervisor]=useState(supervisorName);
  function change<K extends keyof PayrollEmploymentDetails>(key:K,next:PayrollEmploymentDetails[K]){onChange({...details,[key]:next});}
  return <fieldset disabled={disabled} className="grid min-w-0 grid-cols-1 gap-4 rounded-2xl border p-4 sm:col-span-2 sm:grid-cols-2">
    <legend className="px-2 font-medium">Datos del contrato</legend>
    <label className="min-w-0 space-y-2"><span className="text-sm">Cargo</span><Input maxLength={160} value={details.jobTitle??""} onChange={event=>change("jobTitle",event.target.value||null)}/></label>
    <label className="min-w-0 space-y-2"><span className="text-sm">Sitio de trabajo</span><Input maxLength={160} value={details.workSite??""} onChange={event=>change("workSite",event.target.value||null)}/></label>
    <label className="min-w-0 space-y-2"><span className="text-sm">Terminación prevista</span><DatePicker today={today} value={details.plannedEndDate??""} onChange={date=>change("plannedEndDate",date||null)} disabled={disabled}/></label>
    <label className="min-w-0 space-y-2"><span className="text-sm">Evaluación de período de prueba</span><DatePicker today={today} value={details.trialReviewDate??""} onChange={date=>change("trialReviewDate",date||null)} disabled={disabled}/></label>
    <label className="min-w-0 space-y-2"><span className="text-sm">Motivo de retiro</span><Input maxLength={250} value={details.withdrawalReason??""} onChange={event=>change("withdrawalReason",event.target.value||null)}/></label>
    <label className="min-w-0 space-y-2"><span className="text-sm">Código ARL / PILA</span><Input maxLength={32} value={details.arlPilaCode??""} onChange={event=>change("arlPilaCode",event.target.value||null)}/></label>
    <label className="min-w-0 space-y-2 sm:col-span-2"><span className="text-sm">Jefe inmediato</span><PartyRoleSelect role="Employee" includePartyId ariaLabel="Jefe inmediato" disabled={disabled}
      value={details.supervisorPartyId??"_none"} leadingOptions={[{value:"_none",label:"Sin jefe asignado"}]}
      selectedOption={details.supervisorPartyId?{value:details.supervisorPartyId,label:supervisor??"Jefe seleccionado"}:null}
      onChange={(id,party)=>{change("supervisorPartyId",id==="_none"?null:id);setSupervisor(party?.displayName);}}/></label>
    <label className="min-w-0 space-y-2"><span className="text-sm">Corte de vacaciones</span><DatePicker today={today} value={details.vacationCutoffDate??""} onChange={date=>change("vacationCutoffDate",date||null)} disabled={disabled}/></label>
    <label className="min-w-0 space-y-2"><span className="text-sm">Días de vacaciones usados al corte</span><Input type="number" min={0} max={99999.9999} step="0.0001" value={details.vacationDaysUsed??""} onChange={event=>change("vacationDaysUsed",event.target.value?event.currentTarget.valueAsNumber:null)}/></label>
  </fieldset>;
}

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
    <label className="space-y-2"><span className="text-sm">Perfil de nómina</span>
      <PagedEntitySelect<PayrollProfile> queryKey={["payroll-profiles",tenant,"active"]} value={value.accountingProfileId??"_general"}
        disabled={disabled} ariaLabel="Perfil de nómina" leadingOptions={[{value:"_general",label:"Sin perfil (pendiente de configurar)"}]}
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
    <p className="text-xs text-muted-foreground sm:col-span-2">El perfil reúne los porcentajes vigentes y las cuentas por concepto. Se aplica al liquidar sin volver a elegir reglas. Si asignas un perfil, completa las cinco afiliaciones con las entidades del contrato.</p>
  </fieldset>;
}
