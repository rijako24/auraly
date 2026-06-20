using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Services;

namespace MimosBabySpa.API.Functions;

public sealed class ExternalEscalationAttemptPollerFunction
{
    private readonly IExternalEscalationService _escalations;
    private readonly ILogger<ExternalEscalationAttemptPollerFunction> _logger;

    public ExternalEscalationAttemptPollerFunction(
        IExternalEscalationService attempts,
        ILogger<ExternalEscalationAttemptPollerFunction> logger)
    {
        _escalations = attempts;
        _logger = logger;
    }

    [Function("ExternalEscalationAttemptPoller")]
    public async Task Run([TimerTrigger("0 */1 * * * *")] TimerInfo timerInfo, CancellationToken ct)
    {
        try
        {
            await _escalations.ProcessExpiredAttemptsAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ExternalEscalationAttemptPoller: error procesando escalamientos vencidas");
        }
    }
}
