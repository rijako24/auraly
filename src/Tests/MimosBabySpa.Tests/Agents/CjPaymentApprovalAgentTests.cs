using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using FluentAssertions;
using Moq;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Agents.Operations;
using MimosBabySpa.Application.Agents.Operations.Internal;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

public sealed class CjPaymentApprovalAgentTests
{
    [Fact]
    public void PaymentApprovalSeed_CompilesAsExclusivePaymentAgent()
    {
        var config = LoadSeedConfig("SeedCJPaymentApprovalAgent.sql", "SettingsJson");
        var compiler = new AgentConfigurationCompiler(new AgentOperationRegistry(
        [
            new OperationStub("internal.search_manual_payments", "payment.single_pending", "payment.multiple_pending", "payment.none_pending"),
            new OperationStub("internal.confirm_manual_payment", "payment.confirmed", "payment.already_confirmed", "payment.not_found", "payment.ambiguous", "payment.not_manual", "payment.not_pending", "payment.expired", "payment.confirmation_failed", "input.invalid")
        ]));

        var compilation = compiler.Compile(config);

        compilation.IsValid.Should().BeTrue(string.Join("; ", compilation.Diagnostics.Select(x => x.Message)));
        config.InteractiveActions["manual_payment"]["confirm"].Operation
            .Should().Be("internal.confirm_manual_payment");
        config.Flows.SelectMany(flow => flow.Stages).SelectMany(stage => stage.Actions)
            .Select(action => action.Operation)
            .Should().OnlyContain(operation => operation == "internal.search_manual_payments"
                || operation == "internal.confirm_manual_payment");
        config.Policies.Should().Contain("Tu unica funcion").And.Contain("Nunca confirmes un pago ambiguo");
    }

    [Fact]
    public void CjCommercialSeed_NotifiesConfiguredApproverWithPaymentSpecificButton()
    {
        var config = LoadSeedConfig("SeedCJDistribuciones.sql", "SettingsJson");

        config.Notifications["manual_payment_requested"].Enabled.Should().BeTrue();
        config.Notifications["manual_payment_requested"].Deliveries.Should().ContainSingle();
        var delivery = config.Notifications["manual_payment_requested"].Deliveries.Single();
        delivery.Id.Should().Be("internal");
        delivery.Recipients.Should().Equal("inbound:payment_approver");
        var button = config.MessageSequences["manual_payment_approval_request"].Messages.Single().Buttons.Single();
        button.Id.Should().Be("manual_payment:confirm:{payment_transaction_id}");
        button.Title.Should().Be("Confirmar pago");
    }

    [Fact]
    public async Task ConfirmButton_WithSeveralPendingPayments_ConfirmsOnlyPayloadPaymentId()
    {
        var businessId = Guid.NewGuid();
        var selected = PendingPayment(businessId, "PED-001");
        var other = PendingPayment(businessId, "PED-002");
        var (operation, payments, confirmation) = CreateOperation(selected, other);
        confirmation
            .Setup(service => service.HandleAsync(
                selected.PaymentReferenceId,
                It.IsAny<string>(),
                selected.AmountInCents,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>(),
                PaymentTransactionSource.Manual))
            .Callback(() =>
            {
                selected.Status = PaymentTransactionStatus.Confirmed;
                selected.Source = PaymentTransactionSource.Manual;
                selected.ConfirmedAt = DateTime.UtcNow;
            })
            .ReturnsAsync(new PaymentConfirmationResult(true, null));

        using var input = JsonDocument.Parse($$"""{"payment_transaction_id":"{{selected.PaymentTransactionId}}"}""");
        var result = await operation.ExecuteAsync(input.RootElement, Context(businessId), CancellationToken.None);

        result.Code.Should().Be("payment.confirmed");
        selected.Status.Should().Be(PaymentTransactionStatus.Confirmed);
        other.Status.Should().Be(PaymentTransactionStatus.Created);
    }

    [Fact]
    public async Task SpokenConfirmation_WithOrderNumber_ResolvesAndConfirmsThatOrder()
    {
        var businessId = Guid.NewGuid();
        var first = PendingPayment(businessId, "PED-001");
        var selected = PendingPayment(businessId, "PED-002");
        var (operation, _, confirmation) = CreateOperation(first, selected);
        confirmation
            .Setup(service => service.HandleAsync(
                selected.PaymentReferenceId,
                It.IsAny<string>(),
                selected.AmountInCents,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>(),
                PaymentTransactionSource.Manual))
            .Callback(() => selected.Status = PaymentTransactionStatus.Confirmed)
            .ReturnsAsync(new PaymentConfirmationResult(true, null));

        using var input = JsonDocument.Parse("""{"query":"PED-002"}""");
        var result = await operation.ExecuteAsync(input.RootElement, Context(businessId), CancellationToken.None);

        result.Code.Should().Be("payment.confirmed");
        selected.Status.Should().Be(PaymentTransactionStatus.Confirmed);
        first.Status.Should().Be(PaymentTransactionStatus.Created);
    }

