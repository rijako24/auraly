CREATE TABLE [dbo].[SalesReturnReceivableApplications]
(
    [ReturnId] UNIQUEIDENTIFIER NOT NULL,
    [ReceivableId] UNIQUEIDENTIFIER NOT NULL,
    [Amount] DECIMAL(19,4) NOT NULL,
    [AppliedAt] DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT [PK_SalesReturnReceivableApplications] PRIMARY KEY ([ReturnId]),
    CONSTRAINT [FK_SalesReturnReceivableApplications_Returns]
      FOREIGN KEY ([ReturnId]) REFERENCES [dbo].[SalesReturns] ([ReturnId]),
    CONSTRAINT [FK_SalesReturnReceivableApplications_Receivables]
      FOREIGN KEY ([ReceivableId]) REFERENCES [dbo].[Receivables] ([ReceivableId]),
    CONSTRAINT [CK_SalesReturnReceivableApplications_Amount] CHECK ([Amount] > 0)
);
GO
CREATE INDEX [IX_SalesReturnReceivableApplications_Receivable]
  ON [dbo].[SalesReturnReceivableApplications] ([ReceivableId],[AppliedAt]);
GO
