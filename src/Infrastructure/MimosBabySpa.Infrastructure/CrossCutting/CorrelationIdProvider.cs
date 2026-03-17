using MimosBabySpa.Application.Common.Interfaces;

namespace MimosBabySpa.Infrastructure.CrossCutting;

public class CorrelationIdProvider : ICorrelationIdProvider
{
    private string _correlationId = Guid.NewGuid().ToString("N");

    public string CorrelationId => _correlationId;

    public void Set(string correlationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        _correlationId = correlationId;
    }
}
