namespace Auraly.Platform.Application.Common.Exceptions;

public class NotFoundException : Exception
{
    public NotFoundException(string entityName, object key)
        : base($"{entityName} con identificador '{key}' no fue encontrado.") { }
}
