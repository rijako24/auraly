using Microsoft.Extensions.DependencyInjection;

namespace MimosBabySpa.Application.Agents.Operations.Internal;

public static class InternalOperationRegistration
{
    public static IServiceCollection AddInternalAgentOperations(this IServiceCollection services)
    {
        services.AddScoped<IAgentOperation, GetReservationsOperation>();
        services.AddScoped<IAgentOperation, BlockAvailabilityOperation>();
        services.AddScoped<IAgentOperation, RequestRescheduleOperation>();
        services.AddScoped<IAgentOperation, GetBusinessMetricsOperation>();
        services.AddScoped<IAgentOperation, GetCustomerHistoryOperation>();
        services.AddScoped<IAgentOperation, SearchOrderOperation>();
        services.AddScoped<IAgentOperation, AcceptOrderRequestOperation>();
        services.AddScoped<IAgentOperation, RejectOrderRequestOperation>();
        services.AddScoped<IAgentOperation, ConfirmManualPaymentOperation>();
        services.AddScoped<IAgentOperation, SearchManualPaymentsOperation>();
        return services;
    }
}
