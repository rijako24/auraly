using Microsoft.AspNetCore.Authorization;

namespace MimosBabySpa.WebAPI.Authorization;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true)]
public class PermissionAuthorizeAttribute : AuthorizeAttribute
{
    public const string PolicyPrefix = "Permission_";
    public string Permission { get; }

    public PermissionAuthorizeAttribute(string permission)
        : base($"{PolicyPrefix}{permission}")
    {
        Permission = permission;
    }
}
