import type {
  CommerceOrderFilters,
  CommerceOrderListItem,
  CommerceOrderPage,
} from "./commerce-orders-client";

export async function loadAllMatchingOrders(
  loadPage: (
    filters: CommerceOrderFilters & { page: number; pageSize: number },
  ) => Promise<CommerceOrderPage>,
  filters: Omit<CommerceOrderFilters, "page" | "pageSize">,
  pageSize = 100,
): Promise<CommerceOrderListItem[]> {
  const orders = new Map<string, CommerceOrderListItem>();
  let page = 1;

  while (true) {
    const result = await loadPage({ ...filters, page, pageSize });
    const countBefore = orders.size;
    for (const order of result.items) orders.set(order.orderId, order);
    if (!result.hasMore) return [...orders.values()];
    if (result.items.length === 0 || orders.size === countBefore)
      throw new Error("La consulta de pedidos no avanzó a la siguiente página.");
    page += 1;
  }
}
