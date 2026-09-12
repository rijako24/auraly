import assert from "node:assert/strict";
import test from "node:test";

import { editableOrderAvailableQuantity, editableOrderInitialState, evaluateOrderReviewQuantity, isOrderReviewLinePending, sellerOrderMaximumQuantity, sellerOrderSubmitDisabled } from "./order-review";

test("a review quantity becomes valid when it is reduced to current stock", () => {
  assert.deepEqual(evaluateOrderReviewQuantity("7", true, 7, 0), {
    quantity: 7,
    additionalQuantity: 7,
    validNumber: true,
    insufficient: false,
  });
  assert.equal(evaluateOrderReviewQuantity("8", true, 7, 0).insufficient, true);
});

test("stock already reserved is not requested from the sales warehouse again", () => {
  assert.deepEqual(evaluateOrderReviewQuantity("5", true, 0, 5), {
    quantity: 5,
    additionalQuantity: 0,
    validNumber: true,
    insufficient: false,
  });
  assert.equal(evaluateOrderReviewQuantity("7", true, 2, 5).insufficient, false);
  assert.equal(evaluateOrderReviewQuantity("8", true, 2, 5).insufficient, true);
});

test("products without inventory control only require a positive quantity", () => {
  assert.equal(evaluateOrderReviewQuantity("500", false, 0, 0).insufficient, false);
  assert.equal(evaluateOrderReviewQuantity("0", false, 0, 0).validNumber, false);
});

test("only inventory lines not yet fully reserved are pending review", () => {
  assert.equal(isOrderReviewLinePending(true, 10, 0), true);
  assert.equal(isOrderReviewLinePending(true, 5, 5), false);
  assert.equal(isOrderReviewLinePending(false, 10, 0), false);
});

test("complete editing can reuse reserved stock plus current sales stock", () => {
  assert.equal(editableOrderAvailableQuantity(2, 5), 7);
});

test("an existing shortage can be resubmitted but cannot be increased", () => {
  const maximum = sellerOrderMaximumQuantity(true, 3, 10, false);
  assert.equal(maximum, 10);
  assert.equal(sellerOrderSubmitDisabled(false, 2, true, true, 10 > maximum), false);
  assert.equal(sellerOrderSubmitDisabled(false, 2, true, true, 11 > maximum), true);
  assert.equal(sellerOrderSubmitDisabled(false, 2, true, false, false), true);
});

test("negative-stock policy controls the capture limit", () => {
  assert.equal(sellerOrderMaximumQuantity(true, 3, null, false), 3);
  assert.equal(sellerOrderMaximumQuantity(true, 3, null, true), Number.POSITIVE_INFINITY);
  assert.equal(sellerOrderMaximumQuantity(false, 0, null, false), Number.POSITIVE_INFINITY);
});

test("complete editing renders the order snapshot before the catalog refresh finishes", () => {
  const state = editableOrderInitialState([{
    orderItemId: "line-1",
    productId: "product-1",
    productCode: "ROS-12",
    sku: null,
    productName: "Rosa roja",
    unitCode: "UND",
    quantity: 12,
    unitPrice: 4_500,
    discountAmount: 0,
    lineTotal: 54_000,
    quantityOnHand: 3,
    manageStock: true,
    priceSource: "Captured",
    reservedQuantity: 4,
  }]);

  assert.deepEqual(state.quantities, { "product-1": 12 });
  assert.deepEqual(state.knownItems["product-1"], {
    productId: "product-1",
    productCode: "ROS-12",
    name: "Rosa roja",
    unitCode: "UND",
    unitPrice: 4_500,
    priceSource: "Captured",
    quantityOnHand: 7,
    manageStock: true,
  });
});
