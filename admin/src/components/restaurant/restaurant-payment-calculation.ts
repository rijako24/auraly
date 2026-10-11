const round = (value: number) => Math.round(value * 10000) / 10000;

export function settleRestaurantPayment(total: number, rows: ReadonlyArray<{
  methodCode: string; amount: string;
}>) {
  const nonCashReceived = round(rows.filter((row) => row.methodCode !== "Cash")
    .reduce((sum, row) => sum + (Number(row.amount) || 0), 0));
  const cashTendered = round(rows.filter((row) => row.methodCode === "Cash")
    .reduce((sum, row) => sum + (Number(row.amount) || 0), 0));
  const cashApplied = round(Math.min(cashTendered, Math.max(0, total - nonCashReceived)));
  return {
    cashTendered,
    cashApplied,
    due: round(total - nonCashReceived - cashApplied),
    change: round(nonCashReceived > total ? 0 : Math.max(0, cashTendered - (total - nonCashReceived))),
  };
}
