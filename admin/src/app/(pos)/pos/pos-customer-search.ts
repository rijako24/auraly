import type { PosCustomer } from "@/services/pos/pos-edge-client";

export type PosCustomerSearchOption = Pick<PosCustomer,
  "customerId" | "partySiteId" | "siteName" | "siteAddress" | "name" | "identification">;
export type PosCustomerSearchResults = {
  items: PosCustomerSearchOption[]; hasMore: boolean; nextOffset: number | null;
};

export function selectableCustomerPage<T extends PosCustomerSearchOption>(page: {
  items: T[]; hasMore: boolean; nextOffset: number | null;
}, requireSite = true): {
  items: T[];
  omittedWithoutSite: number;
  hasMore: boolean;
  nextOffset: number | null;
} {
  const items = page.items.filter(
    (customer) => !requireSite || Boolean(customer.partySiteId),
  );
  return {
    ...page,
    items,
    omittedWithoutSite: page.items.length - items.length,
  };
}
