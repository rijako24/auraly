using MimosBabySpa.Domain.Enums;

namespace MimosBabySpa.Domain.Entities;

public class Order
{
    public Guid OrderId { get; set; }
    public Guid BusinessId { get; set; }
    public Guid? AgentId { get; set; }
    public Guid? ConversationId { get; set; }
    public Guid? IntegrationConnectionId { get; set; }
    public Guid? PaymentTransactionId { get; set; }
    public OrderSource Source { get; set; } = OrderSource.Bot;
    public OrderFulfillmentMode FulfillmentMode { get; set; } = OrderFulfillmentMode.Local;
    public OrderStatus Status { get; set; } = OrderStatus.Draft;
    public DeliveryAssignmentStatus DeliveryAssignmentStatus { get; set; } = DeliveryAssignmentStatus.NotRequested;
    public Guid? DeliveryExternalEscalationAttemptId { get; set; }
    public string? DeliveryAssigneeKeySnapshot { get; set; }
    public string? DeliveryAssigneeNameSnapshot { get; set; }
    public string? DeliveryAssigneeRoleSnapshot { get; set; }
    public string? DeliveryAssigneePhoneSnapshot { get; set; }
    public DateTime? DeliveryAssignmentRequestedAt { get; set; }
    public DateTime? DeliveryAssignmentAcceptedAt { get; set; }
    public DateTime? DeliveryAssignmentDeclinedAt { get; set; }
    public DateTime? DeliveryAssignmentTimedOutAt { get; set; }
    public string? CustomerNameSnapshot { get; set; }
    public string? CustomerEmailSnapshot { get; set; }
    public string? CustomerPhoneSnapshot { get; set; }
    public string? CustomerDocumentSnapshot { get; set; }
    public string? DeliveryAddressSnapshot { get; set; }
    public string? Notes { get; set; }
    public string Currency { get; set; } = "COP";
    public decimal Subtotal { get; set; }
    public decimal DiscountTotal { get; set; }
    public decimal TaxTotal { get; set; }
    public decimal Total { get; set; }
    public bool CustomerConfirmed { get; set; }
    public string? ExternalOrderId { get; set; }
    public string? ExternalDocumentNumber { get; set; }
    public string? ExternalStatus { get; set; }
    public string? IdempotencyKey { get; set; }
    public string? CustomAttributesJson { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public virtual Business Business { get; set; } = null!;
    public virtual Agent? Agent { get; set; }
    public virtual Conversation? Conversation { get; set; }
    public virtual IntegrationConnection? IntegrationConnection { get; set; }
    public virtual PaymentTransaction? PaymentTransaction { get; set; }
    public virtual ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();
}