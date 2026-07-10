using System.Text.Json;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Services;

namespace MimosBabySpa.Application.Agents.Tools.Impl;

/// <summary>
/// Encola una secuencia nombrada del catalogo del agente (texto + adjuntos) para envio tras la respuesta principal.
/// Capacidad generica: el stage indica que secuencia usar (p. ej. reservation_docs).
/// </summary>
[AgentToolMetadata("send_message_sequence")]
public sealed class SendMessageSequenceTool : IAgentTool
{
private readonly IMessageSequenceResolver _sequenceResolver;

    public SendMessageSequenceTool(IMessageSequenceResolver sequenceResolver)
    {
        _sequenceResolver = sequenceResolver;
    }

    public string Name => "send_message_sequence";

    public string Description =>
        "Queues a named outbound message sequence (text and optional attachments) configured for this agent. " +
        "Messages are sent after the main bot reply, in order. " +
        "Input: sequence name from messageSequences catalog.";

    public string ParametersSchema => """
        {
          "type": "object",
          "properties": {
            "sequence": {
              "type": "string",
              "description": "Name of the sequence in messageSequences (e.g. reservation_docs)"
            }
          },
          "required": ["sequence"]
        }
        """;

    public async Task<string> ExecuteAsync(
        JsonElement arguments,
        AgentToolContext ctx,
        CancellationToken cancellationToken = default)
    {
        if (!ToolResultHelper.TryGetString(arguments, "sequence", out var sequenceName)
            || string.IsNullOrWhiteSpace(sequenceName))
        {
            return ToolResultHelper.MissingPrerequisites(["sequence"]);
        }

        sequenceName = sequenceName.Trim();
        var catalog = ctx.Config?.MessageSequences;
        if (catalog is null || !catalog.ContainsKey(sequenceName))
        {
            return ToolResultHelper.Error("unknown_sequence", $"Sequence '{sequenceName}' is not configured for this agent.");
        }

        if (ctx.Turn is null)
        {
            return ToolResultHelper.Error("internal_error", "Turn context is unavailable.");
        }

        if (!ctx.Turn.TryMarkSequenceEnqueued(sequenceName))
        {
            return ToolResultHelper.Ok(new
            {
                sequence = sequenceName,
                queued = 0,
                already_queued = true
            });
        }

        var reservation = ctx.SingleManageableReservation;
        var context = new MessageSequenceContext { Reservation = reservation, Custom = ctx.Facts };

        var messages = await _sequenceResolver.ResolveAsync(
            ctx.BusinessId,
            sequenceName,
            catalog,
            context,
            cancellationToken);

        if (messages.Count == 0)
        {
            return ToolResultHelper.Ok(new
            {
                sequence = sequenceName,
                queued = 0,
                note = "Sequence resolved to zero deliverable messages."
            });
        }

        ctx.Turn.EnqueueOutbound(messages);
        ctx.Turn.MarkDirectOutboundRequested();

        return ToolResultHelper.Ok(new
        {
            sequence = sequenceName,
            queued = messages.Count
        });
    }
}
