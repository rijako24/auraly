import assert from "node:assert/strict";
import test from "node:test";

import { selectableCustomerPage } from "./pos-customer-search";

test("keeps valid synchronized customer sites when a legacy customer has no site", () => {
  const page = selectableCustomerPage({
    items: [
      {
        customerId: "customer-without-site",
        identification: "100",
        name: "Cliente antiguo",
        priceChannelId: null,
        requiresElectronicInvoice: false,
        isActive: true,
      },
      {
        customerId: "customer-with-site",
        partySiteId: "site-1",
        identification: "200",
        name: "Cliente disponible",
        priceChannelId: null,
        requiresElectronicInvoice: false,
        isActive: true,
      },
    ],
    hasMore: true,
    nextOffset: 2,
  });

  assert.equal(page.omittedWithoutSite, 1);
  assert.deepEqual(page.items.map((item) => item.partySiteId), ["site-1"]);
  assert.equal(page.hasMore, true);
  assert.equal(page.nextOffset, 2);
});
