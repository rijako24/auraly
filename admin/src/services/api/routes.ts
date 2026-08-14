import { apiClient, withPagedDefaults } from "./client";

export interface SalesZoneItem { zoneId:string; code:string; name:string; isActive:boolean; rowVersion:string; }
export interface RouteSellerOption { sellerId:string; code:string; name:string; }
export interface RouteOptions { sellers:RouteSellerOption[]; zones:SalesZoneItem[]; }
export interface RouteScheduleInput { dayOfWeek:number; runOrder:number; plannedStartTime:string|null; }
export interface SalesRouteListItem {
  routeId:string; code:string; name:string; sellerId:string; sellerName:string;
  zoneId:string|null; zoneName:string|null; isActive:boolean; preparationStatus:"Draft"|"Ready"|"AttentionRequired";
  stopCount:number; days:number[]; updatedAt:string; rowVersion:string;
}
export interface SalesRoutePage { items:SalesRouteListItem[]; page:number; pageSize:number; totalCount:number; totalPages:number; }
export interface SalesRouteSchedule { routeScheduleId:string; dayOfWeek:number; runOrder:number; plannedStartTime:string|null; }
export interface SalesRouteStop {
  routeStopId:string; customerId:string; partySiteId:string; sequence:number; customerName:string;
  identification:string|null; siteName:string; addressLine:string; neighborhood:string|null;
  cityName:string; phone:string|null; googleMapsUrl:string|null; latitude:number|null; longitude:number|null;
  plannedVisitTime:string|null; visitNote:string|null; rowVersion:string;
}
export interface SalesRouteDetail {
  routeId:string; businessId:string; code:string; name:string; sellerId:string; sellerName:string;
  zoneId:string|null; zoneName:string|null; notes:string|null; isActive:boolean;
  preparationStatus:"Draft"|"Ready"|"AttentionRequired"; schedules:SalesRouteSchedule[];
  stops:SalesRouteStop[]; createdAt:string; updatedAt:string; rowVersion:string;
}
export interface SalesRouteVisit {
  routeVisitId:string; routeStopId:string; visitDate:string; status:"Visited"|"Skipped";
  skipReason:string|null; visitObservation:string|null; orderId:string|null; occurredAt:string; recordedBy:string;
}
export interface RecordSalesRouteVisit { routeStopId:string; visitDate:string; status:"Visited"|"Skipped"; skipReason:string|null; visitObservation?:string|null; orderId:string|null; occurredAt:string; idempotencyKey:string; }
export interface RouteCandidateSite {
  customerId:string; partySiteId:string; customerName:string; identification:string|null;
  siteName:string; addressLine:string; neighborhood:string|null; cityName:string; phone:string|null;
  googleMapsUrl:string|null; latitude:number|null; longitude:number|null;
  isAlreadyInRoute:boolean; hasScheduleConflict:boolean; conflictDescription:string|null;
}
export interface RouteCandidatePage { items:RouteCandidateSite[]; page:number; pageSize:number; totalCount:number; totalPages:number; }
export interface RouteMutationResult { routeId:string; rowVersion:string; isActive:boolean; preparationStatus:string; stopCount:number; }
export interface RouteWrite {
  businessId:string; code:string; name:string; sellerId:string; zoneId:string|null;
  notes:string|null; schedules:RouteScheduleInput[];
}

export const routesApi = {
  page: (params:{page?:number;pageSize?:number;search?:string;sellerId?:string;zoneId?:string;dayOfWeek?:number;isActive?:boolean;preparationStatus?:string}) =>
    apiClient.get<SalesRoutePage>("/commerce/v1/routes",withPagedDefaults(params)),
  detail: (routeId:string) => apiClient.get<SalesRouteDetail>(`/commerce/v1/routes/${routeId}`),
  export: (routeId:string) => apiClient.get<SalesRouteDetail>(`/commerce/v1/routes/${routeId}/export`),
  visits: (routeId:string,date:string) => apiClient.get<SalesRouteVisit[]>(`/commerce/v1/routes/${routeId}/visits?date=${encodeURIComponent(date)}`),
  recordVisit: (routeId:string,request:RecordSalesRouteVisit) =>
    apiClient.put<SalesRouteVisit>(`/commerce/v1/routes/${routeId}/visits`,request),
  options: () => apiClient.get<RouteOptions>("/commerce/v1/routes/options"),
  candidates: (routeId:string,params:{page?:number;pageSize?:number;search?:string;countryId?:string;administrativeDivisionId?:string;cityId?:string;neighborhood?:string}) =>
    apiClient.get<RouteCandidatePage>(`/commerce/v1/routes/${routeId}/candidate-sites`,withPagedDefaults(params)),
  createZone: (request:{businessId:string;code:string;name:string}) => apiClient.post<SalesZoneItem>("/commerce/v1/route-zones",request),
  create: (request:RouteWrite) => apiClient.post<RouteMutationResult>("/commerce/v1/routes",request),
  update: (routeId:string,request:Omit<RouteWrite,"businessId">&{rowVersion:string}) => apiClient.put<RouteMutationResult>(`/commerce/v1/routes/${routeId}`,request),
  setStatus: (routeId:string,isActive:boolean,rowVersion:string) => apiClient.post<RouteMutationResult>(`/commerce/v1/routes/${routeId}/status`,{isActive,rowVersion}),
  addStops: (routeId:string,stops:Array<{customerId:string;partySiteId:string;plannedVisitTime:string|null;visitNote:string|null}>,routeRowVersion:string) =>
    apiClient.post<RouteMutationResult>(`/commerce/v1/routes/${routeId}/stops`,{stops,routeRowVersion}),
  updateStop: (routeId:string,stopId:string,request:{plannedVisitTime:string|null;visitNote:string|null;routeRowVersion:string}) => apiClient.put<RouteMutationResult>(`/commerce/v1/routes/${routeId}/stops/${stopId}`,request),
  removeStop: (routeId:string,stopId:string,rowVersion:string) =>
    apiClient.delete<RouteMutationResult>(`/commerce/v1/routes/${routeId}/stops/${stopId}?rowVersion=${encodeURIComponent(rowVersion)}`),
  reorder: (routeId:string,orderedStopIds:string[],routeRowVersion:string) =>
    apiClient.put<RouteMutationResult>(`/commerce/v1/routes/${routeId}/stops/order`,{orderedStopIds,routeRowVersion}),
};
