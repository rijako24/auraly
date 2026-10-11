import { strict as assert } from "node:assert";
import { test } from "node:test";
import { settleRestaurantPayment } from "./restaurant-payment-calculation";

test("un efectivo mayor al consumo se aplica solo por el total y produce cambio", () => {
  assert.deepEqual(settleRestaurantPayment(76_000, [{ methodCode: "Cash", amount: "100000" }]), {
    cashTendered: 100_000, cashApplied: 76_000, due: 0, change: 24_000,
  });
});

test("el efectivo y la tarjeta completan una sola cuenta sin duplicar cobro", () => {
  assert.deepEqual(settleRestaurantPayment(76_000, [
    { methodCode: "Cash", amount: "50000" },
    { methodCode: "DebitCard", amount: "30000" },
  ]), { cashTendered: 50_000, cashApplied: 46_000, due: 0, change: 4_000 });
});

test("un pago electrónico excesivo no se interpreta como vuelto", () => {
  assert.deepEqual(settleRestaurantPayment(76_000, [
    { methodCode: "CreditCard", amount: "80000" },
  ]), { cashTendered: 0, cashApplied: 0, due: -4_000, change: 0 });
});
