import { apiClient } from "./client";

export type PriceChannelStrategy = "TieredProductPrice"|"PercentageOverBasePrice"|"PercentageOverAverageCost"|"FixedMarginOverAverageCost"|"SellAtAverageCost"|"ProductMarginAdjustment";
export interface PriceSegmentSummary { id:string; code:string; name:string; isActive:boolean; createdAt:string; productCount:number; customerCount:number; strategy:PriceChannelStrategy; value:number|null; }
export interface PriceSegmentItem { productId:string; productCode:string; productName:string; amount:number; currencyCode:string; minimumQuantity:number; }
export type PriceChannelExclusionScope = "Category"|"Brand"|"Product";
export interface PriceChannelExclusion { exclusionId:string; scopeType:PriceChannelExclusionScope; scopeId:string; scopeName:string; categoryDepth:number|null; productCode:string|null; }

export const priceSegmentsApi = {
  list: () => apiClient.get<PriceSegmentSummary[]>("/commerce/v1/pricing/segments"),
  create: (data:{name:string;channelStrategy:PriceChannelStrategy;channelValue?:number|null;items?:Array<{productId:string;amount:number;minimumQuantity:number}>;exclusions?:Array<{scopeType:PriceChannelExclusionScope;scopeId:string}>}) => apiClient.post<PriceSegmentSummary>("/commerce/v1/pricing/segments",data),
  items: (id:string) => apiClient.get<PriceSegmentItem[]>(`/commerce/v1/pricing/segments/${id}/items`),
  saveItem: (id:string,productId:string,data:{amount:number;minimumQuantity:number}) => apiClient.put<void>(`/commerce/v1/pricing/segments/${id}/items/${productId}`,data),
  deleteItem: (id:string,productId:string,minimumQuantity:number) => apiClient.delete<void>(`/commerce/v1/pricing/segments/${id}/items/${productId}?minimumQuantity=${encodeURIComponent(minimumQuantity)}`),
  saveChannelSettings: (id:string,name:string,channelStrategy:PriceChannelStrategy,channelValue:number|null) => apiClient.put<void>(`/commerce/v1/pricing/segments/${id}/settings`,{name,channelStrategy,channelValue}),
  exclusions: (id:string) => apiClient.get<PriceChannelExclusion[]>(`/commerce/v1/pricing/segments/${id}/exclusions`),
  saveExclusion: (id:string,scopeType:PriceChannelExclusionScope,scopeId:string) => apiClient.post<{exclusionId:string}>(`/commerce/v1/pricing/segments/${id}/exclusions`,{scopeType,scopeId}),
  deleteExclusion: (id:string,exclusionId:string) => apiClient.delete<void>(`/commerce/v1/pricing/segments/${id}/exclusions/${exclusionId}`),
};
