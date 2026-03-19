using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Domain.Models.Flow;

namespace MimosBabySpa.Application.GenericFlow.Services;

/// <summary>
/// Provides deterministic regex-based intention detection used ONLY as a degraded fallback
/// when the extraction LLM call fails (content filter, timeout, malformed JSON).
///
/// In normal operation, intentions are detected inside the extraction LLM call alongside
/// field extraction — no separate LLM call is made.
///
/// Only intentions with DegradedRegex configured are evaluated. All others default to false.
/// </summary>
public class FlowIntentionDetector
{
    private readonly ILogger<FlowIntentionDetector> _logger;

    public FlowIntentionDetector(ILogger<FlowIntentionDetector> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Runs regex-only detection against intentions that have DegradedRegex configured.
    /// Called exclusively when extraction fails. All intentions without DegradedRegex default to false.
    /// </summary>
    public Dictionary<string, bool> DegradedDetect(string userMessage, FlowDefinitionDocument flow)
    {
        var results = new Dictionary<string, bool>();

        foreach (var intention in flow.IntentionSchema)
        {
            if (string.IsNullOrEmpty(intention.DegradedRegex))
            {
                results[intention.Key] = false;
                continue;
            }

            try
            {
                var matched = Regex.IsMatch(userMessage, intention.DegradedRegex, RegexOptions.IgnoreCase);
                results[intention.Key] = matched;

                if (matched)
                    _logger.LogDebug("Degraded: intention {Key} matched via regex", intention.Key);
            }
            catch (RegexParseException ex)
            {
                _logger.LogWarning(ex, "Invalid degraded regex for intention {Key}", intention.Key);
                results[intention.Key] = false;
            }
        }

        return results;
    }
}
