using Azure.AI.OpenAI;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Services;
using OpenAI.Audio;

namespace MimosBabySpa.Infrastructure.Services;

/// <summary>
/// Servicio de transcripción de audio (Whisper).
/// La generación de respuestas chat fue migrada a AzureOpenAIChatClient + AgentConversationService.
/// </summary>
public class AIService : IAIService
{
    private readonly AzureOpenAIClient _audioClient;
    private readonly string _audioDeploymentName;
    private readonly ILogger<AIService> _logger;

    public AIService(
        AzureOpenAIClient audioClient,
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

            var audioClient = _audioClient.GetAudioClient(_audioDeploymentName);
            using var transcriptionStream = new MemoryStream(audioBytes);
            var response = await audioClient.TranscribeAudioAsync(
                transcriptionStream,
                GetAudioFileName(mimeType),
                new AudioTranscriptionOptions
                {
                    ResponseFormat = AudioTranscriptionFormat.Verbose,
                    Language = "es"
                });

            var transcription = response.Value;
            var segments = transcription.Segments
                .Select(segment => new AudioTranscriptionSegmentSignal(
                    segment.Text,
                    segment.StartTime,
                    segment.EndTime,
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

    private static string GetAudioFileName(string mimeType) => mimeType.ToLowerInvariant() switch
    {
        "audio/ogg" or "audio/opus" => "audio.ogg",
        "audio/mp4" or "audio/m4a" => "audio.m4a",
        "audio/mpeg" or "audio/mp3" => "audio.mp3",
        "audio/wav" or "audio/x-wav" => "audio.wav",
        "audio/webm" => "audio.webm",
        _ => "audio.ogg"
    };}
