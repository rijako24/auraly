using MimosBabySpa.Application.Common.DTOs;
using MimosBabySpa.Application.Common.Exceptions;
using MimosBabySpa.Application.Identity.DTOs;
using MimosBabySpa.Application.Identity.Interfaces;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Identity.Services;

public class PaymentAdminService : IPaymentAdminService
{
    private readonly IUnitOfWork _unitOfWork;

    public PaymentAdminService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
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

    private static PaymentTransactionDto MapToDto(PaymentTransaction t) =>
        new(
            t.PaymentTransactionId, t.BusinessId, t.ConversationId,
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
