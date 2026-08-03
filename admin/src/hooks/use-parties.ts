"use client";
import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { partiesApi, type CreateThirdPartyRequest } from "@/services/api/parties";
import { useBusinessContextStore } from "@/stores/business-context-store";

export function useParties(params: { page: number; pageSize: number; search?: string; role?: string; isActive?: boolean; isIncomplete?: boolean }) {
  const businessId=useBusinessContextStore((state)=>state.selectedBusinessId);
  return useQuery({ queryKey:["parties",businessId,params],queryFn:()=>partiesApi.page(params),enabled:!!businessId,placeholderData:keepPreviousData });
}
export function useCreateThirdParty(role:"Customer"|"Supplier") {
  const client=useQueryClient(); const businessId=useBusinessContextStore((state)=>state.selectedBusinessId);
  return useMutation({mutationFn:(request:CreateThirdPartyRequest)=>role==="Customer"?partiesApi.createCustomer(request):partiesApi.createSupplier(request),onSuccess:()=>client.invalidateQueries({queryKey:["parties",businessId]})});
}
export function useUpdateParty(){const client=useQueryClient();const businessId=useBusinessContextStore((state)=>state.selectedBusinessId);return useMutation({mutationFn:({partyId,request}:{partyId:string;request:Parameters<typeof partiesApi.update>[1]})=>partiesApi.update(partyId,request),onSuccess:()=>client.invalidateQueries({queryKey:["parties",businessId]})});}
export function useSetPartyStatus(){const client=useQueryClient();const businessId=useBusinessContextStore((state)=>state.selectedBusinessId);return useMutation({mutationFn:({partyId,isActive,rowVersion}:{partyId:string;isActive:boolean;rowVersion:string})=>partiesApi.setStatus(partyId,isActive,rowVersion),onSuccess:()=>client.invalidateQueries({queryKey:["parties",businessId]})});}
export function useCountries(includeInactive=false){return useQuery({queryKey:["geography","countries",includeInactive],queryFn:()=>partiesApi.countries(includeInactive)});}
export function useDivisions(countryId:string){return useQuery({queryKey:["geography","divisions",countryId],queryFn:()=>partiesApi.divisions(countryId),enabled:!!countryId});}
export function useCities(divisionId:string){return useQuery({queryKey:["geography","cities",divisionId],queryFn:()=>partiesApi.cities(divisionId),enabled:!!divisionId});}
