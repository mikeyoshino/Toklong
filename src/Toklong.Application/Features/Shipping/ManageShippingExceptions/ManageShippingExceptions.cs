using MediatR;
using Toklong.Application.Abstractions;
using Toklong.Application.Common;
using Toklong.Domain.Transactions;
using static Toklong.Application.Features.Shipping.ManageShippingExceptions.ShippingTransactionLookup;

namespace Toklong.Application.Features.Shipping.ManageShippingExceptions;

public sealed record OpenCarrierExceptionCommand(
    Guid TransactionId,
    ActorRole ActorRole,
    string ActorId,
    string ReasonCode,
    string CaseReference,
    string IdempotencyKey) : IRequest;

public sealed record RecordShippingAdjustmentCommand(
    Guid TransactionId,
    Guid ManagedShipmentId,
    string Provider,
    string ProviderReference,
    long AmountSatang,
    DateTimeOffset ProviderOccurredAt,
    string CrmCaseReference,
    string ReasonCode,
    string ActorId) : IRequest;

public sealed record ResolveShippingAdjustmentCommand(
    Guid TransactionId,
    Guid AdjustmentId,
    string ResolutionCode,
    string ActorId) : IRequest;

public sealed record ResolveCarrierExceptionCommand(
    Guid TransactionId,
    TransactionState TargetState,
    string ActorId,
    string Reason,
    string CaseReference,
    string IdempotencyKey) : IRequest;

public sealed record AuthorizeShippingOperationRetryCommand(
    Guid TransactionId,
    Guid OperationId,
    string ActorId,
    string Reason,
    string ProviderOutcomeReference,
    string IdempotencyKey) : IRequest;

public sealed record OpenInsuranceCaseCommand(
    Guid TransactionId,
    Guid ManagedShipmentId,
    string Provider,
    string ProviderCaseReference,
    string ReasonCode,
    long DeclaredValueSatang,
    long ClaimedAmountSatang,
    string CrmCaseReference,
    string ActorId) : IRequest;

public sealed record ResolveInsuranceCaseCommand(
    Guid TransactionId,
    Guid InsuranceCaseId,
    string ActorId,
    string ProviderResultCode,
    string ProviderResolutionReference) : IRequest;

public sealed record AuthorizeManagedReturnCommand(
    Guid TransactionId,
    ManagedShipmentDraft Shipment,
    string ActorId,
    string CaseReference,
    string Reason,
    string IdempotencyKey) : IRequest;

public sealed record RecordTrustedReturnDeliveryCommand(
    Guid TransactionId,
    Guid ManagedShipmentId,
    string EventId,
    DateTimeOffset DeliveredAt,
    string Provider) : IRequest;

public sealed record AuthorizeManualReturnResolutionCommand(
    Guid TransactionId,
    string Reference,
    string ActorId,
    string Reason,
    string IdempotencyKey) : IRequest;

