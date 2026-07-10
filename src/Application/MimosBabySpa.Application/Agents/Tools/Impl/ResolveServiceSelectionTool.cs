using System.Globalization;
using System.Text;
using System.Text.Json;
using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Agents.Facts;
using MimosBabySpa.Application.Services;

namespace MimosBabySpa.Application.Agents.Tools.Impl;

[AgentToolMetadata("resolve_service_selection", Capabilities = new[] { ToolCapabilities.FactWrite })]
public sealed class ResolveServiceSelectionTool : IAgentTool
{
    private readonly ServiceSelectionResolver _resolver;
    private readonly IConversationFactsService _factsService;
    private readonly IAddOnCatalogService _addOnCatalog;

    public ResolveServiceSelectionTool(
        ServiceSelectionResolver resolver,
        IConversationFactsService factsService,
        IAddOnCatalogService addOnCatalog)
    {
        _resolver = resolver;
        _factsService = factsService;
        _addOnCatalog = addOnCatalog;
    }

    public string Name => "resolve_service_selection";

    public IReadOnlyList<string> Capabilities => [ToolCapabilities.FactWrite];

    public string Description =>
        "Resolves a customer service selection against the active catalog and stores booking.service only for a new/current booking request. " +
        "It does not apply service changes to an existing reservation.";

    public string ParametersSchema => """
        {
          "type": "object",
          "properties": {
            "text": {
              "type": "string",
              "description": "Customer wording for the service selection."
            }
          },
          "required": ["text"]
        }
        """;

    public async Task<string> ExecuteAsync(
        JsonElement arguments,
        AgentToolContext ctx,
        CancellationToken cancellationToken = default)
    {
        var text = ToolResultHelper.TryGetString(arguments, "text", out var rawText)
            ? rawText
            : ctx.LatestUserMessage;

        text = ChooseCustomerSupportedText(text, ctx.LatestUserMessage);

        if (string.IsNullOrWhiteSpace(text))
            return ToolResultHelper.MissingPrerequisites(["text"]);

        var roleIndex = new FactRoleIndex(ctx.Config?.FactSchema ?? []);
        var key = roleIndex.KeyByRole("booking.service") ?? ConversationFactKeys.Service;
        ctx.Facts.TryGetValue(key, out var currentService);

        var resolution = await _resolver.ResolveAsync(ctx.BusinessId, text, ct: cancellationToken);
        if (!string.IsNullOrWhiteSpace(currentService)
            && resolution.Status == ServiceSelectionStatus.Resolved
            && !string.IsNullOrWhiteSpace(resolution.ServiceName)
            && currentService.Trim().Equals(resolution.ServiceName.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return ToolResultHelper.Ok(new
            {
                selection_status = "resolved",
                service = resolution.ServiceName,
                key,
                unchanged = true,
                storage = "fact_unchanged"
            });
        }

        if (!string.IsNullOrWhiteSpace(currentService))
        {
            var addOnValidation = await _addOnCatalog.ValidateAsync(ctx.BusinessId, currentService, text, cancellationToken);
            if (addOnValidation.IsValid && !string.IsNullOrWhiteSpace(addOnValidation.NormalizedCsv))
            {
                return ToolResultHelper.ErrorWithLlm(
                    "add_on_selection_detected",
                    "Selection matches a compatible add-on for the current service, not a service change.",
                    new
                    {
                        next_action = "set_fact",
                        key = ConversationFactKeys.AddOns,
                        value = addOnValidation.NormalizedCsv
                    },
                    recoverable: true);
            }
        }

        if (resolution.Status != ServiceSelectionStatus.Resolved || string.IsNullOrWhiteSpace(resolution.ServiceName))
            return ServiceSelectionToolResults.Unresolved(resolution, text.Trim());

        var schemaEntry = roleIndex.EntryFor(key);

        await _factsService.SetAsync(
            ctx.ConversationId,
            ctx.BusinessId,
            key,
            resolution.ServiceName,
            schemaEntry?.ShouldRememberAcrossRequests() ?? false,
            cancellationToken);

        ctx.Facts[key] = resolution.ServiceName;

        return ToolResultHelper.Ok(new
        {
            selection_status = "resolved",
            service = resolution.ServiceName,
            key,
            storage = "fact"
        });
    }
    private static string? ChooseCustomerSupportedText(string? toolText, string? latestUserMessage)
    {
        if (string.IsNullOrWhiteSpace(toolText) || string.IsNullOrWhiteSpace(latestUserMessage))
            return toolText;

        return IsSupportedByLatestMessage(toolText, latestUserMessage)
            ? toolText
            : latestUserMessage;
    }

    private static bool IsSupportedByLatestMessage(string toolText, string latestUserMessage)
    {
        var toolCompact = Compact(toolText);
        var messageCompact = Compact(latestUserMessage);
        if (string.IsNullOrWhiteSpace(toolCompact) || string.IsNullOrWhiteSpace(messageCompact))
            return true;

        if (toolCompact.Equals(messageCompact, StringComparison.Ordinal)
            || messageCompact.Contains(toolCompact, StringComparison.Ordinal))
        {
            return true;
        }

        var toolTokens = Tokenize(toolText).Distinct(StringComparer.Ordinal).ToList();
        if (toolTokens.Count == 0)
            return true;

        var messageTokens = Tokenize(latestUserMessage).Distinct(StringComparer.Ordinal).ToList();
        if (messageTokens.Count == 0)
            return false;

        var matched = toolTokens.Count(toolToken => messageTokens.Any(messageToken => TokensMatch(toolToken, messageToken)));
        if (toolTokens.Count == 1)
            return matched == 1;

        var ratio = matched / (double)toolTokens.Count;
        return matched >= 2 && ratio >= 0.67d;
    }

    private static bool TokensMatch(string left, string right) =>
        left.Equals(right, StringComparison.Ordinal)
        || (left.Length >= 4 && right.StartsWith(left, StringComparison.Ordinal))
        || (right.Length >= 4 && left.StartsWith(right, StringComparison.Ordinal));

    private static IReadOnlyList<string> Tokenize(string value)
    {
        var normalized = RemoveDiacritics(value).ToLowerInvariant();
        var tokens = new List<string>();
        var token = new List<char>();

        foreach (var ch in normalized)
        {
            if (char.IsLetterOrDigit(ch))
            {
                token.Add(ch);
                continue;
            }

            FlushToken(token, tokens);
        }

        FlushToken(token, tokens);
        return tokens;
    }

    private static void FlushToken(List<char> token, List<string> tokens)
    {
        if (token.Count == 0)
            return;

        var text = new string(token.ToArray());
        token.Clear();

        if (text.Length >= 3 || text.All(char.IsDigit))
            tokens.Add(text);
    }

    private static string Compact(string value) =>
        string.Concat(RemoveDiacritics(value).Where(char.IsLetterOrDigit)).ToLowerInvariant();

    private static string RemoveDiacritics(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        return string.Concat(normalized.Where(ch =>
            CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark));
    }
}


