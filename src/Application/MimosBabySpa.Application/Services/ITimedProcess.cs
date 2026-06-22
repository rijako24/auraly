namespace MimosBabySpa.Application.Services;

public interface ITimedProcess
{
    string Name { get; }

    Task RunAsync(CancellationToken ct = default);
}
