using MimosBabySpa.Application.Common.DTOs;
using MimosBabySpa.Application.Identity.DTOs;
using MimosBabySpa.Domain.Enums;

namespace MimosBabySpa.Application.Identity.Interfaces;

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
