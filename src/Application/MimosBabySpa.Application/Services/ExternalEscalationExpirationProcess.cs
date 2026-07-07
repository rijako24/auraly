using Microsoft.Extensions.Logging;

namespace MimosBabySpa.Application.Services;

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
            var expiredAttempts = await _escalations.ProcessExpiredAttemptsAsync(ct);
            foreach (var expired in expiredAttempts)
            {
                await _outcomes.PublishAsync(
                    expired.BusinessId,
                    expired.AttemptId,
                    expired.OutcomeKey,
                    expired.Payload,
                    ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ExternalEscalationExpirationProcess: error procesando escalamientos vencidos");
        }
    }
}
