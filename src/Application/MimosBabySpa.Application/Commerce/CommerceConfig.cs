using MimosBabySpa.Domain.Enums;

namespace MimosBabySpa.Application.Commerce;

public sealed class CommerceConfig
{
    public bool Enabled { get; init; }
    public CommerceProvider Provider { get; init; } = CommerceProvider.Local;
    public int OfferMemoryMaxSnapshots { get; init; } = 8;
    public int OfferMemoryMaxProducts { get; init; } = 100;
    public CommerceConversationPolicy Conversation { get; init; } = new();
    public PendingCartPolicy PendingCart { get; init; } = new();
    public ProductMatchingPolicy Matching { get; init; } = new();
}

public sealed class CommerceConversationPolicy
{
    public IReadOnlyList<string> ContextualConfirmationPhrases { get; init; } = [];
    public IReadOnlyList<CommercePhraseRule> CartReviewRules { get; init; } = [];
    public IReadOnlyList<CommercePhraseRule> ProductReplacementRules { get; init; } = [];
    public IReadOnlyList<string> CandidateSelectionPhrases { get; init; } = [];
    public IReadOnlyList<string> ClauseSeparators { get; init; } = [];
    public IReadOnlyList<string> AdditionalRequestPhrases { get; init; } = [];
    public IReadOnlyDictionary<string, decimal> QuantityWords { get; init; }
        = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
}

public sealed class CommercePhraseRule
{
    public string Phrase { get; init; } = string.Empty;
    public string Match { get; init; } = CommercePhraseMatchModes.Exact;
}

public static class CommercePhraseMatchModes
{
    public const string Exact = "exact";
    public const string Contains = "contains";
    public const string Prefix = "prefix";
    public const string Suffix = "suffix";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Exact, Contains, Prefix, Suffix
    };
}
public sealed class PendingCartPolicy
{
    public IReadOnlyList<string> DiscardOnFinalizeIssueCodes { get; init; } = [];
    public IReadOnlyList<string> FinalizeConfirmationPhrases { get; init; } = [];
    public IReadOnlyList<CommercePhraseRule> CancellationRules { get; init; } = [];
    public IReadOnlyList<string> QuantityCorrectionPhrases { get; init; } = [];
    public bool DiscardAllOnExplicitFinalization { get; init; }
}

public sealed class ProductMatchingPolicy
{
    public int ExactNameDominanceMinimumMatches { get; init; }
    public double CandidateMentionSimilarity { get; init; } = 0.8d;
    public double PendingReferenceSimilarity { get; init; } = 0.78d;
    public double CandidateSelectionSimilarity { get; init; } = 0.6d;
}