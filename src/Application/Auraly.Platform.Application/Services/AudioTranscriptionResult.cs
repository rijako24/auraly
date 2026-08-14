namespace Auraly.Platform.Application.Services;

public sealed record AudioTranscriptionResult(
    string Text,
    TimeSpan? Duration,
    IReadOnlyList<AudioTranscriptionSegmentSignal> Segments)
{
    public static AudioTranscriptionResult Empty { get; } = new(string.Empty, null, []);
}

public sealed record AudioTranscriptionSegmentSignal(
    string Text,
    TimeSpan Start,
    TimeSpan End,
    double AverageLogProbability,
    double NoSpeechProbability,
    double CompressionRatio);

public enum AudioTranscriptionReliability
{
    Reliable,
    Ambiguous,
    Unreliable
}

public sealed record AudioTranscriptionQualityAssessment(
    AudioTranscriptionReliability Reliability,
    decimal ConfidenceScore,
    bool ShouldAccept,
    string Reason,
    IReadOnlyList<string> Flags);