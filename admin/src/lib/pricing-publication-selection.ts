import type {
  PricePublicationFilter,
  PriceRevisionListItem,
  PublishPricesRequest,
} from "@/services/api/pricing";

export function buildPricePublicationRequest(
  rows: PriceRevisionListItem[],
  allMatching: boolean,
  filter: PricePublicationFilter,
): PublishPricesRequest {
  if (allMatching) return { allMatching: filter };
  return { productIds: [...new Set(rows.map((row) => row.productId))] };
}
