namespace Auraly.Platform.Application.Configuration;

public sealed class AudioTranscriptionQualityOptions
{
    public const string SectionName = "WhatsApp:AudioTranscriptionQuality";

    public bool Enabled { get; set; } = true;

    public int MinimumLettersOrDigits { get; set; } = 2;

    public int MinimumCharactersWithoutSignals { get; set; } = 4;

    public double MinimumDurationWithoutSignalsSeconds { get; set; } = 1;

    public double AmbiguousAverageLogProbabilityThreshold { get; set; } = -0.85;

    public double UnreliableAverageLogProbabilityThreshold { get; set; } = -1.15;

    public double AmbiguousNoSpeechProbabilityThreshold { get; set; } = 0.45;

    public double UnreliableNoSpeechProbabilityThreshold { get; set; } = 0.75;

    public double AmbiguousCompressionRatioThreshold { get; set; } = 2.4;

    public double UnreliableCompressionRatioThreshold { get; set; } = 2.8;

    public bool EnableStructuredTranscriptRecovery { get; set; } = true;

    public int StructuredTranscriptMinimumCharacters { get; set; } = 80;

    public int StructuredTranscriptMinimumTokens { get; set; } = 12;

    public int StructuredTranscriptMinimumNumericTokens { get; set; } = 2;

    public int AmbiguousConfirmationTtlMinutes { get; set; } = 15;

    public string AmbiguousAudioReply { get; set; } =
        "Entendi: \"{{transcription}}\". Es eso lo que quisiste decir?";

    public string UnclearAudioReply { get; set; } =
        "No alcance a entender bien el audio. Me lo puedes repetir mas claro o escribirlo?";
}
