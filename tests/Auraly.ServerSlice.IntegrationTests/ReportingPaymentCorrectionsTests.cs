namespace Auraly.ServerSlice.IntegrationTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PaymentReportingCorrectionsCollection : ICollectionFixture<ServerSliceFixture>
{
    public const string Name = "Auraly payment reporting corrections";
}

[Collection(PaymentReportingCorrectionsCollection.Name)]
[Trait("EngineCertification", "Reporting")]
public sealed class ReportingPaymentCorrectionsTests(ServerSliceFixture fixture, Xunit.Abstractions.ITestOutputHelper output)
{
    [Theory]
    [InlineData("Cash", "Transfer")]
    [InlineData("Cash", "DebitCard")]
    [InlineData("DebitCard", "CreditCard")]
    public async Task Confirmed_cash_closure_projects_effective_tender_once(string original, string target)
    {
        var flow = new AccountingVerticalSliceTests(fixture, output);
        await flow.InitializeAsync();
        try
        {
            await flow.Closure_native_form_correction_preserves_invoice_and_uses_actual_accounts_once(
                original, target, false, false, false, false, verifyReporting: true);
        }
        finally { await flow.DisposeAsync(); }
    }

    [Theory]
    [InlineData("Transfer", "Cash", "None", true, true)]
    [InlineData("DebitCard", "Cash", "None", true, false)]
    [InlineData("CreditCard", "Cash", "None", true, false)]
    [InlineData("Cash", "Cash", "None", true, false)]
    public async Task Confirmed_dispatch_refreshes_only_actual_tenders_and_survives_replay_and_rebuild(
        string method, string original, string returned, bool before, bool rebuild)
    {
        var flow = new AccountingVerticalSliceTests(fixture, output);
        await flow.InitializeAsync();
        try
        {
            await flow.Order_dispatch_collection_uses_one_session_and_reclassifies_only_changed_accounts(
                "SalesReceipt", method, method == original, false, returnCase: returned,
                originalMethod: original, verifyReporting: true, projectBeforeSettlement: before, rebuildReporting: rebuild);
        }
        finally { await flow.DisposeAsync(); }
    }
}
