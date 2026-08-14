using Microsoft.Extensions.Logging;

namespace Auraly.Platform.Application.Services;

public sealed class ExternalEscalationExpirationProcess : ITimedProcess
{
    public const string ProcessName = "external_escalation_expiration";

    private readonly IExternalEscalationService _escalations;
    private readonly IExternalEscalationOutcomePublisher _outcomes;
    private readonly ILogger<ExternalEscalationExpirationProcess> _logger;

    public ExternalEscalationExpirationProcess(
        IExternalEscalationService escalations,
        IExternalEscalationOutcomePublisher outcomes,
        ILogger<ExternalEscalationExpirationProcess> logger)
    {
        _escalations = escalations;
        _outcomes = outcomes;
        _logger = logger;
    }

    public string Name => ProcessName;

    public async Task RunAsync(CancellationToken ct = default)
    {
        try
        {
            await _escalations.ProcessExpiredAttemptsAsync(ct);
            await _outcomes.PublishPendingAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ExternalEscalationExpirationProcess: error procesando escalamientos vencidos");
        }
    }
}
