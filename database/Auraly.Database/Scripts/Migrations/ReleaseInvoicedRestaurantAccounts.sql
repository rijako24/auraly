-- Una cuenta facturada ya no debe reservar su mesa. Corrige cuentas emitidas
-- antes de que el cierre liberara la mesa en la misma transacción.
SET QUOTED_IDENTIFIER ON;
UPDATE sales.RestaurantAccounts
SET Status = N'Released',
    ReleasedAt = InvoicedAt,
    UpdatedAt = CASE WHEN UpdatedAt < InvoicedAt THEN InvoicedAt ELSE UpdatedAt END
WHERE Status = N'Invoiced'
  AND InvoicedAt IS NOT NULL
  AND ReleasedAt IS NULL;
