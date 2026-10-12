const round = (value: number) => Math.round(value * 10000) / 10000;

export function settleRestaurantPayment(total: number, rows: ReadonlyArray<{
  methodCode: string; amount: string;
}>, cashTipIncluded = 0) {
  const nonCashReceived = round(rows.filter((row) => row.methodCode !== "Cash")
    .reduce((sum, row) => sum + (Number(row.amount) || 0), 0));
  const cashTendered = round(rows.filter((row) => row.methodCode === "Cash")
    .reduce((sum, row) => sum + (Number(row.amount) || 0), 0));
  const cashSaleTendered = round(cashTendered - cashTipIncluded);
  const cashApplied = round(Math.min(Math.max(0, cashSaleTendered),
    Math.max(0, total - nonCashReceived)));
  return {
    cashTendered,
    cashSaleTendered,
    cashApplied,
    due: round(total - nonCashReceived - cashApplied),
    change: round(nonCashReceived > total ? 0 :
      Math.max(0, cashSaleTendered - (total - nonCashReceived))),
  };
}
