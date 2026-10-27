namespace Auraly.Commerce.Accounting.Contracts;

public sealed record AccountingAccountSemantics(Guid? ParentAccountId, string FinancialFunction,
    string BalanceSide, Guid? BusinessId, Guid? BankAccountId, Guid? PaymentMethodOptionId);
