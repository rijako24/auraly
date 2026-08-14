using System.Text.Json;

namespace Auraly.Platform.Application.Commerce;

public static class CommerceJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
