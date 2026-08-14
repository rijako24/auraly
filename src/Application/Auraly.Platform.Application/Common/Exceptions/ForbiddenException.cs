namespace Auraly.Platform.Application.Common.Exceptions;

public class ForbiddenException : Exception
{
    public ForbiddenException(string message = "No tiene permisos para realizar esta acción.")
        : base(message) { }
}
