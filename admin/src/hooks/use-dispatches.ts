import { useEffect } from "react";
import { useMutation, useQuery, useQueryClient, type QueryClient } from "@tanstack/react-query";
import { dispatchesApi, type DispatchExecution, type DispatchPage } from "@/services/api/dispatches";
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

// Commands return a summary, not the full manifest. Reload that one active
// detail; filtered lists/counts are read once when their visible owner resumes.
async function invalidateDispatch(cache: QueryClient, key: readonly unknown[], id?: string) {
  await cache.invalidateQueries({ queryKey: key, refetchType: "none" });
  if (id) await cache.refetchQueries({ queryKey: [...key, "detail", id], exact: true, type: "active" });
}

export function useDispatches(params: {page:number;pageSize:number;search?:string;status?:string;from?:string;to?:string}, enabled = true) {
  const key = useDispatchKey();
  return useQuery({ queryKey: [...key, "page", params], queryFn: () => dispatchesApi.page(params), enabled: enabled && Boolean(key[1] && key[2] && key[3]) });
}
export function useDispatchOptions() {
  const key = useDispatchKey();
  return useQuery({ queryKey: [...key, "options"], queryFn: dispatchesApi.options, enabled: Boolean(key[1] && key[2] && key[3]) });
}
export function useDispatchCandidates(params: {page:number;pageSize:number;search?:string;documentType?:string;warehouseId?:string;from:string;to:string;dateRangeVersion:2}, enabled = true) {
  const key = useDispatchKey();
  return useQuery({ queryKey: [...key, "candidates", params], queryFn: () => dispatchesApi.candidates(params), enabled: enabled && Boolean(key[1] && key[2] && key[3]) });
}
export function useDispatchDetail(id?: string, enabled = true) {
  const key = useDispatchKey();
  return useQuery({ queryKey: [...key, "detail", id], queryFn: () => dispatchesApi.detail(id!), enabled: enabled && Boolean(id && key[1] && key[2] && key[3]) });
}
export function useCreateDispatch() {
  const cache = useQueryClient(), key = useDispatchKey();
  return useMutation({ mutationFn: dispatchesApi.create, onSuccess: () => invalidateDispatch(cache, key) });
}
export function useDispatchTransition() {
  const cache = useQueryClient(), key = useDispatchKey();
  return useMutation({ mutationFn: ({id,action,rowVersion}: {id:string;action:string;rowVersion:string}) => dispatchesApi.transition(id,action,rowVersion), onSuccess: (_,value) => invalidateDispatch(cache,key,value.id) });
}
export function useVerifyDispatch() {
  const cache = useQueryClient(), key = useDispatchKey();
  return useMutation({ mutationFn: ({id,lineId,quantity}: {id:string;lineId:string;quantity:number}) => dispatchesApi.verify(id,lineId,quantity,null), onSuccess: (_,value) => invalidateDispatch(cache,key,value.id) });
}
export function useDeclareDispatchShortage() {
  const cache = useQueryClient(), key = useDispatchKey();
  return useMutation({ mutationFn: ({id,...request}: {id:string;dispatchLineId:string;quantity:number;reason:string;notes:string|null;rowVersion:string}) => dispatchesApi.shortage(id,request), onSuccess: (_,value) => invalidateDispatch(cache,key,value.id) });
}
