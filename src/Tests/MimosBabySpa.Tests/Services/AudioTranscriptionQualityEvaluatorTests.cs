using FluentAssertions;
using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Application.Services;
using Xunit;

namespace MimosBabySpa.Tests.Services;

public sealed class AudioTranscriptionQualityEvaluatorTests
{
    [Fact]
    public void Evaluate_AcceptsReliableAudio()
    {
        var evaluator = new AudioTranscriptionQualityEvaluator(new AudioTranscriptionQualityOptions());
        var transcription = BuildTranscription(
            text: "quiero reservar manana a las tres",
            averageLogProbability: -0.25,
            noSpeechProbability: 0.05,
            compressionRatio: 1.2);

        var result = evaluator.Evaluate(transcription);

        result.Reliability.Should().Be(AudioTranscriptionReliability.Reliable);
        result.ShouldAccept.Should().BeTrue();
        result.ConfidenceScore.Should().BeGreaterThan(0.70m);
    }

    [Fact]
    public void Evaluate_RejectsAudioWithHighNoSpeechProbability()
    {
        var evaluator = new AudioTranscriptionQualityEvaluator(new AudioTranscriptionQualityOptions());
        var transcription = BuildTranscription(
            text: "quiero reservar manana",
            averageLogProbability: -0.30,
            noSpeechProbability: 0.90,
            compressionRatio: 1.2);

        var result = evaluator.Evaluate(transcription);

        result.Reliability.Should().Be(AudioTranscriptionReliability.Unreliable);
        result.ShouldAccept.Should().BeFalse();
        result.Flags.Should().Contain("very_high_no_speech_probability");
    }

    [Fact]
    public void Evaluate_CanAcceptAmbiguousAudioWhenConfigured()
    {
        var evaluator = new AudioTranscriptionQualityEvaluator(new AudioTranscriptionQualityOptions
        {
            AcceptAmbiguousTranscriptions = true
        });
        var transcription = BuildTranscription(
            text: "manana a las tres",
            averageLogProbability: -0.95,
            noSpeechProbability: 0.10,
            compressionRatio: 1.2);

        var result = evaluator.Evaluate(transcription);

        result.Reliability.Should().Be(AudioTranscriptionReliability.Ambiguous);
        result.ShouldAccept.Should().BeTrue();
        result.Flags.Should().Contain("low_average_log_probability");
    }

    private static AudioTranscriptionResult BuildTranscription(
        string text,
        double averageLogProbability,
        double noSpeechProbability,
        double compressionRatio) =>
        new(
            text,
            TimeSpan.FromSeconds(3),
            [
                new AudioTranscriptionSegmentSignal(
                    text,
                    TimeSpan.Zero,
                    TimeSpan.FromSeconds(3),
                    averageLogProbability,
                    noSpeechProbability,
                    compressionRatio)
            ]);
}