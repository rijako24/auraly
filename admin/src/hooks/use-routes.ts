import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { routesApi, type RouteWrite } from "@/services/api/routes";

const key = ["sales-routes"] as const;

export function useRoutes(params:{page:number;pageSize:number;search?:string;sellerId?:string;zoneId?:string;dayOfWeek?:number;isActive?:boolean;preparationStatus?:string}){
  return useQuery({queryKey:[...key,"page",params],queryFn:()=>routesApi.page(params)});
}
export function useRouteOptions(){return useQuery({queryKey:[...key,"options"],queryFn:routesApi.options});}
export function useRouteDetail(routeId?:string){return useQuery({queryKey:[...key,"detail",routeId],queryFn:()=>routesApi.detail(routeId!),enabled:!!routeId});}
export function useRouteCandidates(routeId:string|undefined,search:string,page:number){return useQuery({queryKey:[...key,"candidates",routeId,search,page],queryFn:()=>routesApi.candidates(routeId!,{search:search||undefined,page,pageSize:50}),enabled:!!routeId});}

export function useCreateRoute(){const cache=useQueryClient();return useMutation({mutationFn:(request:RouteWrite)=>routesApi.create(request),onSuccess:()=>cache.invalidateQueries({queryKey:key})});}
export function useUpdateRoute(){const cache=useQueryClient();return useMutation({mutationFn:({routeId,request}:{routeId:string;request:Omit<RouteWrite,"businessId">&{rowVersion:string}})=>routesApi.update(routeId,request),onSuccess:(_,value)=>{cache.invalidateQueries({queryKey:key});cache.invalidateQueries({queryKey:[...key,"detail",value.routeId]});}});}
export function useCreateRouteZone(){const cache=useQueryClient();return useMutation({mutationFn:(request:{businessId:string;code:string;name:string})=>routesApi.createZone(request),onSuccess:()=>cache.invalidateQueries({queryKey:[...key,"options"]})});}
export function useSetRouteStatus(){const cache=useQueryClient();return useMutation({mutationFn:({routeId,isActive,rowVersion}:{routeId:string;isActive:boolean;rowVersion:string})=>routesApi.setStatus(routeId,isActive,rowVersion),onSuccess:(_,value)=>{cache.invalidateQueries({queryKey:key});cache.invalidateQueries({queryKey:[...key,"detail",value.routeId]});}});}
export function useAddRouteStops(){const cache=useQueryClient();return useMutation({mutationFn:({routeId,stops,rowVersion}:{routeId:string;stops:Array<{customerId:string;partySiteId:string;visitNote:string|null}>;rowVersion:string})=>routesApi.addStops(routeId,stops,rowVersion),onSuccess:(_,value)=>{cache.invalidateQueries({queryKey:key});cache.invalidateQueries({queryKey:[...key,"detail",value.routeId]});cache.invalidateQueries({queryKey:[...key,"candidates"]});}});}
export function useRemoveRouteStop(){const cache=useQueryClient();return useMutation({mutationFn:({routeId,stopId,rowVersion}:{routeId:string;stopId:string;rowVersion:string})=>routesApi.removeStop(routeId,stopId,rowVersion),onSuccess:(_,value)=>{cache.invalidateQueries({queryKey:key});cache.invalidateQueries({queryKey:[...key,"detail",value.routeId]});}});}
export function useReorderRouteStops(){const cache=useQueryClient();return useMutation({mutationFn:({routeId,orderedStopIds,rowVersion}:{routeId:string;orderedStopIds:string[];rowVersion:string})=>routesApi.reorder(routeId,orderedStopIds,rowVersion),onSuccess:(_,value)=>{cache.invalidateQueries({queryKey:key});cache.invalidateQueries({queryKey:[...key,"detail",value.routeId]});}});}
