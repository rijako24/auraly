namespace MimosBabySpa.Application.Common.Exceptions;

public class DomainValidationException : Exception
{
    public IReadOnlyDictionary<string, string[]> Errors { get; }

    public DomainValidationException(IDictionary<string, string[]> errors)
        : base("Se encontraron uno o más errores de validación.")
    {
        Errors = new Dictionary<string, string[]>(errors);
    }

    public DomainValidationException(string field, string message)
        : this(new Dictionary<string, string[]> { { field, new[] { message } } }) { }
}
