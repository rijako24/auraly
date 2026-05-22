using System.Text.Json;

using MimosBabySpa.Application.Services;

using MimosBabySpa.Domain.Models;

using static MimosBabySpa.Application.Agents.ToolSideEffectNames;



namespace MimosBabySpa.Application.Agents.Tools.Impl;



public sealed class EscalateToHumanTool : IAgentTool

{

    private readonly IEscalationNotifier _escalationNotifier;



    public EscalateToHumanTool(IEscalationNotifier escalationNotifier) =>

        _escalationNotifier = escalationNotifier;



    public string Name => "escalate_to_human";



    public string Description =>

        "Transfers the conversation to a human agent when the customer requests it or after repeated failures.";



    public string ParametersSchema => """

        {

          "type": "object",

          "properties": {

            "reason": { "type": "string" },

            "last_user_message": { "type": "string" }

          },

          "required": ["reason"]

        }

        """;



    public async Task<string> ExecuteAsync(

        JsonElement arguments,

        AgentToolContext ctx,

        CancellationToken cancellationToken = default)

    {

        ToolResultHelper.TryGetString(arguments, "reason", out var reason);

        ToolResultHelper.TryGetString(arguments, "last_user_message", out var lastUserMessage);



        ctx.ConversationState.Owner = ConversationOwner.Human;

        ctx.ConversationState.LastEscalatedAt = DateTime.UtcNow;



        var contactPhone = ConversationContactPhone.Resolve(ctx.Facts, ctx.ChannelPhone) ?? string.Empty;



        if (ctx.EscalationContacts.Count > 0)

        {

            try

            {

                var notification = new EscalationNotification(

                    ConversationId: ctx.ConversationId,

                    CustomerPhone: contactPhone,

                    Reason: reason ?? "agent_request",

                    LastUserMessage: lastUserMessage,

                    PaymentReferenceId: ctx.ActivePayment?.PaymentReferenceId);



                await _escalationNotifier.NotifyAsync(

                    ctx.BusinessId, ctx.EscalationContacts, notification, cancellationToken);

            }

            catch

            {

                // Fallo de notificación no revierte Owner=Human

            }

        }



        return ToolResultHelper.Ok(new

        {

            escalated = true,

            reason,

            message = "The conversation has been transferred to a human agent."

        }, EscalatedToHuman);

    }

}


