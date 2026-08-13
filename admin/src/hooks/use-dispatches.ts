import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { dispatchesApi } from "@/services/api/dispatches";

const key=["dispatches"] as const;
export const useDispatches=(params:{page:number;pageSize:number;search?:string;status?:string;from?:string;to?:string})=>useQuery({queryKey:[...key,"page",params],queryFn:()=>dispatchesApi.page(params)});
export const useDispatchOptions=()=>useQuery({queryKey:[...key,"options"],queryFn:dispatchesApi.options});
export const useDispatchCandidates=(params:{search?:string;documentType?:string;warehouseId?:string},enabled=true)=>useQuery({queryKey:[...key,"candidates",params],queryFn:()=>dispatchesApi.candidates({...params,page:1,pageSize:100}),enabled});
export const useDispatchDetail=(id?:string)=>useQuery({queryKey:[...key,"detail",id],queryFn:()=>dispatchesApi.detail(id!),enabled:!!id});
export function useCreateDispatch(){const cache=useQueryClient();return useMutation({mutationFn:dispatchesApi.create,onSuccess:()=>cache.invalidateQueries({queryKey:key})});}
export function useDispatchTransition(){const cache=useQueryClient();return useMutation({mutationFn:({id,action,rowVersion}:{id:string;action:string;rowVersion:string})=>dispatchesApi.transition(id,action,rowVersion),onSuccess:(_,value)=>{cache.invalidateQueries({queryKey:key});cache.invalidateQueries({queryKey:[...key,"detail",value.id]});}});}
export function useVerifyDispatch(){const cache=useQueryClient();return useMutation({mutationFn:({id,lineId,quantity}:{id:string;lineId:string;quantity:number})=>dispatchesApi.verify(id,lineId,quantity,null),onSuccess:(_,value)=>{cache.invalidateQueries({queryKey:key});cache.invalidateQueries({queryKey:[...key,"detail",value.id]});}});}
export function useDeclareDispatchShortage(){const cache=useQueryClient();return useMutation({mutationFn:({id,...request}:{id:string;dispatchLineId:string;quantity:number;reason:string;notes:string|null;rowVersion:string})=>dispatchesApi.shortage(id,request),onSuccess:(_,value)=>{cache.invalidateQueries({queryKey:key});cache.invalidateQueries({queryKey:[...key,"detail",value.id]});}});}
