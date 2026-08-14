using System.Text.Json;
using Auraly.Platform.Application.Common.DTOs;
using Auraly.Platform.Application.Common.Exceptions;
using Auraly.Platform.Application.Identity.DTOs;
using Auraly.Platform.Application.Identity.Interfaces;
using Auraly.Platform.Application.Services;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Enums;
using Auraly.Platform.Domain.Repositories;

namespace Auraly.Platform.Application.Identity.Services;

public class PaymentAdminService : IPaymentAdminService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPaymentConfirmationHandler _paymentConfirmation;

    public PaymentAdminService(IUnitOfWork unitOfWork, IPaymentConfirmationHandler paymentConfirmation)
    {
        _unitOfWork = unitOfWork;
        _paymentConfirmation = paymentConfirmation;
    }

    public async Task<PagedResponse<PaymentTransactionDto>> GetPagedByBusinessIdAsync(
        Guid tenantId, Guid businessId, PagedRequest request,
        Domain.Enums.PaymentTransactionStatus? status, CancellationToken ct)
    {
        await EnsureBusinessBelongsToTenantAsync(tenantId, businessId, ct);

        var (items, totalCount) = await _unitOfWork.PaymentTransactions.GetPagedByBusinessIdAsync(
            businessId, request.Page, request.PageSize, request.Search, status, ct);

        return new PagedResponse<PaymentTransactionDto>(
            items.Select(MapToDto).ToList(), totalCount, request.Page, request.PageSize);
    }

    public async Task<PaymentTransactionDto> GetByIdAsync(Guid tenantId, Guid paymentTransactionId, CancellationToken ct)
    {
        var tx = await _unitOfWork.PaymentTransactions.GetByIdAsync(paymentTransactionId)
            ?? throw new NotFoundException(nameof(PaymentTransaction), paymentTransactionId);

        await EnsureBusinessBelongsToTenantAsync(tenantId, tx.BusinessId, ct);
        return MapToDto(tx);
    }

    public async Task<PaymentTransactionDto> ConfirmManualAsync(
        Guid tenantId,
        Guid adminUserId,
        Guid paymentTransactionId,
        CancellationToken ct)
    {
        var tx = await _unitOfWork.PaymentTransactions.GetByIdAsync(paymentTransactionId, ct)
            ?? throw new NotFoundException(nameof(PaymentTransaction), paymentTransactionId);

        await EnsureBusinessBelongsToTenantAsync(tenantId, tx.BusinessId, ct);

        if (tx.Status != PaymentTransactionStatus.Created)
            throw new DomainValidationException("Payment", "Solo se pueden confirmar manualmente pagos pendientes.");

        if (tx.ExpiresAt.HasValue && tx.ExpiresAt.Value <= DateTime.UtcNow)
            throw new DomainValidationException("Payment", "No se puede confirmar manualmente un pago vencido.");

        if (tx.AmountInCents <= 0 || string.IsNullOrWhiteSpace(tx.PaymentReferenceId))
            throw new DomainValidationException("Payment", "La transaccion no tiene datos de pago validos.");

        var payload = JsonSerializer.Serialize(new
        {
            source = "admin_manual",
            admin_user_id = adminUserId,
            confirmed_at = DateTime.UtcNow
        });

        var result = await _paymentConfirmation.HandleAsync(
            tx.PaymentReferenceId,
            $"manual:{adminUserId:N}",
            tx.AmountInCents,
            payload,
            ct,
            PaymentTransactionSource.Manual);

        if (!result.Success)
            throw new DomainValidationException("Payment", result.ErrorMessage ?? "No se pudo confirmar el pago manualmente.");

        var updated = await _unitOfWork.PaymentTransactions.GetByIdAsync(paymentTransactionId, ct)
            ?? throw new NotFoundException(nameof(PaymentTransaction), paymentTransactionId);
        return MapToDto(updated);
    }

    private static PaymentTransactionDto MapToDto(PaymentTransaction t) =>
        new(
            t.PaymentTransactionId, t.BusinessId, t.ConversationId,
            t.ReservationId,
            t.PaymentReferenceId, t.ProviderTransactionId, t.AmountInCents,
            t.Currency, t.Status.ToString(), t.Source.ToString(),
            t.CreatedAt, t.ConfirmedAt);

    private async Task EnsureBusinessBelongsToTenantAsync(Guid tenantId, Guid businessId, CancellationToken ct)
    {
        var business = await _unitOfWork.Businesses.GetByIdAsync(businessId)
            ?? throw new NotFoundException(nameof(Business), businessId);
        if (business.TenantId != tenantId)
            throw new NotFoundException(nameof(Business), businessId);
    }
}
