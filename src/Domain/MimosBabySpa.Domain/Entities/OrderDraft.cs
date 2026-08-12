using MimosBabySpa.Domain.Enums;

namespace MimosBabySpa.Domain.Entities;

public class OrderDraft
{
    public Guid OrderDraftId { get; set; }
    public Guid BusinessId { get; set; }
    public Guid? AgentId { get; set; }
    public Guid ConversationId { get; set; }
    public Guid? IntegrationConnectionId { get; set; }
    public string? CommerceWarehouseCode { get; set; }
    public Guid? PaymentTransactionId { get; set; }
    public OrderSource Source { get; set; } = OrderSource.Bot;
    public OrderFulfillmentMode FulfillmentMode { get; set; } = OrderFulfillmentMode.Local;
    public string? CustomerNameSnapshot { get; set; }
    public string? CustomerEmailSnapshot { get; set; }
    public string? CustomerPhoneSnapshot { get; set; }
    public string? CustomerDocumentSnapshot { get; set; }
    public string? DeliveryAddressSnapshot { get; set; }
    public string? Notes { get; set; }
    public string Currency { get; set; } = "COP";
    public decimal Subtotal { get; set; }
    public decimal DiscountTotal { get; set; }

    public decimal Total { get; set; }
    public bool CustomerConfirmed { get; set; }
    public string? CustomAttributesJson { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public virtual Business Business { get; set; } = null!;
    public virtual Agent? Agent { get; set; }
    public virtual Conversation Conversation { get; set; } = null!;
    public virtual IntegrationConnection? IntegrationConnection { get; set; }
    public virtual PaymentTransaction? PaymentTransaction { get; set; }
    public virtual ICollection<OrderDraftItem> Items { get; set; } = new List<OrderDraftItem>();
}