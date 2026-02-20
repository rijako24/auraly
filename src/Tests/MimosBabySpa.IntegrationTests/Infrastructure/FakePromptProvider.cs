using MimosBabySpa.Application.Prompts;

namespace MimosBabySpa.IntegrationTests.Infrastructure;

/// <summary>
/// Returns a minimal system prompt so the orchestrator has a non-null value
/// to pass to the LLM. The FakeLLMAdapter ignores it entirely.
/// </summary>
public class FakePromptProvider : IPromptProvider
{
    public Task<string> BuildAsync(
        SystemPromptInput input,
        CancellationToken cancellationToken = default) =>
        Task.FromResult("Eres un asistente de pruebas de MimosBabySpa. Responde de forma natural.");
}
