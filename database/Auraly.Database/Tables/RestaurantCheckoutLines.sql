CREATE TABLE [sales].[RestaurantCheckoutLines]
(
    [DocumentId] UNIQUEIDENTIFIER NOT NULL,
    [LineNumber] INT NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [AccountId] UNIQUEIDENTIFIER NOT NULL,
    [AdditionId] UNIQUEIDENTIFIER NOT NULL,
    [ProductRecipeVersionId] UNIQUEIDENTIFIER NULL,
    [RecognizedCostAmount] DECIMAL(19,4) NULL,
    CONSTRAINT [PK_RestaurantCheckoutLines] PRIMARY KEY ([DocumentId],[LineNumber]),
    CONSTRAINT [UQ_RestaurantCheckoutLines_Addition] UNIQUE ([AccountId],[AdditionId]),
    CONSTRAINT [FK_RestaurantCheckoutLines_Account] FOREIGN KEY ([BusinessId],[AccountId])
        REFERENCES [sales].[RestaurantAccounts] ([BusinessId],[AccountId]),
    CONSTRAINT [FK_RestaurantCheckoutLines_Addition] FOREIGN KEY ([AccountId],[AdditionId])
        REFERENCES [sales].[RestaurantAccountAdditions] ([AccountId],[AdditionId]),
    CONSTRAINT [FK_RestaurantCheckoutLines_RecipeVersion] FOREIGN KEY ([TenantId],[ProductRecipeVersionId])
        REFERENCES [catalog].[ProductRecipeVersions] ([TenantId],[ProductRecipeVersionId]),
    CONSTRAINT [FK_RestaurantCheckoutLines_Business] FOREIGN KEY ([BusinessId],[TenantId])
        REFERENCES [dbo].[Businesses] ([BusinessId],[TenantId])
);
GO
CREATE TABLE [sales].[RestaurantRecipeConsumptionLines]
(
    [DocumentId] UNIQUEIDENTIFIER NOT NULL,
    [SaleLineNumber] INT NOT NULL,
    [MovementLineNumber] INT NOT NULL,
    [IngredientProductId] UNIQUEIDENTIFIER NOT NULL,
    [Quantity] DECIMAL(19,6) NOT NULL,
    [RecognizedCostAmount] DECIMAL(19,4) NULL,
    CONSTRAINT [PK_RestaurantRecipeConsumptionLines]
        PRIMARY KEY ([DocumentId],[MovementLineNumber]),
    CONSTRAINT [FK_RestaurantRecipeConsumptionLines_CheckoutLine]
        FOREIGN KEY ([DocumentId],[SaleLineNumber])
        REFERENCES [sales].[RestaurantCheckoutLines] ([DocumentId],[LineNumber]),
    CONSTRAINT [FK_RestaurantRecipeConsumptionLines_Ingredient]
        FOREIGN KEY ([IngredientProductId]) REFERENCES [dbo].[Products] ([ProductId]),
    CONSTRAINT [CK_RestaurantRecipeConsumptionLines_Quantity]
        CHECK ([Quantity]>0)
);
GO
CREATE INDEX [IX_RestaurantCheckoutLines_Account]
    ON [sales].[RestaurantCheckoutLines] ([AccountId],[LineNumber]);
GO
