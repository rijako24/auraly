using Azure.AI.OpenAI;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Services;

namespace MimosBabySpa.Infrastructure.Services;

/// <summary>
/// Servicio de transcripción de audio (Whisper).
/// La generación de respuestas chat fue migrada a AzureOpenAIChatClient + AgentConversationService.
/// </summary>
public class AIService : IAIService
{
    private readonly OpenAIClient _audioClient;
    private readonly string _audioDeploymentName;
    private readonly ILogger<AIService> _logger;

    public AIService(
        OpenAIClient audioClient,
        string audioDeploymentName,
        ILogger<AIService> logger)
    {
        _audioClient = audioClient ?? throw new ArgumentNullException(nameof(audioClient));
        _audioDeploymentName = audioDeploymentName;
        _logger = logger;
    }

    public async Task<AudioTranscriptionResult> TranscribeAudioAsync(Stream audioStream, string mimeType)
    {
        try
        {
            _logger.LogInformation("Transcribiendo audio con tipo MIME: {MimeType}", mimeType);

            using var memoryStream = new MemoryStream();
            await audioStream.CopyToAsync(memoryStream);
            var audioBytes = memoryStream.ToArray();

            var response = await _audioClient.GetAudioTranscriptionAsync(
                new AudioTranscriptionOptions(_audioDeploymentName, BinaryData.FromBytes(audioBytes))
                {
                    ResponseFormat = AudioTranscriptionFormat.Verbose,
                    Language = "es"
                });

            var transcription = response.Value;
            var segments = transcription.Segments
                .Select(segment => new AudioTranscriptionSegmentSignal(
                    segment.Text,
                    segment.Start,
                    segment.End,
                    segment.AverageLogProbability,
                    segment.NoSpeechProbability,
                    segment.CompressionRatio))
                .ToList();

            _logger.LogInformation("Audio transcrito exitosamente. Duration={DurationSeconds}s, Segments={SegmentCount}",
                transcription.Duration?.TotalSeconds,
                segments.Count);

            return new AudioTranscriptionResult(transcription.Text, transcription.Duration, segments);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al transcribir audio");
            throw;
        }
    }
}
