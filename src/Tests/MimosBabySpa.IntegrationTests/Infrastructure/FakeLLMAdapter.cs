using MimosBabySpa.Application.LLM;

namespace MimosBabySpa.IntegrationTests.Infrastructure;

/// <summary>
/// Script para un turno de conversación: qué JSON devuelve la extracción
/// y qué texto devuelve la generación de respuesta.
/// </summary>
public record TurnScript(
    string ExtractionJson,
    string ConversationalResponse);

/// <summary>
/// Adaptador LLM fake que devuelve respuestas scripteadas por turno.
/// SendWithJsonModeAsync → extracción (FASE 2)
/// SendMessageAsync → respuesta conversacional (FASE 5)
/// </summary>
public class FakeLLMAdapter : ILLMAdapter
{
    private readonly List<TurnScript> _scripts;
    private int _extractionIndex;
    private int _responseIndex;

    private readonly List<LLMRequest> _extractionRequests = [];
    private readonly List<LLMRequest> _responseRequests = [];

    public int ExtractionCallCount => _extractionRequests.Count;
    public int ResponseCallCount   => _responseRequests.Count;
    public IReadOnlyList<LLMRequest> ExtractionRequests => _extractionRequests.AsReadOnly();
    public IReadOnlyList<LLMRequest> ResponseRequests   => _responseRequests.AsReadOnly();

    public FakeLLMAdapter(List<TurnScript> scripts)
    {
        _scripts = scripts;
    }

    public Task<LLMResponse> SendWithJsonModeAsync(
        LLMRequest request,
        CancellationToken cancellationToken = default)
    {
        _extractionRequests.Add(request);

        var json = _extractionIndex < _scripts.Count
            ? _scripts[_extractionIndex++].ExtractionJson
            : EmptyExtractionJson();

        return Task.FromResult(new LLMResponse
        {
            Content = json,
            Success = true,
            Usage = new TokenUsage { PromptTokens = 0, CompletionTokens = 0, TotalTokens = 0 }
        });
    }

    public Task<LLMResponse> SendMessageAsync(
        LLMRequest request,
        CancellationToken cancellationToken = default)
    {
        _responseRequests.Add(request);

        var text = _responseIndex < _scripts.Count
            ? _scripts[_responseIndex++].ConversationalResponse
            : "Respuesta genérica del bot.";

        return Task.FromResult(new LLMResponse
        {
            Content = text,
            Success = true,
            Usage = new TokenUsage { PromptTokens = 0, CompletionTokens = 0, TotalTokens = 0 }
        });
    }

    private static string EmptyExtractionJson() => """
        {
          "extracted_fields": [],
          "intentions": {
            "user_requested_availability": false,
            "user_confirmed_booking": false,
            "is_information_query": false,
            "user_wants_to_cancel": false
          },
          "ambiguities": []
        }
        """;
}
