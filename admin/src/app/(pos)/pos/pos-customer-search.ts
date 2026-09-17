import type { PosCustomer, PosCustomerSearchPage } from "@/services/pos/pos-edge-client";

export type SelectableCustomer = PosCustomer & { partySiteId: string };

export function selectableCustomerPage(page: PosCustomerSearchPage): {
  items: SelectableCustomer[];
  omittedWithoutSite: number;
  hasMore: boolean;
  nextOffset: number | null;
} {
  const items = page.items.filter(
    (customer): customer is SelectableCustomer => Boolean(customer.partySiteId),
  );
  return {
    ...page,
    items,
    omittedWithoutSite: page.items.length - items.length,
  };
}
