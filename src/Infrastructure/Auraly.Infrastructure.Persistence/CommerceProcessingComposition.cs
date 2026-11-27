using Auraly.Application.DocumentProcessing;
using Auraly.Application.Fiscal;
using Auraly.Application.Dispatching;
using Auraly.Application.Receivables;
using Auraly.Application.Returns;
using Auraly.Commerce.Accounting.Application;
using Auraly.Commerce.Accounting.Infrastructure;
using Auraly.Commerce.Taxation.Application;
using Auraly.Commerce.Taxation.Domain;
using Auraly.Contracts.Fiscal;
using Microsoft.Extensions.DependencyInjection;

namespace Auraly.Infrastructure.Persistence;

public static class CommerceProcessingComposition
{
    public static void AddDispatchSettlementExecution(this IServiceCollection services)
    {
        services.AddScoped<IReceivablesStore, SqlReceivablesStore>();
        services.AddScoped<ReceivablesService>();
        services.AddScoped<ISalesReturnStore, SqlSalesReturnStore>();
        services.AddScoped<SalesReturnService>();
        services.AddScoped<SqlDispatchSettlementPreparationStore>();
        services.AddScoped<IDispatchSettlementPreparationStore>(provider =>
            provider.GetRequiredService<SqlDispatchSettlementPreparationStore>());
        services.AddScoped<DispatchSettlementPreparation>();
    }

    public static void AddFiscalProcessingPersistence(this IServiceCollection services)
    {
        services.AddSingleton<ConfigurationFiscalTechnicalKeyProvider>();
        services.AddSingleton<SqlProtectedFiscalTechnicalKeyStore>();
        services.AddSingleton<IFiscalTechnicalKeyProvider, CompositeFiscalTechnicalKeyProvider>();
        services.AddSingleton<SqlProtectedFiscalCredentialVault>();
        services.AddScoped<IFiscalGenerationWorkStore, SqlFiscalGenerationWorkStore>();
        services.AddScoped<IFiscalSubmissionWorkStore, SqlFiscalSubmissionWorkStore>();
        services.AddScoped<IDianHabilitationConfigurationProvider, SqlDianHabilitationConfigurationProvider>();
        services.AddScoped<IDianProductionConfigurationProvider, SqlDianProductionConfigurationProvider>();
    }

    public static void AddWithholdingCalculation(this IServiceCollection services)
    {
        services.AddScoped<IWithholdingRuleStore, SqlWithholdingRuleStore>();
        services.AddScoped<WithholdingEngine>();
        services.AddScoped<WithholdingService>();
    }

    public static void AddDocumentProcessingExecution(this IServiceCollection services)
    {
        services.AddScoped<SqlDocumentProcessingSessionAccessor>();
        services.AddScoped<SqlInventoryLedgerWriter>();
        services.AddScoped<IDocumentProcessingJobStore, SqlDocumentProcessingJobStore>();
        services.AddScoped<IDocumentProcessingWorkSource, SqlDocumentProcessingWorkSource>();
        services.AddScoped<SqlDocumentContinuationOutbox>();
        services.AddScoped<IDocumentProcessingCompletionObserver,
            SqlDocumentProcessingCompletionObserver>();
        services.AddScoped<SqlPosSaleDocumentHandler>();
        services.AddScoped<IConfirmedDocumentHandler>(services => services.GetRequiredService<SqlPosSaleDocumentHandler>());
        services.AddScoped<IConfirmedDocumentHandler, SqlSalesReceiptDocumentHandler>();
        services.AddScoped<SqlGoodsReceiptDocumentHandler>();
        services.AddScoped<IConfirmedDocumentHandler>(services => services.GetRequiredService<SqlGoodsReceiptDocumentHandler>());
        services.AddScoped<IConfirmedDocumentHandler, SqlGoodsReceiptCostRevisionDocumentHandler>();
        services.AddScoped<IConfirmedDocumentHandler, SqlExpenseDocumentHandler>();
        services.AddScoped<IConfirmedDocumentHandler, SqlPurchaseReturnDocumentHandler>();
        services.AddScoped<IConfirmedDocumentHandler, SqlCashReceiptDocumentHandler>();
        services.AddScoped<IConfirmedDocumentHandler, SqlCashDisbursementDocumentHandler>();
        services.AddScoped<IConfirmedDocumentHandler, SqlSalesReturnDocumentHandler>();
        services.AddScoped<IConfirmedDocumentHandler, SqlSalesDebitNoteDocumentHandler>();
        services.AddScoped<SqlInventoryOperationProcessor>();
        services.AddScoped<IConfirmedDocumentHandler, SqlStockCountDocumentHandler>();
        services.AddScoped<IConfirmedDocumentHandler, SqlInventoryDamageDocumentHandler>();
        services.AddScoped<IConfirmedDocumentHandler, SqlInventoryAdjustmentDocumentHandler>();
        services.AddScoped<IConfirmedDocumentHandler, SqlWarehouseTransferDocumentHandler>();
        services.AddScoped<IConfirmedDocumentHandler, SqlWarehouseTransferDispatchDocumentHandler>();
        services.AddScoped<IConfirmedDocumentHandler, SqlWarehouseTransferReceiptDocumentHandler>();
        services.AddScoped<IConfirmedDocumentHandler, SqlProductConversionDocumentHandler>();
        services.AddScoped<IConfirmedDocumentHandler, SqlProductionDocumentHandler>();
        services.AddScoped<IConfirmedDocumentHandler, SqlDispatchCashDifferenceDocumentHandler>();
        services.AddScoped<DocumentProcessingEngine>();
        services.AddScoped<DocumentProcessingWorker>();
    }

    public static void AddAccountingProcessingExecution(this IServiceCollection services)
    {
        services.AddScoped<SqlAccountingPostingProcessor>();
        services.AddScoped<IAccountingPostingCompletionObserver, SqlAccountingReportingCompletionObserver>();
    }

    public static void AddSalesReportingExecution(this IServiceCollection services)
    {
        services.AddScoped<SqlSalesReportingProjectionWriter>();
        services.AddScoped<SqlSalesReportingProcessor>();
    }
}
