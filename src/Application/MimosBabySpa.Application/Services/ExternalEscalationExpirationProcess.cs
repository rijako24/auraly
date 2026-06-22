using Microsoft.Extensions.Logging;

namespace MimosBabySpa.Application.Services;

public sealed class ExternalEscalationExpirationProcess : ITimedProcess
{
    private readonly IExternalEscalationService _escalations;
    private readonly ILogger<ExternalEscalationExpirationProcess> _logger;

    public ExternalEscalationExpirationProcess(
        IExternalEscalationService escalations,
        ILogger<ExternalEscalationExpirationProcess> logger)
    {
        _escalations = escalations;
        _logger = logger;
    }

    public string Name => "external_escalation_expiration";

    public async Task RunAsync(CancellationToken ct = default)
    {
        try
        {
            await _escalations.ProcessExpiredAttemptsAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ExternalEscalationExpirationProcess: error procesando escalamientos vencidos");
        }
    }
}
