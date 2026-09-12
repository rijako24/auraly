import assert from "node:assert/strict";
import test from "node:test";

import { editableOrderAvailableQuantity, evaluateOrderReviewQuantity, isOrderReviewLinePending } from "./order-review";

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
