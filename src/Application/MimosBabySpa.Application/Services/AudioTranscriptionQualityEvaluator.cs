using MimosBabySpa.Application.Configuration;

namespace MimosBabySpa.Application.Services;

public interface IAudioTranscriptionQualityEvaluator
{
    AudioTranscriptionQualityAssessment Evaluate(AudioTranscriptionResult transcription);
}

public sealed class AudioTranscriptionQualityEvaluator : IAudioTranscriptionQualityEvaluator
{
    private readonly AudioTranscriptionQualityOptions _options;

    public AudioTranscriptionQualityEvaluator(AudioTranscriptionQualityOptions options)
    {
        _options = options;
    }

    public AudioTranscriptionQualityAssessment Evaluate(AudioTranscriptionResult transcription)
    {
        if (!_options.Enabled)
        {
            return new AudioTranscriptionQualityAssessment(
                AudioTranscriptionReliability.Reliable,
                1m,
                ShouldAccept(AudioTranscriptionReliability.Reliable),
                "quality_gate_disabled",
                []);
        }

        var text = transcription.Text?.Trim() ?? string.Empty;
        var flags = new List<string>();

        if (string.IsNullOrWhiteSpace(text))
        {
            flags.Add("empty_text");
            return Build(AudioTranscriptionReliability.Unreliable, 0m, flags);
        }

        var alphaNumericCount = text.Count(char.IsLetterOrDigit);
        if (alphaNumericCount < _options.MinimumLettersOrDigits)
            flags.Add("too_few_letters_or_digits");

        if (transcription.Segments.Count == 0)
            return EvaluateWithoutSegments(text, transcription.Duration, flags);

        var averageLogProbability = transcription.Segments.Average(s => s.AverageLogProbability);
        var maxNoSpeechProbability = transcription.Segments.Max(s => s.NoSpeechProbability);
        var maxCompressionRatio = transcription.Segments.Max(s => s.CompressionRatio);

        if (averageLogProbability <= _options.UnreliableAverageLogProbabilityThreshold)
            flags.Add("very_low_average_log_probability");
        else if (averageLogProbability <= _options.AmbiguousAverageLogProbabilityThreshold)
            flags.Add("low_average_log_probability");

        if (maxNoSpeechProbability >= _options.UnreliableNoSpeechProbabilityThreshold)
            flags.Add("very_high_no_speech_probability");
        else if (maxNoSpeechProbability >= _options.AmbiguousNoSpeechProbabilityThreshold)
            flags.Add("high_no_speech_probability");

        if (maxCompressionRatio >= _options.UnreliableCompressionRatioThreshold)
            flags.Add("very_high_compression_ratio");
        else if (maxCompressionRatio >= _options.AmbiguousCompressionRatioThreshold)
            flags.Add("high_compression_ratio");

        var reliability = ResolveReliability(flags);
        var score = CalculateScore(averageLogProbability, maxNoSpeechProbability, maxCompressionRatio);
        return Build(reliability, score, flags);
    }

    private AudioTranscriptionQualityAssessment EvaluateWithoutSegments(
        string text,
        TimeSpan? duration,
        List<string> flags)
    {
        flags.Add("missing_audio_segments");

        if (text.Length < _options.MinimumCharactersWithoutSignals)
            flags.Add("short_text_without_segments");

        if (duration.HasValue && duration.Value.TotalSeconds < _options.MinimumDurationWithoutSignalsSeconds)
            flags.Add("short_audio_without_segments");

        var reliability = flags.Count > 1
            ? AudioTranscriptionReliability.Ambiguous
            : AudioTranscriptionReliability.Reliable;

        return Build(reliability, reliability == AudioTranscriptionReliability.Reliable ? 0.75m : 0.45m, flags);
    }

    private AudioTranscriptionQualityAssessment Build(
        AudioTranscriptionReliability reliability,
        decimal confidenceScore,
        IReadOnlyList<string> flags)
    {
        var reason = flags.Count == 0 ? "ok" : string.Join(",", flags);
        return new AudioTranscriptionQualityAssessment(
            reliability,
            confidenceScore,
            ShouldAccept(reliability),
            reason,
            flags);
    }

    private bool ShouldAccept(AudioTranscriptionReliability reliability) =>
        reliability == AudioTranscriptionReliability.Reliable
        || (_options.AcceptAmbiguousTranscriptions && reliability == AudioTranscriptionReliability.Ambiguous);

    private static AudioTranscriptionReliability ResolveReliability(IReadOnlyList<string> flags)
    {
        if (flags.Any(flag => flag.StartsWith("very_", StringComparison.OrdinalIgnoreCase)))
            return AudioTranscriptionReliability.Unreliable;

        return flags.Count > 0
            ? AudioTranscriptionReliability.Ambiguous
            : AudioTranscriptionReliability.Reliable;
    }

    private decimal CalculateScore(
        double averageLogProbability,
        double maxNoSpeechProbability,
        double maxCompressionRatio)
    {
        var logProbabilityScore = Normalize(
            averageLogProbability,
            _options.UnreliableAverageLogProbabilityThreshold,
            0);
        var speechScore = 1 - Math.Clamp(maxNoSpeechProbability, 0, 1);
        var compressionScore = 1 - Normalize(
            maxCompressionRatio,
            1,
            _options.UnreliableCompressionRatioThreshold);

        var score = Math.Min(logProbabilityScore, Math.Min(speechScore, compressionScore));
        return Math.Round((decimal)Math.Clamp(score, 0, 1), 2);
    }

    private static double Normalize(double value, double min, double max)
    {
        if (Math.Abs(max - min) < double.Epsilon)
            return 0;

        return Math.Clamp((value - min) / (max - min), 0, 1);
    }
}