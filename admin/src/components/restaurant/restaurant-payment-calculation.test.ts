import { strict as assert } from "node:assert";
import { test } from "node:test";
import { finalRestaurantPayments, settleRestaurantPayment } from "./restaurant-payment-calculation";

test("al emitir la factura tras pagos parciales la propina no entra en los medios de venta", () => {
  const salePayments = finalRestaurantPayments([
    { payments: [{ methodCode: "Cash", amount: 80_000, reference: null, tenderedAmount: 90_000 }] },
    { payments: [{ methodCode: "Cash", amount: 110_000, reference: null, tenderedAmount: 110_000 }] },
  ]);
  assert.deepEqual(salePayments, [{ methodCode: "Cash", amount: 190_000,
    reference: null, tenderedAmount: 200_000 }]);
});

test("el último cobro conserva el ajuste al peso de venta directa y agrega los efectivos sin duplicar propina", () => {
  const salePayments = finalRestaurantPayments([
    { payments: [{ methodCode: "Cash", amount: 373_284, reference: null,
      tenderedAmount: 378_284 }] },
    { payments: [{ methodCode: "Cash", amount: 373_284, reference: null,
      tenderedAmount: 373_316, roundingAdjustment: 32 }] },
  ]);
  assert.deepEqual(salePayments, [{ methodCode: "Cash", amount: 746_568,
    reference: null, tenderedAmount: 751_600, roundingAdjustment: 32 }]);
  assert.equal(settleRestaurantPayment(373_284,
    [{ methodCode: "Cash", amount: "443316" }], 70_000, 32).due, 0);
});

test("el ajuste negativo reduce el cobro final y calcula el cambio sobre el total redondeado", () => {
  assert.deepEqual(settleRestaurantPayment(10_749,
    [{ methodCode: "Cash", amount: "11000" }], 0, -49), {
    cashTendered: 11_000, cashSaleTendered: 11_000,
    cashApplied: 10_700, due: 0, change: 300,
  });
});

test("un efectivo mayor al consumo se aplica solo por el total y produce cambio", () => {
  assert.deepEqual(settleRestaurantPayment(76_000, [{ methodCode: "Cash", amount: "100000" }]), {
    cashTendered: 100_000, cashSaleTendered: 100_000,
    cashApplied: 76_000, due: 0, change: 24_000,
  });
});

test("el efectivo y la tarjeta completan una sola cuenta sin duplicar cobro", () => {
  assert.deepEqual(settleRestaurantPayment(76_000, [
    { methodCode: "Cash", amount: "50000" },
    { methodCode: "DebitCard", amount: "30000" },
  ]), { cashTendered: 50_000, cashSaleTendered: 50_000,
    cashApplied: 46_000, due: 0, change: 4_000 });
});

test("un pago electrónico excesivo no se interpreta como vuelto", () => {
  assert.deepEqual(settleRestaurantPayment(76_000, [
    { methodCode: "CreditCard", amount: "80000" },
  ]), { cashTendered: 0, cashSaleTendered: 0,
    cashApplied: 0, due: -4_000, change: 0 });
});

test("la propina escrita se separa del efectivo entregado antes de calcular el cambio", () => {
  assert.deepEqual(settleRestaurantPayment(190_000,
    [{ methodCode: "Cash", amount: "200000" }], 10_000), {
    cashTendered: 200_000, cashSaleTendered: 190_000,
    cashApplied: 190_000, due: 0, change: 0,
  });
  assert.deepEqual(settleRestaurantPayment(190_000,
    [{ methodCode: "Cash", amount: "220000" }], 20_000), {
    cashTendered: 220_000, cashSaleTendered: 200_000,
    cashApplied: 190_000, due: 0, change: 10_000,
  });
});

test("la propina en efectivo no altera un cobro de consumo por tarjeta", () => {
  assert.deepEqual(settleRestaurantPayment(190_000,
    [{ methodCode: "DebitCard", amount: "190000" }]), {
    cashTendered: 0, cashSaleTendered: 0,
    cashApplied: 0, due: 0, change: 0,
  });
});

test("dos medios cubren el consumo y el efectivo incluye propina sin aumentar la factura", () => {
  assert.deepEqual(settleRestaurantPayment(100_000, [
    { methodCode: "Cash", amount: "60000" },
    { methodCode: "DebitCard", amount: "50000" },
  ], 10_000), { cashTendered: 60_000, cashSaleTendered: 50_000,
    cashApplied: 50_000, due: 0, change: 0 });
  assert.deepEqual(settleRestaurantPayment(100_000, [
    { methodCode: "Cash", amount: "70000" },
    { methodCode: "DebitCard", amount: "50000" },
  ], 20_000), { cashTendered: 70_000, cashSaleTendered: 50_000,
    cashApplied: 50_000, due: 0, change: 0 });
});

test("el efectivo puede cubrir solo la propina mientras la tarjeta cubre el consumo", () => {
  assert.deepEqual(settleRestaurantPayment(100_000, [
    { methodCode: "Cash", amount: "10000" },
    { methodCode: "DebitCard", amount: "100000" },
  ], 10_000), { cashTendered: 10_000, cashSaleTendered: 0,
    cashApplied: 0, due: 0, change: 0 });
});
