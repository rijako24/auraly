using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;
using Xunit;

namespace MimosBabySpa.Tests.Services;

public sealed class MessageSequenceResolverTests
{
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IMediaUrlResolver> _mediaUrlResolver = new();
    private readonly MessageSequenceResolver _resolver;

    public MessageSequenceResolverTests()
    {
        _resolver = new MessageSequenceResolver(
            _unitOfWork.Object,
            _mediaUrlResolver.Object,
            NullLogger<MessageSequenceResolver>.Instance);
    }

    [Fact]
    public async Task ResolveAsync_UnknownSequence_ReturnsEmpty()
    {
        var result = await _resolver.ResolveAsync(
            Guid.NewGuid(),
            "missing",
            new MessageSequenceCatalog(),
            new MessageSequenceContext(),
            CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ResolveAsync_TextStep_ExpandsReservationPlaceholders()
    {
        var businessId = Guid.NewGuid();
        var catalog = new MessageSequenceCatalog
        {
            ["reservation_confirmed"] = new MessageSequence
            {
                Messages =
                [
                    new MessageSequenceStep
                    {
                        Body = "Hola {CustomerName}, tu cita {Service} el {Date} a las {Time}."
                    }
                ]
            }
        };

        SetupEmptyAttachments(businessId);
        SetupServices(businessId);

        var reservation = new Reservation
        {
            ReservationId = Guid.NewGuid(),
            CustomerNameSnapshot = "Ana",
            ReservationDateTime = new DateTime(2026, 6, 10, 9, 0, 0),
            Service = new Service { ServiceName = "Plan Marineritos", Price = 100000 }
        };

        _unitOfWork.Setup(u => u.Reservations.GetByIdAsync(reservation.ReservationId))
            .ReturnsAsync(reservation);

        var result = await _resolver.ResolveAsync(
            businessId,
            "reservation_confirmed",
            catalog,
            new MessageSequenceContext { Reservation = reservation },
            CancellationToken.None);

        result.Should().HaveCount(1);
        result[0].Body.Should().Contain("Ana");
        result[0].Body.Should().Contain("Plan Marineritos");
        result[0].Body.Should().Contain("10/06/2026");
        result[0].Body.Should().Contain("09:00");
    }

    [Fact]
    public async Task ResolveAsync_WithAttachment_ResolvesMediaUrl()
    {
        var businessId = Guid.NewGuid();
        var attachmentId = Guid.NewGuid();
        var catalog = new MessageSequenceCatalog
        {
            ["reservation_docs"] = new MessageSequence
            {
                Messages =
                [
                    new MessageSequenceStep
                    {
                        Body = "Indicaciones",
                        AttachmentId = attachmentId
                    }
                ]
            }
        };

        _unitOfWork.Setup(u => u.BusinessAttachments.GetByBusinessIdAsync(businessId))
            .ReturnsAsync([
                new BusinessAttachment
                {
                    BusinessAttachmentId = attachmentId,
                    BusinessId = businessId,
                    BlobPath = "confirmations/guia.pdf",
                    MediaType = "document",
                    Filename = "guia.pdf",
                    IsActive = true
                }
            ]);

        SetupServices(businessId);

        _mediaUrlResolver.Setup(m => m.ResolveAsync(businessId, "confirmations/guia.pdf", It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://blob.example/guia.pdf?sas=1");

        var result = await _resolver.ResolveAsync(
            businessId,
            "reservation_docs",
            catalog,
            new MessageSequenceContext(),
            CancellationToken.None);

        result.Should().HaveCount(1);
        result[0].MediaUrl.Should().Be("https://blob.example/guia.pdf?sas=1");
        result[0].MediaType.Should().Be("document");
        result[0].Filename.Should().Be("guia.pdf");
    }

    private void SetupEmptyAttachments(Guid businessId)
    {
        _unitOfWork.Setup(u => u.BusinessAttachments.GetByBusinessIdAsync(businessId))
            .ReturnsAsync(Array.Empty<BusinessAttachment>());
    }

    private void SetupServices(Guid businessId)
    {
        _unitOfWork.Setup(u => u.Services.GetActiveByBusinessIdAsync(businessId))
            .ReturnsAsync(Array.Empty<Service>());
        _unitOfWork.Setup(u => u.ServiceAddOnRules.GetByBusinessIdAsync(businessId))
            .ReturnsAsync(Array.Empty<ServiceAddOnRule>());
    }
}
