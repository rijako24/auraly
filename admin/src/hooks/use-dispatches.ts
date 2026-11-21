import { useEffect } from "react";
import { useMutation, useQuery, useQueryClient, type QueryClient } from "@tanstack/react-query";
import { dispatchesApi, type DispatchExecution, type DispatchPage, type DispatchDetail, type DispatchDetailQuery, type DispatchMutation } from "@/services/api/dispatches";
import { DISPATCH_SYNC_COMPLETED_EVENT } from "@/lib/dispatch-offline-store";
import { useAuthStore } from "@/stores/auth-store";
import { useTenantContextStore } from "@/stores/tenant-context-store";
import { useBusinessContextStore } from "@/stores/business-context-store";

export function useDispatchKey() {
  const tenant = useTenantContextStore(state => state.selectedTenantId);
  const business = useBusinessContextStore(state => state.selectedBusinessId);
  const user = useAuthStore(state => state.user?.userId);
  return ["dispatches", tenant, business, user] as const;
}

export function updateDispatchExecution(cache: QueryClient, prefix: readonly unknown[], updated: DispatchExecution) {
  cache.setQueryData([...prefix,"execution",updated.dispatchId],updated);
  cache.setQueryData<DispatchPage>([...prefix,"my-deliveries"],current=>current?{...current,items:current.items.map(item=>
    item.dispatchId===updated.dispatchId?{...item,status:updated.status,rowVersion:updated.rowVersion??item.rowVersion}:item)}:current);
}

export function useDispatchOutboxSynchronization() {
  const cache=useQueryClient(),prefix=useDispatchKey();
  const [,tenant,business,user]=prefix;
  useEffect(()=>{
    const receive=(event:Event)=>{
      const result=(event as CustomEvent<{scope:string;execution:DispatchExecution}>).detail;
      if(result.scope!==`${tenant}:${business}:${user}`)return;
      updateDispatchExecution(cache,["dispatches",tenant,business,user],result.execution);
    };
    window.addEventListener(DISPATCH_SYNC_COMPLETED_EVENT,receive);
    return()=>window.removeEventListener(DISPATCH_SYNC_COMPLETED_EVENT,receive);
  },[cache,tenant,business,user]);
}

export function useDispatches(params: {page:number;pageSize:number;search?:string;status?:string;from?:string;to?:string;orderBy?:string}, enabled = true) {
  const key = useDispatchKey();
  return useQuery({ queryKey: [...key, "page", params], queryFn: () => dispatchesApi.page(params), enabled: enabled && Boolean(key[1] && key[2] && key[3]) });
}
export function useDispatchCandidates(params: {page:number;pageSize:number;search?:string;documentType?:string;warehouseId?:string;from:string;to:string;dateRangeVersion:2;routeId?:string}, enabled = true) {
  const key = useDispatchKey();
  return useQuery({ queryKey: [...key, "candidates", params], queryFn: () => dispatchesApi.candidates(params), enabled: enabled && Boolean(key[1] && key[2] && key[3]) });
}
export function useDispatchDetail(id: string|undefined, enabled: boolean, params:DispatchDetailQuery) {
  const key = useDispatchKey();
  return useQuery({ queryKey: [...key, "detail", id, params], queryFn: () => dispatchesApi.detail(id!,params), enabled: enabled && Boolean(id && key[1] && key[2] && key[3]) });
}
export function useCreateDispatch() {
  const cache = useQueryClient(), key = useDispatchKey();
  return useMutation({ mutationFn: dispatchesApi.create, onSuccess: () => cache.invalidateQueries({queryKey:key,refetchType:"none"}) });
}
export function useDispatchTransition() {
  const cache = useQueryClient(), key = useDispatchKey();
  return useMutation({ mutationFn: ({id,action,rowVersion}: {id:string;action:string;rowVersion:string}) => dispatchesApi.transition(id,action,rowVersion), onSuccess: result => applyDispatchMutation(cache,key,result) });
}
function applyDispatchMutation(cache:QueryClient,key:readonly unknown[],result:DispatchMutation) {
  cache.setQueriesData<DispatchDetail>({queryKey:[...key,"detail",result.dispatchId]},current=>current?{
    ...current,status:result.status,rowVersion:result.rowVersion,summary:result,
    lines:current.lines.map(line=>result.updatedLine&&line.dispatchLineId===result.updatedLine.dispatchLineId?result.updatedLine:line),
  }:current);
  void cache.invalidateQueries({queryKey:[...key,"page"],refetchType:"none"});
}
export function useVerifyDispatch() {
  const cache = useQueryClient(), key = useDispatchKey();
  return useMutation({ mutationFn: ({id,lineId,quantity}: {id:string;lineId:string;quantity:number}) => dispatchesApi.verify(id,lineId,quantity,null), onSuccess: result => applyDispatchMutation(cache,key,result) });
}
export function useDeclareDispatchShortage() {
  const cache = useQueryClient(), key = useDispatchKey();
  return useMutation({ mutationFn: ({id,...request}: {id:string;dispatchLineId:string;quantity:number;reason:string;notes:string|null;rowVersion:string}) => dispatchesApi.shortage(id,request), onSuccess: result => applyDispatchMutation(cache,key,result) });
}
