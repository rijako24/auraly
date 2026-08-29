"use client";

import { useQuery } from "@tanstack/react-query";
import { PagedEntitySelect } from "@/components/forms/paged-entity-select";
import { dispatchesApi } from "@/services/api/dispatches";

export function DispatchDriverSelect({value,onChange}:{value:string;onChange:(value:string,name:string)=>void}) {
  const selected=useQuery({queryKey:["dispatch-driver-value",value],queryFn:async()=>
    (await dispatchesApi.drivers({page:1,pageSize:1,userId:value})).items[0]??null,enabled:Boolean(value&&value!=="none")});
  return <PagedEntitySelect queryKey={["dispatch-driver-select"]} value={value}
    leadingOptions={[{value:"none",label:"Selecciona un transportador"}]}
    selectedOption={selected.data?{value:selected.data.userId,label:selected.data.name}:null}
    loadPage={(search,page,pageSize)=>dispatchesApi.drivers({page,pageSize,search:search||undefined})}
    getOption={item=>({value:item.userId,label:item.name})}
    onChange={(id,option)=>onChange(id,id==="none"?"":option.label)}
    placeholder="Buscar transportador"/>;
}
