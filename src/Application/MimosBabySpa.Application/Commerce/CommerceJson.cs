using System.Text.Json;

namespace MimosBabySpa.Application.Commerce;

public static class CommerceJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
