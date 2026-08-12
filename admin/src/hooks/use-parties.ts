"use client";
import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { partiesApi, type CreateThirdPartyRequest } from "@/services/api/parties";
import { useBusinessContextStore } from "@/stores/business-context-store";

export function useParties(params: { page: number; pageSize: number; search?: string; role?: string; isActive?: boolean; isIncomplete?: boolean }) {
  const businessId=useBusinessContextStore((state)=>state.selectedBusinessId);
  return useQuery({ queryKey:["parties",businessId,params],queryFn:()=>partiesApi.page(params),enabled:!!businessId,placeholderData:keepPreviousData });
}
export function useCreateThirdParty(role:"Customer"|"Supplier"|"Seller"|"Carrier") {
  const client=useQueryClient(); const businessId=useBusinessContextStore((state)=>state.selectedBusinessId);
  return useMutation({mutationFn:(request:CreateThirdPartyRequest)=>role==="Customer"?partiesApi.createCustomer(request):role==="Supplier"?partiesApi.createSupplier(request):role==="Seller"?partiesApi.createSeller(request):partiesApi.createCarrier(request),onSuccess:()=>client.invalidateQueries({queryKey:["parties",businessId]})});
}
export function useUpdateParty(){const client=useQueryClient();const businessId=useBusinessContextStore((state)=>state.selectedBusinessId);return useMutation({mutationFn:({partyId,request}:{partyId:string;request:Parameters<typeof partiesApi.update>[1]})=>partiesApi.update(partyId,request),onSuccess:()=>client.invalidateQueries({queryKey:["parties",businessId]})});}
export function useSetPartyStatus(){const client=useQueryClient();const businessId=useBusinessContextStore((state)=>state.selectedBusinessId);return useMutation({mutationFn:({partyId,isActive,rowVersion}:{partyId:string;isActive:boolean;rowVersion:string})=>partiesApi.setStatus(partyId,isActive,rowVersion),onSuccess:()=>client.invalidateQueries({queryKey:["parties",businessId]})});}
export function useCountries(includeInactive=false){return useQuery({
  queryKey:["geography","countries",includeInactive],
  queryFn:()=>partiesApi.countries(includeInactive),
  select:(countries)=>countries.filter((country,index,items)=>
    items.findIndex((candidate)=>
      candidate.name.trim().localeCompare(country.name.trim(),undefined,{sensitivity:"base"})===0)===index),
});}
export function useDivisions(countryId:string){return useQuery({queryKey:["geography","divisions",countryId],queryFn:()=>partiesApi.divisions(countryId),enabled:!!countryId});}
export function useCities(divisionId:string){return useQuery({queryKey:["geography","cities",divisionId],queryFn:()=>partiesApi.cities(divisionId),enabled:!!divisionId});}

export function useCustomerPricingOptions(enabled=true){return useQuery({queryKey:["parties","pricing-options"],queryFn:()=>partiesApi.pricingOptions(),enabled});}

export function usePartyIdentity(
  params: { countryId: string; identificationTypeCode: string; identification: string; requestedRole: "Customer"|"Supplier"|"Seller"|"Carrier" },
  enabled: boolean,
) {
  const businessId=useBusinessContextStore((state)=>state.selectedBusinessId);
  return useQuery({
    queryKey:["parties","identity",businessId,params],
    queryFn:()=>partiesApi.identity(params),
    enabled:enabled&&!!businessId,
    staleTime:30_000,
    retry:false,
  });
}

export function usePartyDetail(partyId?:string) {
  const businessId=useBusinessContextStore((state)=>state.selectedBusinessId);
  return useQuery({
    queryKey:["parties","detail",businessId,partyId],
    queryFn:()=>partiesApi.detail(partyId!),
    enabled:!!businessId&&!!partyId,
  });
}