public sealed class OpenCarrierExceptionHandler(
    ITransactionRepository repository,
    IUnitOfWork unitOfWork,
    IClock clock,
    TransactionTransitionService transitions)
    : IRequestHandler<OpenCarrierExceptionCommand>
{
    public async Task Handle(
        OpenCarrierExceptionCommand request,
        CancellationToken cancellationToken)
    {
        var transaction = await GetAsync(
            repository,
            request.TransactionId,
            cancellationToken);
        transaction.OpenCarrierException(
            request.ActorRole,
            request.ActorId,
            request.ReasonCode,
            request.CaseReference,
            request.IdempotencyKey,
            clock.UtcNow,
            transitions);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

public sealed class RecordShippingAdjustmentHandler(
    ITransactionRepository repository,
    IUnitOfWork unitOfWork,
    IClock clock)
    : IRequestHandler<RecordShippingAdjustmentCommand>
{
    public async Task Handle(
        RecordShippingAdjustmentCommand request,
        CancellationToken cancellationToken)
    {
        var transaction = await GetAsync(
            repository,
            request.TransactionId,
            cancellationToken);
        transaction.RecordProviderShippingAdjustment(
            ProviderShippingAdjustment.Create(
                request.TransactionId,
                request.ManagedShipmentId,
                request.Provider,
                request.ProviderReference,
                request.AmountSatang,
                "THB",
                request.ProviderOccurredAt,
                request.CrmCaseReference,
                request.ReasonCode,
                clock.UtcNow),
            request.ActorId,
            clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

public sealed class ResolveShippingAdjustmentHandler(
    ITransactionRepository repository,
    IUnitOfWork unitOfWork,
    IClock clock)
    : IRequestHandler<ResolveShippingAdjustmentCommand>
{
    public async Task Handle(
        ResolveShippingAdjustmentCommand request,
        CancellationToken cancellationToken)
    {
        var transaction = await GetAsync(
            repository,
            request.TransactionId,
            cancellationToken);
        transaction.ResolveProviderShippingAdjustment(
            request.AdjustmentId,
            request.ActorId,
            request.ResolutionCode,
            clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

public sealed class ResolveCarrierExceptionHandler(
    ITransactionRepository repository,
    IUnitOfWork unitOfWork,
    IClock clock,
    TransactionTransitionService transitions)
    : IRequestHandler<ResolveCarrierExceptionCommand>
{
    public async Task Handle(
        ResolveCarrierExceptionCommand request,
        CancellationToken cancellationToken)
    {
        var transaction = await GetAsync(
            repository,
            request.TransactionId,
            cancellationToken);
        transaction.ResolveCarrierException(
            request.TargetState,
            request.ActorId,
            request.Reason,
            request.CaseReference,
            request.IdempotencyKey,
            clock.UtcNow,
            transitions);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

public sealed class AuthorizeShippingOperationRetryHandler(
    ITransactionRepository repository,
    IUnitOfWork unitOfWork,
    IClock clock)
    : IRequestHandler<AuthorizeShippingOperationRetryCommand>
{
    public async Task Handle(
        AuthorizeShippingOperationRetryCommand request,
        CancellationToken cancellationToken)
    {
        var transaction = await GetAsync(
            repository,
            request.TransactionId,
            cancellationToken);
        transaction.AuthorizeShippingOperationRetry(
            request.OperationId,
            request.ActorId,
            request.Reason,
            request.ProviderOutcomeReference,
            request.IdempotencyKey,
            clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

public sealed class OpenInsuranceCaseHandler(
    ITransactionRepository repository,
    IUnitOfWork unitOfWork,
    IClock clock)
    : IRequestHandler<OpenInsuranceCaseCommand>
{
    public async Task Handle(
        OpenInsuranceCaseCommand request,
        CancellationToken cancellationToken)
    {
        var transaction = await GetAsync(
            repository,
            request.TransactionId,
            cancellationToken);
        transaction.OpenShippingInsuranceCase(
            ShippingInsuranceCase.Open(
                request.TransactionId,
                request.ManagedShipmentId,
                request.Provider,
                request.ProviderCaseReference,
                request.ReasonCode,
                request.DeclaredValueSatang,
                request.ClaimedAmountSatang,
                "THB",
                request.CrmCaseReference,
                request.ActorId,
                clock.UtcNow),
            request.ActorId,
            clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

public sealed class ResolveInsuranceCaseHandler(
    ITransactionRepository repository,
    IUnitOfWork unitOfWork,
    IClock clock)
    : IRequestHandler<ResolveInsuranceCaseCommand>
{
    public async Task Handle(
        ResolveInsuranceCaseCommand request,
        CancellationToken cancellationToken)
    {
        var transaction = await GetAsync(
            repository,
            request.TransactionId,
            cancellationToken);
        var insuranceCase =
            transaction.ShippingInsuranceCases.SingleOrDefault(
                item => item.Id == request.InsuranceCaseId)
            ?? throw new NotFoundException(
                "ไม่พบเคสประกันพัสดุ");
        insuranceCase.Resolve(
            ActorRole.Reconciliation,
            request.ActorId,
            request.ProviderResultCode,
            request.ProviderResolutionReference,
            clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

public sealed class AuthorizeManagedReturnHandler(
    ITransactionRepository repository,
    IUnitOfWork unitOfWork,
    IClock clock)
    : IRequestHandler<AuthorizeManagedReturnCommand>
{
    public async Task Handle(
        AuthorizeManagedReturnCommand request,
        CancellationToken cancellationToken)
    {
        var transaction = await GetAsync(
            repository,
            request.TransactionId,
            cancellationToken);
        var shipment = ManagedShipment.CreateReturn(
            transaction.Id,
            request.Shipment,
            clock.UtcNow);
        var fingerprint =
            ManagedShippingOperationQueue.BookingFingerprint(
                shipment);
        var operation = ShippingOperation.Queue(
            transaction.Id,
            shipment.Id,
            ShippingOperationType.BookReturn,
            $"book-return:{transaction.Id:N}:{fingerprint}",
            fingerprint,
            clock.UtcNow);
        transaction.AuthorizeManagedReturn(
            shipment,
            operation,
            request.ActorId,
            request.CaseReference,
            request.Reason,
            request.IdempotencyKey,
            clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

public sealed class RecordTrustedReturnDeliveryHandler(
    ITransactionRepository repository,
    IUnitOfWork unitOfWork,
    IClock clock)
    : IRequestHandler<RecordTrustedReturnDeliveryCommand>
{
    public async Task Handle(
        RecordTrustedReturnDeliveryCommand request,
        CancellationToken cancellationToken)
    {
        var transaction = await GetAsync(
            repository,
            request.TransactionId,
            cancellationToken);
        transaction.RecordTrustedReturnDelivery(
            request.ManagedShipmentId,
            request.EventId,
            request.DeliveredAt,
            request.Provider,
            clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

public sealed class AuthorizeManualReturnResolutionHandler(
    ITransactionRepository repository,
    IUnitOfWork unitOfWork,
    IClock clock)
    : IRequestHandler<AuthorizeManualReturnResolutionCommand>
{
    public async Task Handle(
        AuthorizeManualReturnResolutionCommand request,
        CancellationToken cancellationToken)
    {
        var transaction = await GetAsync(
            repository,
            request.TransactionId,
            cancellationToken);
        transaction.AuthorizeManualReturnResolution(
            request.Reference,
            request.ActorId,
            request.Reason,
            request.IdempotencyKey,
            clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

internal static class ShippingTransactionLookup
{
    public static async Task<SaleTransaction> GetAsync(
        ITransactionRepository repository,
        Guid transactionId,
        CancellationToken cancellationToken) =>
        await repository.GetByIdAsync(
            transactionId,
            cancellationToken)
        ?? throw new NotFoundException("ไม่พบรายการ");
}
