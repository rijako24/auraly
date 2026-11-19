"use client";

import { useTenantContextStore } from "@/stores/tenant-context-store";
import { useBusinessContextStore } from "@/stores/business-context-store";
import { PagedEntitySelect } from "@/components/forms/paged-entity-select";
import { dispatchesApi } from "@/services/api/dispatches";

export function DispatchDriverSelect({value,name,onChange}:{value:string;name:string;onChange:(value:string,name:string)=>void}) {
  const tenant = useTenantContextStore(state => state.selectedTenantId);
  const business = useBusinessContextStore(state => state.selectedBusinessId);
  return <PagedEntitySelect queryKey={["dispatch-driver-select",tenant,business]} value={value}
    leadingOptions={[{value:"none",label:"Selecciona un transportador"}]}
    selectedOption={value!=="none"&&name?{value,label:name}:null}
    loadPage={(search,page,pageSize)=>dispatchesApi.drivers({page,pageSize,search:search||undefined})}
    getOption={item=>({value:item.userId,label:item.name})}
    onChange={(id,option)=>onChange(id,id==="none"?"":option.label)}
    ariaLabel="Transportador del despacho" placeholder="Buscar transportador"/>;
}