    [Fact]
    public async Task UnidentifiedConfirmation_DoesNotConfirmAndSearchReturnsSingleForReview()
    {
        var businessId = Guid.NewGuid();
        var pending = PendingPayment(businessId, "PED-001");
        var (confirmOperation, unitOfWork, confirmation) = CreateOperation(pending);
        var searchOperation = new SearchManualPaymentsOperation(unitOfWork.Object);

        using var empty = JsonDocument.Parse("{}");
        var unsafeConfirmation = await confirmOperation.ExecuteAsync(empty.RootElement, Context(businessId), CancellationToken.None);
        var review = await searchOperation.ExecuteAsync(empty.RootElement, Context(businessId), CancellationToken.None);

        unsafeConfirmation.Code.Should().Be("input.invalid");
        review.Code.Should().Be("payment.single_pending");
        review.Data.GetProperty("selected_payment_transaction_id").GetString()
            .Should().Be(pending.PaymentTransactionId.ToString());
        confirmation.VerifyNoOtherCalls();
    }

    private static (ConfirmManualPaymentOperation Operation, Mock<IUnitOfWork> UnitOfWork, Mock<IPaymentConfirmationHandler> Confirmation)
        CreateOperation(params PaymentTransaction[] payments)
    {
        var repository = new Mock<IPaymentTransactionRepository>();
        repository
            .Setup(value => value.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => payments.SingleOrDefault(payment => payment.PaymentTransactionId == id));
        repository
            .Setup(value => value.GetPagedByBusinessIdAsync(
                It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(),
                It.IsAny<PaymentTransactionStatus?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, int _, int _, string? _, PaymentTransactionStatus? _, CancellationToken _) =>
                ((IReadOnlyList<PaymentTransaction>)payments, payments.Length));

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.SetupGet(value => value.PaymentTransactions).Returns(repository.Object);
        var confirmation = new Mock<IPaymentConfirmationHandler>();
        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider
            .Setup(value => value.GetService(typeof(IPaymentConfirmationHandler)))
            .Returns(confirmation.Object);
        return (new ConfirmManualPaymentOperation(unitOfWork.Object, serviceProvider.Object), unitOfWork, confirmation);
    }

    private static PaymentTransaction PendingPayment(Guid businessId, string orderNumber) => new()
    {
        PaymentTransactionId = Guid.NewGuid(),
        BusinessId = businessId,
        ConversationId = Guid.NewGuid(),
        PaymentReferenceId = $"manual-order-{Guid.NewGuid():N}",
        LinkUrl = string.Empty,
        AmountInCents = 125_000,
        Currency = "COP",
        Status = PaymentTransactionStatus.Created,
        ExpiresAt = DateTime.UtcNow.AddHours(1),
        CreatedAt = DateTime.UtcNow,
        CheckoutKind = CheckoutKind.Order,
        CheckoutSnapshotJson = JsonSerializer.Serialize(new
        {
            order_number = orderNumber,
            payer_name = $"Cliente {orderNumber}",
            payment_phone = "3000000000",
            delivery_address = "Calle 1"
        })
    };

    private static OperationContext Context(Guid businessId) => new()
    {
        AgentId = Guid.NewGuid(),
        BusinessId = businessId,
        Session = new AgentConversationContext { BusinessId = businessId, ChannelPhone = "+573001112233" }
    };

    private static AgentConfig LoadSeedConfig(string seedFile, string variableName)
    {
        var path = Path.Combine(FindSolutionRoot(), "database", "MimosBabySpa.Database", "Scripts", "Seeds", seedFile);
        var sql = File.ReadAllText(path);
        var match = Regex.Match(
            sql,
            $"DECLARE\\s+@{Regex.Escape(variableName)}\\s+NVARCHAR\\(MAX\\)\\s*=\\s*N'(.*?)';",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
        match.Success.Should().BeTrue();
        return JsonSerializer.Deserialize<AgentConfig>(match.Groups[1].Value.Replace("''", "'"), new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            Converters = { new JsonStringEnumConverter() }
        })!;
    }

    private static string FindSolutionRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "MimosBabySpa.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate MimosBabySpa.sln.");
    }

    private sealed class OperationStub : IAgentOperation
    {
        public OperationStub(string id, params string[] outcomes) => Descriptor = new(
            id,
            "{\"type\":\"object\",\"required\":[]}",
            outcomes,
            [],
            [],
            []);

        public OperationDescriptor Descriptor { get; }

        public Task<OperationOutcome> ExecuteAsync(JsonElement input, OperationContext context, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
    }
}
