"use client";
import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { partiesApi, type CommercialPartyRole, type CreateThirdPartyRequest } from "@/services/api/parties";
import { useBusinessContextStore } from "@/stores/business-context-store";

export function useParties(params: { page: number; pageSize: number; search?: string; role?: string; isActive?: boolean; isIncomplete?: boolean }) {
  const businessId=useBusinessContextStore((state)=>state.selectedBusinessId);
  return useQuery({ queryKey:["parties",businessId,params],queryFn:()=>partiesApi.page(params),enabled:!!businessId,placeholderData:keepPreviousData });
}
export function useCustomerMap(){
  const businessId=useBusinessContextStore((state)=>state.selectedBusinessId);
  return useQuery({queryKey:["parties","customer-map",businessId],queryFn:()=>partiesApi.customerMap(),enabled:!!businessId,staleTime:60_000});
}
export function useCreateThirdParty(role:CommercialPartyRole) {
  const client=useQueryClient(); const businessId=useBusinessContextStore((state)=>state.selectedBusinessId);
  return useMutation({mutationFn:(request:CreateThirdPartyRequest)=>role==="Customer"?partiesApi.createCustomer(request):role==="Supplier"?partiesApi.createSupplier(request):role==="Seller"?partiesApi.createSeller(request):partiesApi.createCarrier(request),onSuccess:()=>client.invalidateQueries({queryKey:["parties",businessId]})});
}
export function useAddPartySite(partyId:string){const client=useQueryClient();const businessId=useBusinessContextStore((state)=>state.selectedBusinessId);return useMutation({mutationFn:({customerId,request}:{customerId:string;request:Parameters<typeof partiesApi.addSite>[1]})=>partiesApi.addSite(customerId,request),onSuccess:async()=>{await Promise.all([client.invalidateQueries({queryKey:["parties","detail",businessId,partyId]}),client.invalidateQueries({queryKey:["parties",businessId]})])}});}
export function useUpdatePartySite(partyId:string){const client=useQueryClient();const businessId=useBusinessContextStore((state)=>state.selectedBusinessId);return useMutation({mutationFn:({customerId,siteId,request}:{customerId:string;siteId:string;request:Parameters<typeof partiesApi.updateSite>[2]})=>partiesApi.updateSite(customerId,siteId,request),onSuccess:async()=>{await Promise.all([client.invalidateQueries({queryKey:["parties","detail",businessId,partyId]}),client.invalidateQueries({queryKey:["parties",businessId]}),client.invalidateQueries({queryKey:["parties","customer-map",businessId]})])}});}
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
  params: { countryId: string; identificationTypeCode: string; identification: string; requestedRole: import("@/services/api/parties").PartyRole },
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
