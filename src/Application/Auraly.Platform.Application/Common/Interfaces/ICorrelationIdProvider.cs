namespace Auraly.Platform.Application.Common.Interfaces;

public interface ICorrelationIdProvider
{
    string CorrelationId { get; }
    void Set(string correlationId);
}
