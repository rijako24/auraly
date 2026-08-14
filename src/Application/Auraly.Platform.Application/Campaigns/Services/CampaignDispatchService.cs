using System.Text.Json;
using Auraly.Platform.Application.Billing;
using Auraly.Platform.Application.Campaigns.Interfaces;
using Auraly.Platform.Application.Services;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Enums;
using Auraly.Platform.Domain.Repositories;

namespace Auraly.Platform.Application.Campaigns.Services;

public sealed class CampaignDispatchService : ICampaignDispatchService
{
    private const int BatchSize = 25;
    private static readonly TimeSpan SendingStaleAfter = TimeSpan.FromMinutes(15);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IUnitOfWork _unitOfWork;
    private readonly IWhatsAppService _whatsApp;
    private readonly IUsageBillingService _usageBilling;

    public CampaignDispatchService(
        IUnitOfWork unitOfWork,
        IWhatsAppService whatsApp,
        IUsageBillingService usageBilling)
    {
        _unitOfWork = unitOfWork;
        _whatsApp = whatsApp;
        _usageBilling = usageBilling;
    }

    public async Task DispatchAsync(Guid campaignId, CancellationToken ct = default)
    {
        var campaign = await _unitOfWork.Campaigns.GetByIdAsync(campaignId, ct);
        if (campaign is null || campaign.Status == CampaignStatuses.Cancelled)
            return;

        if (campaign.Status is CampaignStatuses.Completed or CampaignStatuses.CompletedWithErrors)
            return;

        campaign.Status = CampaignStatuses.Processing;
        campaign.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.Campaigns.UpdateAsync(campaign, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        while (!ct.IsCancellationRequested)
        {
            var recipients = await ClaimBatchAsync(campaignId, ct);
            if (recipients.Count == 0)
                break;

            foreach (var recipient in recipients)
                await SendRecipientAsync(campaign, recipient, ct);
        }

        campaign.Status = campaign.FailedCount > 0
            ? CampaignStatuses.CompletedWithErrors
            : CampaignStatuses.Completed;
        campaign.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.Campaigns.UpdateAsync(campaign, ct);
        await _unitOfWork.SaveChangesAsync(ct);
    }

    private async Task<IReadOnlyList<CampaignRecipient>> ClaimBatchAsync(Guid campaignId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var staleBefore = now.Subtract(SendingStaleAfter);
        var recipients = await _unitOfWork.Campaigns.GetDispatchableRecipientsAsync(campaignId, BatchSize, staleBefore, ct);

        foreach (var recipient in recipients)
        {
            recipient.Status = CampaignRecipientStatuses.Sending;
            recipient.AttemptCount += 1;
            recipient.LastAttemptAtUtc = now;
            recipient.Error = null;
        }

        if (recipients.Count > 0)
            await _unitOfWork.SaveChangesAsync(ct);

        return recipients;
    }

    private async Task SendRecipientAsync(Campaign campaign, CampaignRecipient recipient, CancellationToken ct)
    {
        try
        {
            var bodyParameters = ResolveBodyParameters(campaign, recipient);
            var messageId = await _whatsApp.SendTemplateMessageAsync(
                campaign.BusinessId,
                recipient.PhoneNormalized,
                campaign.TemplateName,
                campaign.LanguageCode,
                [],
                bodyParameters);

            recipient.Status = CampaignRecipientStatuses.Sent;
            recipient.WhatsAppMessageId = messageId;
            recipient.SentAt = DateTime.UtcNow;
            campaign.SentCount += 1;

            await _usageBilling.ChargeAsync(new UsageChargeRequest(
                campaign.BusinessId,
                AgentId: null,
                ConversationId: null,
                MessageId: null,
                OperationType: string.Equals(campaign.TemplateCategory, "Utility", StringComparison.OrdinalIgnoreCase)
                    ? UsageOperationType.WhatsappUtilityTemplate
                    : UsageOperationType.WhatsappMarketingTemplate,
                OutboundMessages: 1,
                MetadataJson: JsonSerializer.Serialize(new
                {
                    channel = "whatsapp",
                    campaignId = campaign.CampaignId,
                    recipientId = recipient.CampaignRecipientId
                }, JsonOptions)), ct);
        }
        catch (Exception ex)
        {
            recipient.Status = CampaignRecipientStatuses.Failed;
            recipient.Error = ex.Message.Length > 1000 ? ex.Message[..1000] : ex.Message;
            campaign.FailedCount += 1;
        }

        campaign.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.Campaigns.UpdateAsync(campaign, ct);
        await _unitOfWork.SaveChangesAsync(ct);
    }

    private static IReadOnlyList<string> ResolveBodyParameters(Campaign campaign, CampaignRecipient recipient)
    {
        var mapping = Deserialize<CampaignParameterMapping>(campaign.ParameterMappingJson)
            ?? new CampaignParameterMapping([]);
        var variables = Deserialize<Dictionary<string, string>>(recipient.VariablesJson)
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        return mapping.BodyParameterKeys
            .Select(key => variables.TryGetValue(key, out var value) ? value : string.Empty)
            .ToList();
    }

    private static T? Deserialize<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return default;

        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch
        {
            return default;
        }
    }

    private sealed record CampaignParameterMapping(IReadOnlyList<string> BodyParameterKeys);
}
