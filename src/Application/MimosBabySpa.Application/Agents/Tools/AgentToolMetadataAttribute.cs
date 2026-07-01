namespace MimosBabySpa.Application.Agents.Tools;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class AgentToolMetadataAttribute : Attribute
{
    public AgentToolMetadataAttribute(string name)
    {
        Name = name;
    }

    public string Name { get; }

    public string[] Capabilities { get; init; } = [];

    public string[] RequiredTemplateIds { get; init; } = [];
}
