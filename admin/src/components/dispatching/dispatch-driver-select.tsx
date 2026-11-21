"use client";

import { useTenantContextStore } from "@/stores/tenant-context-store";
import { useBusinessContextStore } from "@/stores/business-context-store";
import { PagedEntitySelect } from "@/components/forms/paged-entity-select";
import { partiesApi } from "@/services/api/parties";

export function DispatchDriverSelect({value,name,onChange}:{value:string;name:string;onChange:(value:string,name:string)=>void}) {
  const tenant = useTenantContextStore(state => state.selectedTenantId);
  const business = useBusinessContextStore(state => state.selectedBusinessId);
  return <PagedEntitySelect queryKey={["party-role-options",tenant,business,"Carrier"]} value={value}
    leadingOptions={[{value:"none",label:"Selecciona un transportador"}]}
    selectedOption={value!=="none"&&name?{value,label:name}:null}
    loadPage={(search,page,pageSize)=>partiesApi.roleOptions({page,pageSize,role:"Carrier",search:search||undefined})}
    getOption={item=>({value:item.roleId,label:item.displayName,description:item.identification})}
    onChange={(id,option)=>onChange(id,id==="none"?"":option.label)}
    ariaLabel="Transportador del despacho" placeholder="Buscar transportador"/>;
}
