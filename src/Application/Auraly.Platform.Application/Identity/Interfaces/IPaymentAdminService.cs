using Auraly.Platform.Application.Common.DTOs;
using Auraly.Platform.Application.Identity.DTOs;
using Auraly.Platform.Domain.Enums;

namespace Auraly.Platform.Application.Identity.Interfaces;

public interface IPaymentAdminService
{
    Task<PagedResponse<PaymentTransactionDto>> GetPagedByBusinessIdAsync(
        Guid tenantId, Guid businessId, PagedRequest request,
        PaymentTransactionStatus? status = null, CancellationToken ct = default);

    Task<PaymentTransactionDto> GetByIdAsync(
        Guid tenantId, Guid paymentTransactionId, CancellationToken ct = default);

    Task<PaymentTransactionDto> ConfirmManualAsync(
        Guid tenantId, Guid adminUserId, Guid paymentTransactionId, CancellationToken ct = default);
}
