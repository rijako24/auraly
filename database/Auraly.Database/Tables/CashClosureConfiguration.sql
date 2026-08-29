CREATE TABLE [worksessions].[CashClosurePaymentMethodMappings]
(
    [PaymentMethodCode] NVARCHAR(32) NOT NULL,
    [ClosureMethodCode] NVARCHAR(32) NOT NULL,
    [RequiresCount] BIT NOT NULL,
    [SortOrder] INT NOT NULL,
    CONSTRAINT [PK_CashClosurePaymentMethodMappings] PRIMARY KEY CLUSTERED ([PaymentMethodCode]),
    CONSTRAINT [CK_CashClosurePaymentMethodMappings_SortOrder] CHECK ([SortOrder] >= 0)
);
GO
