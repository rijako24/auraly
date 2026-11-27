import { apiClient } from "./client";

export type PriceChannelStrategy = "TieredProductPrice"|"PercentageOverBasePrice"|"MarginOverLatestCost"|"FixedMarginOverAverageCost"|"SellAtAverageCost"|"ProductMarginAdjustment";
export interface PriceSegmentSummary { id:string; code:string; name:string; isActive:boolean; createdAt:string; productCount:number; customerCount:number; strategy:PriceChannelStrategy; value:number|null; }
export interface PriceChannelItemSyncSelection { productId:string; minimumQuantity:number; autoUpdatePrice:boolean; }
export interface PriceSegmentItem { productId:string; productCode:string; productName:string; amount:number; currencyCode:string; minimumQuantity:number; targetMarginPercent:number|null; autoUpdatePrice:boolean; costBasisAmount:number|null; salesTaxRate:number; }
export type PriceSegmentTierInput = Pick<PriceSegmentItem, "productId" | "amount" | "minimumQuantity" | "targetMarginPercent" | "autoUpdatePrice"> & { inputMode: "Margin" | "SalePrice" };
export type PriceChannelExclusionScope = "Category"|"Brand"|"Product";
export interface PriceChannelExclusion { exclusionId:string; scopeType:PriceChannelExclusionScope; scopeId:string; scopeName:string; categoryDepth:number|null; productCode:string|null; }
export interface PriceChannelProductReportRow { productId:string; productCode:string; productName:string; minimumQuantity:number; publicAmount:number; channelAmount:number; currencyCode:string; priceSource:string; }
export interface PriceChannelProductReport { priceChannelId:string; code:string; name:string; strategy:PriceChannelStrategy; isActive:boolean; items:PriceChannelProductReportRow[]; }

export const priceSegmentsApi = {
  list: () => apiClient.get<PriceSegmentSummary[]>("/commerce/v1/pricing/segments"),
  create: (data:{name:string;channelStrategy:PriceChannelStrategy;channelValue?:number|null;items?:Array<{productId:string;amount:number;minimumQuantity:number;targetMarginPercent:number|null;autoUpdatePrice:boolean;inputMode:"Margin"|"SalePrice"}>;exclusions?:Array<{scopeType:PriceChannelExclusionScope;scopeId:string}>}) => apiClient.post<PriceSegmentSummary>("/commerce/v1/pricing/segments",data),
  items: (id:string) => apiClient.get<PriceSegmentItem[]>(`/commerce/v1/pricing/segments/${id}/items`),
  productCost: (productId:string) => apiClient.get<{costBasisAmount:number;salesTaxRate:number;currentMarginPercent:number|null}>(`/commerce/v1/pricing/segments/products/${productId}/cost`),
  productPriceReport: (id:string) => apiClient.get<PriceChannelProductReport>(`/commerce/v1/pricing/segments/${id}/product-price-report`),
  saveChannelSettings: (id:string,name:string,channelStrategy:PriceChannelStrategy,channelValue:number|null,autoUpdatePrice?:boolean|null,itemSync?:PriceChannelItemSyncSelection[],newItems?:PriceSegmentTierInput[],retiredItems?:Pick<PriceSegmentItem,"productId"|"minimumQuantity">[]) => apiClient.put<{
    autoUpdatePrice:boolean|null;itemSync:PriceChannelItemSyncSelection[];
    addedItems?: Omit<PriceSegmentItem, "productCode" | "productName" | "currencyCode">[];productCount?:number|null;
  }|undefined>(`/commerce/v1/pricing/segments/${id}/settings`,{name,channelStrategy,channelValue,autoUpdatePrice,itemSync,newItems,retiredItems}),
  exclusions: (id:string) => apiClient.get<PriceChannelExclusion[]>(`/commerce/v1/pricing/segments/${id}/exclusions`),
  saveExclusion: (id:string,scopeType:PriceChannelExclusionScope,scopeId:string) => apiClient.post<{exclusionId:string}>(`/commerce/v1/pricing/segments/${id}/exclusions`,{scopeType,scopeId}),
  deleteExclusion: (id:string,exclusionId:string) => apiClient.delete<void>(`/commerce/v1/pricing/segments/${id}/exclusions/${exclusionId}`),
};
