namespace MimosBabySpa.Domain.Enums;

public enum FlowNodeType
{
    Start = 0,
    CollectFields = 1,
    Action = 2,
    LLMClassify = 3,
    GenerateResponse = 4,
    WaitForEvent = 5,
    Escalate = 6,
    End = 7,

    /// <summary>
    /// Routes the flow based on boolean intention flags detected during extraction.
    /// Zero LLM calls — reads ctx.DetectedIntentions and routes by matching port.
    /// Config: { "routes": [{ "when": "intent_key", "port": "portId" }], "defaultPort": "string" }
    /// </summary>
    IntentionRouter = 9
}
