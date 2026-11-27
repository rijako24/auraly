"use client";
import {useState} from "react";
import {PagedEntitySelect} from "@/components/forms/paged-entity-select";
import {useTenantContextStore} from "@/stores/tenant-context-store";
import {useBusinessContextStore} from "@/stores/business-context-store";
import {payrollApi,type PayrollRunSummary,type PayrollCatalogOption} from "@/services/api/payroll";
const money=new Intl.NumberFormat("es-CO",{style:"currency",currency:"COP",maximumFractionDigits:0});
export function PayrollRunSelect({value,label,regularOnly=false,approvedOnly=true,disabled=false,filters,statusOptions,onChange,onClear,selectedRun}:{value:string;label:string;regularOnly?:boolean;approvedOnly?:boolean;disabled?:boolean;filters?:{status?:string;paymentFrom?:string;paymentTo?:string;workerSearch?:string};statusOptions?:PayrollCatalogOption[];onChange:(run:PayrollRunSummary)=>void;onClear?:()=>void;selectedRun?:PayrollRunSummary|null}) {
  const tenant=useTenantContextStore(state=>state.selectedTenantId),business=useBusinessContextStore(state=>state.selectedBusinessId);
  const [selected,setSelected]=useState<PayrollRunSummary|null>(null);
  const option=(row:PayrollRunSummary)=>({value:row.payrollRunId,label:`${row.periodStart} — ${row.periodEnd}`,description:[statusOptions?.find(value=>value.code===row.status)?.label,row.status==="Draft"?"Pendiente de cálculo":`Neto liquidado ${money.format(row.netPayable)}`,row.payFrequencyName].filter(Boolean).join(" · ")});
  const current = selectedRun ?? selected;
  return <PagedEntitySelect<PayrollRunSummary> queryKey={["payroll-run-select",tenant,business,regularOnly,approvedOnly,filters]} value={value} ariaLabel={label} disabled={disabled}
    selectedOption={current?.payrollRunId===value?option(current):null} onClear={value ? onClear : undefined} searchPlaceholder="Buscar período (AAAA-MM-DD)…" pageSize={20}
    loadPage={(search,page,pageSize)=>payrollApi.runs({...filters,page,pageSize,search:search||undefined,status:approvedOnly?"Approved":filters?.status,kind:regularOnly?"Regular":undefined})}
    getOption={option} onChange={(_id,_option,row)=>{if(row){setSelected(row);onChange(row);}}}/>;
}
