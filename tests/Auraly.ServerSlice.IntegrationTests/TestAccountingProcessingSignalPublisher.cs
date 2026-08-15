using Auraly.Commerce.Accounting.Application;
using Auraly.Commerce.Accounting.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Auraly.ServerSlice.IntegrationTests;

internal sealed class TestAccountingProcessingSignalPublisher(
    IServiceScopeFactory scopes) : IAccountingProcessingSignalPublisher
{
    public async Task PublishAsync(
        AccountingProcessingSignal signal, CancellationToken cancellationToken = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var processor = scope.ServiceProvider.GetRequiredService<SqlAccountingPostingProcessor>();
        await processor.ProcessAsync(signal.DocumentId, signal.DocumentType, signal.BusinessId, cancellationToken);
    }
}
