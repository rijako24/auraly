"use client";

import { formatCurrency, formatDateTime } from "@/lib/utils";
import { Button } from "@/components/ui/button";

function movementLabel(type: string, payable: boolean) {
  switch (type) {
    case "Opening": return payable ? "Obligación creada" : "Cuenta por cobrar creada";
    case "Payment": return payable ? "Pago aplicado" : "Abono aplicado";
    case "CreditApplication": return "Anticipo o saldo a favor aplicado";
    case "Unapplication": return "Abono desaplicado";
    case "CreditUnapplication": return "Cruce desaplicado · saldo a favor restituido";
    case "Credit": case "Reversal": return "Devolución o anulación aplicada";
    case "Adjustment": return "Ajuste de saldo";
    default: return type;
  }
}

export function PortfolioTransactions({ transactions, currencyCode, direction, onUnapply }: {
  transactions: ReadonlyArray<{ transactionId: string; type: string; occurredAt: string; balanceChange: number }>;
  currencyCode: string; direction: "payable" | "receivable";
  onUnapply?: (transactionId: string) => void;
}) {
  return <section><h3 className="mb-3 text-sm font-semibold">Movimientos</h3><div className="space-y-2">
    {transactions.map(transaction => <div key={transaction.transactionId} className="flex items-center justify-between rounded-lg border p-3 text-sm">
      <div><p className="font-medium">{movementLabel(transaction.type, direction === "payable")}</p><p className="text-xs text-muted-foreground">{formatDateTime(transaction.occurredAt)}</p></div>
      <div className="flex items-center gap-3">{Number.isFinite(transaction.balanceChange) ? <span className={transaction.balanceChange < 0 ? "font-semibold text-emerald-700" : "font-semibold"}>
        {transaction.balanceChange < 0 ? "−" : "+"}{formatCurrency(Math.abs(transaction.balanceChange), currencyCode, 4)}
      </span> : <span className="text-muted-foreground">Variación no disponible</span>}
      {transaction.type === "Payment" && onUnapply && <Button variant="outline" size="sm" onClick={() => onUnapply(transaction.transactionId)}>Desaplicar</Button>}
      {transaction.type === "CreditApplication" && onUnapply && <Button variant="outline" size="sm" onClick={() => onUnapply(transaction.transactionId)}>Desaplicar cruce</Button>}
      </div>
    </div>)}
  </div></section>;
}
