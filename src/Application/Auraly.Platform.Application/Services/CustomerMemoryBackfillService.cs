using Auraly.Platform.Application.Agents;
using Auraly.Platform.Application.Agents.Configuration;
using Auraly.Platform.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace Auraly.Platform.Application.Services;

public interface ICustomerMemoryBackfillService
{
    Task<CustomerMemoryBackfillResult> RunAsync(CancellationToken ct = default);
}

public sealed record CustomerMemoryBackfillResult(int BusinessesProcessed, int CustomersProcessed, int FactsWritten);

public sealed class CustomerMemoryBackfillService : ICustomerMemoryBackfillService
{
    private const int TenantPageSize = 100;

    private readonly IUnitOfWork _unitOfWork;
    private readonly IAgentRepository _agentRepository;
    private readonly IAgentConfigProvider _agentConfigProvider;
    private readonly ICustomerMemoryService _customerMemory;
    private readonly ILogger<CustomerMemoryBackfillService> _logger;

    public CustomerMemoryBackfillService(
        IUnitOfWork unitOfWork,
        IAgentRepository agentRepository,
        IAgentConfigProvider agentConfigProvider,
        ICustomerMemoryService customerMemory,
        ILogger<CustomerMemoryBackfillService> logger)
    {
        _unitOfWork = unitOfWork;
        _agentRepository = agentRepository;
        _agentConfigProvider = agentConfigProvider;
        _customerMemory = customerMemory;
        _logger = logger;
    }

    public async Task<CustomerMemoryBackfillResult> RunAsync(CancellationToken ct = default)
    {
        var factsWritten = 0;
        var customersProcessed = 0;
        var businessesProcessed = 0;
        var page = 1;

        while (true)
        {
            var (tenants, totalCount) = await _unitOfWork.Tenants.GetPagedAsync(page, TenantPageSize, null, ct);
            if (tenants.Count == 0)
                break;

            foreach (var tenant in tenants)
            {
                var businesses = await _unitOfWork.Businesses.GetByTenantIdAsync(tenant.TenantId, ct);

                foreach (var business in businesses)
                {
                    ct.ThrowIfCancellationRequested();
                    businessesProcessed++;

                    var activeAgent = await _agentRepository.GetActiveCustomerByBusinessAsync(business.BusinessId, ct);
                    if (activeAgent is null)
                    {
                        _logger.LogWarning(
                            "Backfill: no active agent for business {BusinessId}",
                            business.BusinessId);
                        continue;
                    }

                    var config = await _agentConfigProvider.GetConfigAsync(activeAgent.AgentId, ct);
                    var persistentKeys = new HashSet<string>(
                        config.FactSchema
                            .Where(e => e.ShouldRememberAcrossRequests())
                            .Select(e => e.Key),
                        StringComparer.OrdinalIgnoreCase);

                    if (persistentKeys.Count == 0)
                        continue;

                    var leads = await _unitOfWork.Leads.GetByBusinessIdAsync(business.BusinessId);

                    foreach (var lead in leads)
                    {
                        ct.ThrowIfCancellationRequested();
                        customersProcessed++;

                        if (!string.IsNullOrWhiteSpace(lead.CustomerName)
                            && persistentKeys.Contains(ConversationFactKeys.CustomerName))
                        {
                            await _customerMemory.RememberAsync(
                                business.BusinessId,
                                lead.UserNumber,
                                ConversationFactKeys.CustomerName,
                                lead.CustomerName.Trim(),
                                ct);
                            factsWritten++;
                        }

                        if (!string.IsNullOrWhiteSpace(lead.CustomerEmail)
                            && persistentKeys.Contains(ConversationFactKeys.CustomerEmail))
                        {
                            await _customerMemory.RememberAsync(
                                business.BusinessId,
                                lead.UserNumber,
                                ConversationFactKeys.CustomerEmail,
                                lead.CustomerEmail.Trim(),
                                ct);
                            factsWritten++;
                        }

                        var latestConversation = await _unitOfWork.Conversations.GetByBusinessIdAndUserNumberAsync(
                            business.BusinessId, lead.UserNumber);

                        if (latestConversation is null)
                            continue;

                        var contexts = await _unitOfWork.ConversationContexts.GetByConversationIdAsync(
                            latestConversation.ConversationId);

                        foreach (var context in contexts)
                        {
                            if (!persistentKeys.Contains(context.Field)
                                || string.IsNullOrWhiteSpace(context.Value))
                            {
                                continue;
                            }

                            await _customerMemory.RememberAsync(
                                business.BusinessId,
                                lead.UserNumber,
                                context.Field,
                                context.Value.Trim(),
                                ct);
                            factsWritten++;
                        }
                    }
                }
            }

            if (page * TenantPageSize >= totalCount)
                break;

            page++;
        }

        _logger.LogInformation(
            "Customer memory backfill complete: businesses={Businesses}, customers={Customers}, facts={Facts}",
            businessesProcessed, customersProcessed, factsWritten);

        return new CustomerMemoryBackfillResult(businessesProcessed, customersProcessed, factsWritten);
    }
}
