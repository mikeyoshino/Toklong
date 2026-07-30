using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Toklong.Application.Abstractions;
using Toklong.Application.Features.Checkout.ChooseParcelProtection;
using Toklong.Application.Features.Shipping;
using Toklong.Application.Features.Shipping.ProcessShippingOperations;
using Toklong.Application.Pricing;
using Toklong.Domain.Common;
using Toklong.Domain.Transactions;
using Toklong.Infrastructure.Persistence;

namespace Toklong.Application.Tests.Shipping;

public sealed class DurableShippingOperationProcessingTests
{
    static DurableShippingOperationProcessingTests() =>
        SQLitePCL.raw.SetProvider(
            new SQLitePCL.SQLite3Provider_sqlite3());

    private static readonly DateTimeOffset Now =
        new(2026, 7, 29, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Booking_fingerprint_changes_when_any_protection_field_changes()
    {
        var transactionId = Guid.NewGuid();
        var draft = DraftWithProtection(
                termsVersion: "parcel-protection-2026-07-30",
                optionReference: "protected-option-a") with
            {
                ParcelProtectionElection =
                    ParcelProtectionElectionStatus.Accepted,
                ParcelProtectionProviderCostSatang = 4_500,
                ParcelProtectionIncludedCoverageSatang = 100_000,
                ParcelProtectionSelectedCoverageSatang = 450_000
            };
        var shipment = ManagedShipment.CreateOutbound(
            transactionId,
            draft,
            Now);
        var changedTerms = ManagedShipment.CreateOutbound(
            transactionId,
            draft with
            {
                ParcelProtectionTermsVersion =
                    "parcel-protection-2026-08-01"
            },
            Now);
        var changedOption = ManagedShipment.CreateOutbound(
            transactionId,
            draft with
            {
                ParcelProtectionOptionReference = "protected-option-b"
            },
            Now);
        var changedElection = ManagedShipment.CreateOutbound(
            transactionId,
            draft with
            {
                ParcelProtectionElection =
                    ParcelProtectionElectionStatus.Declined
            },
            Now);
        var changedProviderCost = ManagedShipment.CreateOutbound(
            transactionId,
            draft with
            {
                ParcelProtectionProviderCostSatang = 4_600
            },
            Now);
        var changedIncludedCoverage = ManagedShipment.CreateOutbound(
            transactionId,
            draft with
            {
                ParcelProtectionIncludedCoverageSatang = 100_100
            },
            Now);
        var changedSelectedCoverage = ManagedShipment.CreateOutbound(
            transactionId,
            draft with
            {
                ParcelProtectionSelectedCoverageSatang = 450_100
            },
            Now);

        var fingerprint =
            ManagedShippingOperationQueue.BookingFingerprint(shipment);

        Assert.NotEqual(
            fingerprint,
            ManagedShippingOperationQueue.BookingFingerprint(changedTerms));
        Assert.NotEqual(
            fingerprint,
            ManagedShippingOperationQueue.BookingFingerprint(changedOption));
        Assert.NotEqual(
            fingerprint,
            ManagedShippingOperationQueue.BookingFingerprint(changedElection));
        Assert.NotEqual(
            fingerprint,
            ManagedShippingOperationQueue.BookingFingerprint(changedProviderCost));
        Assert.NotEqual(
            fingerprint,
            ManagedShippingOperationQueue.BookingFingerprint(changedIncludedCoverage));
        Assert.NotEqual(
            fingerprint,
            ManagedShippingOperationQueue.BookingFingerprint(changedSelectedCoverage));
    }

    [Fact]
    public async Task Changed_protection_option_is_superseded_before_provider_reservation()
    {
        await using var database = CreateDatabase();
        var (transaction, operation) = PendingBuyerCheckoutBooking();
        database.Transactions.Add(transaction);
        await database.SaveChangesAsync();
        var provider = new BookingProvider
        {
            ProtectionOption = BookingProvider.DefaultProtectionOption with
            {
                TermsVersion = "parcel-protection-2026-08-01"
            }
        };

        Assert.True(await Handler(
                database,
                operation,
                provider,
                new FixedClock(Now.AddMinutes(1)))
            .Handle(
                new ProcessNextShippingOperationCommand("worker-a"),
                default));

        Assert.Equal(0, provider.ReserveCalls);
        Assert.Equal(
            ParcelProtectionElectionStatus.ReconfirmationRequired,
            transaction.ParcelProtectionElection);
        Assert.Equal(ShippingOperationStatus.Superseded, operation.Status);
        Assert.Contains(transaction.AuditEvents, audit =>
            audit.Name == "parcel_protection.booking_outcome");
    }

    [Fact]
    public async Task Reconfirmation_after_superseded_unreserved_booking_queues_one_new_exact_intent()
    {
        await using var database = CreateDatabase();
        var (transaction, supersededOperation) = PendingBuyerCheckoutBooking();
        database.Transactions.Add(transaction);
        await database.SaveChangesAsync();
        var provider = new BookingProvider
        {
            ProtectionOption = BookingProvider.DefaultProtectionOption with
            {
                TermsVersion = "parcel-protection-2026-08-01"
            }
        };
        await Handler(
                database,
                supersededOperation,
                provider,
                new FixedClock(Now.AddMinutes(1)))
            .Handle(
                new ProcessNextShippingOperationCommand("worker-a"),
                default);

        var handler = new ChooseParcelProtectionHandler(
            new TransactionRepository(database),
            provider,
            new ParcelProtectionPricingPolicy(),
            database,
            new FixedClock(Now.AddMinutes(2)));
        var result = await handler.Handle(
            new ChooseParcelProtectionCommand(
                transaction.Id,
                transaction.BuyerId!.Value,
                AddProtection: false,
                OptionReference: null,
                DisclosedCustomerPriceSatang: null,
                IdempotencyKey: "reconfirm-declined-choice"),
            default);

        Assert.Equal("preparing_shipping", result.BookingStatus);
        Assert.Equal(2, transaction.ManagedShipments.Count);
        Assert.Equal(ShippingOperationStatus.Superseded,
            supersededOperation.Status);
        Assert.Single(transaction.ShippingOperations,
            operation => operation.Status == ShippingOperationStatus.Pending);
    }

    [Fact]
    public async Task Two_consecutive_quote_changes_preserve_history_and_queue_the_latest_reconfirmation()
    {
        await using var database = CreateDatabase();
        var (transaction, first) = PendingBuyerCheckoutBooking();
        database.Transactions.Add(transaction);
        await database.SaveChangesAsync();
        var secondOption = BookingProvider.DefaultProtectionOption with
        {
            TermsVersion = "parcel-protection-2026-08-01"
        };
        var secondProvider = new BookingProvider { ProtectionOption = secondOption };
        await Handler(database, first, secondProvider, new FixedClock(Now.AddMinutes(1)))
            .Handle(new ProcessNextShippingOperationCommand("worker-a"), default);

        await new ChooseParcelProtectionHandler(
                new TransactionRepository(database), secondProvider,
                new ParcelProtectionPricingPolicy(), database,
                new FixedClock(Now.AddMinutes(2)))
            .Handle(new ChooseParcelProtectionCommand(
                transaction.Id, transaction.BuyerId!.Value, true,
                secondOption.OptionReference, 6_000, "reconfirm-first-change"),
                default);
        var second = transaction.ShippingOperations.Single(
            item => item.Status == ShippingOperationStatus.Pending);
        var thirdOption = secondOption with
        {
            TermsVersion = "parcel-protection-2026-09-01"
        };
        var thirdProvider = new BookingProvider { ProtectionOption = thirdOption };
        await Handler(database, second, thirdProvider, new FixedClock(Now.AddMinutes(3)))
            .Handle(new ProcessNextShippingOperationCommand("worker-b"), default);

        await new ChooseParcelProtectionHandler(
                new TransactionRepository(database), thirdProvider,
                new ParcelProtectionPricingPolicy(), database,
                new FixedClock(Now.AddMinutes(4)))
            .Handle(new ChooseParcelProtectionCommand(
                transaction.Id, transaction.BuyerId!.Value, true,
                thirdOption.OptionReference, 6_000, "reconfirm-second-change"),
                default);

        Assert.Equal(3, transaction.ManagedShipments.Count);
        Assert.Equal(2, transaction.ShippingOperations.Count(item =>
            item.Status == ShippingOperationStatus.Superseded));
        Assert.Single(transaction.ShippingOperations, item =>
            item.Status == ShippingOperationStatus.Pending);
    }

    [Fact]
    public void Ambiguous_latest_superseded_outbound_attempt_fails_closed()
    {
        var (transaction, first) = PendingBuyerCheckoutBooking();
        first.Claim("worker-a", Now.AddMinutes(1), TimeSpan.FromMinutes(5));
        first.Supersede("worker-a", "quote-changed", Now.AddMinutes(1));
        var secondShipment = ManagedShipment.CreateOutbound(
            transaction.Id,
            DraftWithProtection("parcel-protection-v2", "option-v2"),
            Now.AddMinutes(2));
        var second = ShippingOperation.Queue(transaction.Id, secondShipment.Id,
            ShippingOperationType.BookOutbound, "book-second",
            ManagedShippingOperationQueue.BookingFingerprint(secondShipment),
            Now.AddMinutes(2));
        transaction.QueueManagedShipment(secondShipment, second, ActorRole.System,
            "test", Now.AddMinutes(2));
        second.Claim("worker-b", Now.AddMinutes(3), TimeSpan.FromMinutes(5));
        second.Supersede("worker-b", "quote-changed", Now.AddMinutes(3));
        typeof(ManagedShipment).GetProperty(nameof(ManagedShipment.CreatedAt))!
            .SetValue(secondShipment, transaction.ManagedShipments.First().CreatedAt);
        var thirdShipment = ManagedShipment.CreateOutbound(transaction.Id,
            DraftWithProtection("parcel-protection-v3", "option-v3"),
            Now.AddMinutes(4));
        var third = ShippingOperation.Queue(transaction.Id, thirdShipment.Id,
            ShippingOperationType.BookOutbound, "book-third",
            ManagedShippingOperationQueue.BookingFingerprint(thirdShipment),
            Now.AddMinutes(4));

        Assert.Throws<DomainException>(() => transaction.QueueManagedShipment(
            thirdShipment, third, ActorRole.System, "test", Now.AddMinutes(4)));
        Assert.Equal(2, transaction.ManagedShipments.Count);
    }

    [Theory]
    [InlineData("provider-cost")]
    [InlineData("included-limit")]
    [InlineData("selected-limit")]
    [InlineData("expiry")]
    public async Task Changed_protection_selection_field_is_superseded_before_provider_reservation(
        string changedField)
    {
        await using var database = CreateDatabase();
        var (transaction, operation) = PendingBuyerCheckoutBooking();
        database.Transactions.Add(transaction);
        await database.SaveChangesAsync();
        var option = changedField switch
        {
            "provider-cost" => BookingProvider.DefaultProtectionOption with
            {
                ProviderCostSatang = 4_501
            },
            "included-limit" => BookingProvider.DefaultProtectionOption with
            {
                IncludedCoverageLimitSatang = 100_001
            },
            "selected-limit" => BookingProvider.DefaultProtectionOption with
            {
                SelectedCoverageLimitSatang = 450_001
            },
            "expiry" => BookingProvider.DefaultProtectionOption with
            {
                ExpiresAt = Now.AddMinutes(29)
            },
            _ => throw new ArgumentOutOfRangeException(nameof(changedField))
        };
        var provider = new BookingProvider { ProtectionOption = option };

        await Handler(
                database,
                operation,
                provider,
                new FixedClock(Now.AddMinutes(1)))
            .Handle(
                new ProcessNextShippingOperationCommand("worker-a"),
                default);

        Assert.Equal(0, provider.ReserveCalls);
        Assert.Equal(ShippingOperationStatus.Superseded, operation.Status);
        Assert.Equal(ParcelProtectionElectionStatus.ReconfirmationRequired,
            transaction.ParcelProtectionElection);
    }

    [Fact]
    public async Task Successful_buyer_checkout_booking_preserves_seller_acceptance_deadline()
    {
        await using var database = CreateDatabase();
        var (transaction, operation) = PendingBuyerCheckoutBooking();
        database.Transactions.Add(transaction);
        await database.SaveChangesAsync();
        var clock = new FixedClock(Now.AddMinutes(1));
        var deadline = transaction.BuyerPaymentDeadlineAt;
        var handler = Handler(
            database,
            operation,
            new BookingProvider(),
            clock);

        Assert.True(await handler.Handle(
            new ProcessNextShippingOperationCommand("worker-a"),
            default));

        Assert.Equal(
            ShippingOperationStatus.Succeeded,
            operation.Status);
        Assert.Equal(
            TransactionState.SellerAcceptedAwaitingPayment,
            transaction.State);
        Assert.Equal(
            deadline,
            transaction.BuyerPaymentDeadlineAt);
        Assert.True(transaction.ParcelProtectionBookingReady);
        Assert.Single(
            transaction.AgreementAcceptances,
            acceptance =>
            acceptance.Role ==
                AgreementAcceptanceRole.Seller);
    }

    [Fact]
    public async Task Reserved_change_cancels_definitely_before_queuing_replacement_booking()
    {
        await using var database = CreateDatabase();
        var (transaction, booking) = PendingBuyerCheckoutBooking();
        database.Transactions.Add(transaction);
        await database.SaveChangesAsync();
        var provider = new BookingProvider();
        await Handler(database, booking, provider, new FixedClock(Now.AddMinutes(1)))
            .Handle(new ProcessNextShippingOperationCommand("worker-a"), default);

        await new ChooseParcelProtectionHandler(
                new TransactionRepository(database), provider,
                new ParcelProtectionPricingPolicy(), database,
                new FixedClock(Now.AddMinutes(2)))
            .Handle(new ChooseParcelProtectionCommand(
                transaction.Id, transaction.BuyerId!.Value, false, null, null,
                "change-reserved-decline"), default);

        var cancellation = Assert.Single(transaction.ShippingOperations,
            operation => operation.OperationType == ShippingOperationType.CancelOutbound);
        Assert.Single(transaction.ManagedShipments);
        Assert.Equal(ParcelProtectionElectionStatus.Accepted,
            transaction.ParcelProtectionElection);

        await Handler(database, cancellation, provider, new FixedClock(Now.AddMinutes(3)))
            .Handle(new ProcessNextShippingOperationCommand("worker-b"), default);

        Assert.Equal(ManagedShipmentStatus.Cancelled,
            transaction.ManagedShipments.First().Status);
        Assert.Equal(2, transaction.ManagedShipments.Count);
        Assert.Single(transaction.ShippingOperations, operation =>
            operation.OperationType == ShippingOperationType.BookOutbound &&
            operation.Status == ShippingOperationStatus.Pending);
        Assert.Equal(ParcelProtectionChangeStatus.AwaitingRebooking,
            Assert.Single(transaction.ParcelProtectionChangeRequests).Status);

        var replacement = Assert.Single(transaction.ShippingOperations,
            operation => operation.OperationType == ShippingOperationType.BookOutbound &&
                operation.Status == ShippingOperationStatus.Pending);
        await Handler(database, replacement, provider, new FixedClock(Now.AddMinutes(4)))
            .Handle(new ProcessNextShippingOperationCommand("worker-c"), default);

        Assert.Equal(ShippingOperationStatus.Succeeded, replacement.Status);
        Assert.Equal(ParcelProtectionElectionStatus.Declined,
            transaction.ParcelProtectionElection);
        Assert.Equal(0, transaction.ParcelInsuranceFeeSatang);
        Assert.Equal(ParcelProtectionChangeStatus.Completed,
            Assert.Single(transaction.ParcelProtectionChangeRequests).Status);
        Assert.Contains(transaction.AuditEvents,
            audit => audit.Name == "parcel_protection.changed");
    }

    [Fact]
    public async Task Fresh_context_cancel_reload_queues_replacement_and_blocks_checkout()
    {
        await using var database = await RelationalDatabase.CreateAsync();
        var (transaction, _) = PendingBuyerCheckoutBooking();
        await using (var setup = database.CreateContext())
        {
            setup.Transactions.Add(transaction);
            await setup.SaveChangesAsync();
        }
        var provider = new BookingProvider();

        await using (var bookingContext = database.CreateContext())
        {
            await RelationalWorker(bookingContext, provider, Now.AddMinutes(1))
                .Handle(new ProcessNextShippingOperationCommand("worker-a"), default);
        }
        await using (var choiceContext = database.CreateContext())
        {
            var stored = await new TransactionRepository(choiceContext).GetByIdAsync(
                transaction.Id, default);
            await new ChooseParcelProtectionHandler(
                    new TransactionRepository(choiceContext), provider,
                    new ParcelProtectionPricingPolicy(), choiceContext,
                    new FixedClock(Now.AddMinutes(2)))
                .Handle(new ChooseParcelProtectionCommand(
                    transaction.Id, stored!.BuyerId!.Value, false, null, null,
                    "fresh-context-change-01"), default);
        }
        await using (var cancellationContext = database.CreateContext())
        {
            await RelationalWorker(cancellationContext, provider, Now.AddMinutes(3))
                .Handle(new ProcessNextShippingOperationCommand("worker-b"), default);
        }
        await using var assertionContext = database.CreateContext();
        var reloaded = await new TransactionRepository(assertionContext)
            .GetByIdAsync(transaction.Id, default);

        Assert.NotNull(reloaded);
        Assert.Equal(ParcelProtectionChangeStatus.AwaitingRebooking,
            Assert.Single(reloaded!.ParcelProtectionChangeRequests).Status);
        Assert.Equal(2, reloaded.ManagedShipments.Count);
        Assert.Single(reloaded.ShippingOperations, operation =>
            operation.OperationType == ShippingOperationType.BookOutbound &&
            operation.Status == ShippingOperationStatus.Pending);
        Assert.Throws<DomainException>(() => reloaded.BeginCheckout(
            "ผู้ซื้อ ทดสอบ", "0800000000", Now.AddMinutes(4),
            new TransactionTransitionService(), "manual-bank", null, 5_900, 0,
            450_000, "fee-v1"));

        await using (var replacementContext = database.CreateContext())
        {
            await RelationalWorker(replacementContext, provider, Now.AddMinutes(5))
                .Handle(new ProcessNextShippingOperationCommand("worker-c"), default);
        }
        await using var completedContext = database.CreateContext();
        var completed = await new TransactionRepository(completedContext)
            .GetByIdAsync(transaction.Id, default);
        Assert.True(completed!.ParcelProtectionBookingReady);
        completed.BeginCheckout(
            "ผู้ซื้อ ทดสอบ", "0800000000", Now.AddMinutes(6),
            new TransactionTransitionService(), "manual-bank", null, 5_900, 0,
            450_000, "fee-v1");
    }

    [Theory]
    [InlineData(ShipmentMutationOutcome.DefiniteFailure,
        ShippingOperationStatus.RetryScheduled)]
    [InlineData(ShipmentMutationOutcome.OutcomeUnknown,
        ShippingOperationStatus.OutcomeUnknown)]
    public async Task Failed_change_cancellation_never_queues_a_replacement(
        ShipmentMutationOutcome outcome,
        ShippingOperationStatus expectedStatus)
    {
        await using var database = CreateDatabase();
        var (transaction, booking) = PendingBuyerCheckoutBooking();
        database.Transactions.Add(transaction);
        await database.SaveChangesAsync();
        var provider = new BookingProvider();
        await Handler(database, booking, provider, new FixedClock(Now.AddMinutes(1)))
            .Handle(new ProcessNextShippingOperationCommand("worker-a"), default);
        await new ChooseParcelProtectionHandler(
                new TransactionRepository(database), provider,
                new ParcelProtectionPricingPolicy(), database,
                new FixedClock(Now.AddMinutes(2)))
            .Handle(new ChooseParcelProtectionCommand(
                transaction.Id, transaction.BuyerId!.Value, false, null, null,
                "change-cancel-failure"), default);
        var cancellation = Assert.Single(transaction.ShippingOperations,
            operation => operation.OperationType == ShippingOperationType.CancelOutbound);
        provider.CancelFailure = new ShipmentMutationException(outcome, "cancel-failed");

        await Handler(database, cancellation, provider, new FixedClock(Now.AddMinutes(3)))
            .Handle(new ProcessNextShippingOperationCommand("worker-b"), default);

        Assert.Equal(expectedStatus, cancellation.Status);
        Assert.Equal(ManagedShipmentStatus.Reserved,
            transaction.CurrentOutboundShipment!.Status);
        Assert.Single(transaction.ManagedShipments);
        Assert.Equal(ParcelProtectionChangeStatus.AwaitingCancellation,
            Assert.Single(transaction.ParcelProtectionChangeRequests).Status);
    }

    [Fact]
    public async Task Replacement_reservation_mismatch_keeps_original_election_and_change_active()
    {
        await using var database = CreateDatabase();
        var (transaction, booking) = PendingBuyerCheckoutBooking();
        database.Transactions.Add(transaction);
        await database.SaveChangesAsync();
        var provider = new BookingProvider();
        await Handler(database, booking, provider, new FixedClock(Now.AddMinutes(1)))
            .Handle(new ProcessNextShippingOperationCommand("worker-a"), default);
        await new ChooseParcelProtectionHandler(
                new TransactionRepository(database), provider,
                new ParcelProtectionPricingPolicy(), database,
                new FixedClock(Now.AddMinutes(2)))
            .Handle(new ChooseParcelProtectionCommand(
                transaction.Id, transaction.BuyerId!.Value, false, null, null,
                "change-mismatch-decline"), default);
        var cancellation = Assert.Single(transaction.ShippingOperations,
            operation => operation.OperationType == ShippingOperationType.CancelOutbound);
        await Handler(database, cancellation, provider, new FixedClock(Now.AddMinutes(3)))
            .Handle(new ProcessNextShippingOperationCommand("worker-b"), default);
        var replacement = Assert.Single(transaction.ShippingOperations,
            operation => operation.OperationType == ShippingOperationType.BookOutbound &&
                operation.Status == ShippingOperationStatus.Pending);
        provider.ReservationFeeOverride = 5_201;

        await Handler(database, replacement, provider, new FixedClock(Now.AddMinutes(4)))
            .Handle(new ProcessNextShippingOperationCommand("worker-c"), default);

        Assert.Equal(ShippingOperationStatus.NeedsReview, replacement.Status);
        Assert.Equal(ParcelProtectionElectionStatus.Accepted,
            transaction.ParcelProtectionElection);
        Assert.Equal(ParcelProtectionChangeStatus.AwaitingRebooking,
            Assert.Single(transaction.ParcelProtectionChangeRequests).Status);
        Assert.False(transaction.ParcelProtectionBookingReady);
        Assert.DoesNotContain(transaction.AuditEvents,
            audit => audit.Name == "parcel_protection.changed");
    }

    [Fact]
    public async Task Included_only_shippop_acceptance_books_without_insurance()
    {
        await using var database = CreateDatabase();
        var (transaction, operation) = PendingBuyerCheckoutBooking(
            addProtection: false);
        database.Transactions.Add(transaction);
        await database.SaveChangesAsync();
        var provider = new BookingProvider();
        var handler = Handler(
            database,
            operation,
            provider,
            new FixedClock(Now.AddMinutes(1)));

        Assert.True(await handler.Handle(
            new ProcessNextShippingOperationCommand("worker-a"),
            default));

        Assert.NotNull(provider.LastReservationRequest);
        Assert.Equal(
            0,
            provider.LastReservationRequest!.Quote.InsuranceFeeSatang);
        Assert.Null(provider.LastReservationRequest.Quote.InsuranceCode);
        Assert.Equal(
            TransactionState.SellerAcceptedAwaitingPayment,
            transaction.State);
    }

    [Fact]
    public async Task Unknown_booking_outcome_is_not_replayed()
    {
        await using var database = CreateDatabase();
        var (transaction, operation) = PendingBuyerCheckoutBooking();
        database.Transactions.Add(transaction);
        await database.SaveChangesAsync();
        var provider = new BookingProvider
        {
            Failure = new ShipmentMutationException(
                ShipmentMutationOutcome.OutcomeUnknown,
                "provider-timeout")
        };
        var handler = Handler(
            database,
            operation,
            provider,
            new FixedClock(Now.AddMinutes(1)));

        Assert.True(await handler.Handle(
            new ProcessNextShippingOperationCommand("worker-a"),
            default));
        Assert.Equal(
            ShippingOperationStatus.OutcomeUnknown,
            operation.Status);
        Assert.Equal(
            TransactionState.SellerAcceptedAwaitingPayment,
            transaction.State);
        Assert.False(transaction.ParcelProtectionBookingReady);
        Assert.Contains(transaction.AuditEvents, audit =>
            audit.Name == "parcel_protection.booking_outcome");

        var second = await handler.Handle(
            new ProcessNextShippingOperationCommand("worker-b"),
            default);
        Assert.False(second);
        Assert.Equal(1, provider.ReserveCalls);
    }

    [Fact]
    public async Task Unexpected_provider_failure_is_sent_to_review_not_left_processing()
    {
        await using var database = CreateDatabase();
        var (transaction, operation) = PendingBuyerCheckoutBooking();
        database.Transactions.Add(transaction);
        await database.SaveChangesAsync();
        var provider = new BookingProvider
        {
            Failure = new InvalidOperationException(
                "raw provider response must not escape")
        };
        var handler = Handler(
            database,
            operation,
            provider,
            new FixedClock(Now.AddMinutes(1)));

        Assert.True(await handler.Handle(
            new ProcessNextShippingOperationCommand("worker-a"),
            default));

        Assert.Equal(
            ShippingOperationStatus.NeedsReview,
            operation.Status);
        Assert.Equal(
            "unexpected-provider-failure",
            operation.LastSanitizedErrorCode);
        Assert.Equal(1, provider.ReserveCalls);
    }

    [Fact]
    public async Task Changed_shipping_intent_is_rejected_before_provider_call()
    {
        await using var database = CreateDatabase();
        var (transaction, _) = PendingBuyerCheckoutBooking();
        var shipment = Assert.Single(
            transaction.ManagedShipments);
        var mismatchedOperation = ShippingOperation.Queue(
            transaction.Id,
            shipment.Id,
            ShippingOperationType.BookOutbound,
            $"book-outbound:{transaction.Id:N}:mismatch",
            new string('f', 64),
            Now);
        transaction.QueueShippingOperation(
            mismatchedOperation,
            ActorRole.System,
            "test",
            Now);
        database.Transactions.Add(transaction);
        await database.SaveChangesAsync();
        var provider = new BookingProvider();
        var handler = Handler(
            database,
            mismatchedOperation,
            provider,
            new FixedClock(Now.AddMinutes(1)));

        Assert.True(await handler.Handle(
            new ProcessNextShippingOperationCommand("worker-a"),
            default));

        Assert.Equal(
            ShippingOperationStatus.NeedsReview,
            mismatchedOperation.Status);
        Assert.Equal(0, provider.ReserveCalls);
    }

    [Fact]
    public async Task Return_booking_records_separate_approved_operational_cost()
    {
        await using var database = CreateDatabase();
        var (transaction, outboundOperation) =
            PendingBuyerCheckoutBooking();
        database.Transactions.Add(transaction);
        await database.SaveChangesAsync();
        var provider = new BookingProvider();
        var clock = new FixedClock(Now.AddMinutes(1));
        await Handler(
                database,
                outboundOperation,
                provider,
                clock)
            .Handle(
                new ProcessNextShippingOperationCommand("worker-a"),
                default);
        typeof(SaleTransaction)
            .GetProperty(nameof(SaleTransaction.State))!
            .SetValue(
                transaction,
                TransactionState.ResolutionPending);
        var returnShipment = ManagedShipment.CreateReturn(
            transaction.Id,
            new ManagedShipmentDraft(
                "shippop",
                "destination-ref",
                "origin-ref",
                transaction.ProductName,
                1_200,
                20,
                30,
                15,
                "THAIPOST",
                "EMST",
                "ไปรษณีย์ไทย EMS",
                5_200,
                1_100,
                120_000,
                "FULL_VALUE",
                "return-quote-001",
                Now.AddHours(2)),
            Now.AddMinutes(2));
        var returnOperation = ShippingOperation.Queue(
            transaction.Id,
            returnShipment.Id,
            ShippingOperationType.BookReturn,
            $"book-return:{transaction.Id:N}:test",
            ManagedShippingOperationQueue.BookingFingerprint(
                returnShipment),
            Now.AddMinutes(2));
        transaction.AuthorizeManagedReturn(
            returnShipment,
            returnOperation,
            "crm-user",
            "CASE-RETURN-001",
            "อนุมัติให้ส่งคืน",
            "crm:return:authorize:001",
            Now.AddMinutes(2));
        await database.SaveChangesAsync();
        var buyerTotalBeforeReturn =
            transaction.BuyerTotalSatang;

        await Handler(
                database,
                returnOperation,
                provider,
                new FixedClock(Now.AddMinutes(3)))
            .Handle(
                new ProcessNextShippingOperationCommand("worker-b"),
                default);

        Assert.Equal(
            ShippingOperationStatus.Succeeded,
            returnOperation.Status);
        Assert.Equal(
            "return-purchase-001",
            returnShipment.PurchaseReference);
        Assert.Equal(
            "purchase-001",
            transaction.ShippingPurchaseReference);
        Assert.Equal(
            buyerTotalBeforeReturn,
            transaction.BuyerTotalSatang);
        var cost = Assert.Single(
            transaction.ProviderShippingAdjustments);
        Assert.Equal(
            "authorized-return-cost",
            cost.ReasonCode);
        Assert.Equal(6_300, cost.AmountSatang);
        Assert.False(cost.IsOpen);
    }

    private static ProcessNextShippingOperationHandler Handler(
        ToklongDbContext database,
        ShippingOperation operation,
        BookingProvider provider,
        IClock clock) =>
        new(
            new SingleOperationRepository(operation),
            new TransactionRepository(database),
            provider,
            provider,
            database,
            clock,
            new TransactionTransitionService());

    private static ProcessNextShippingOperationHandler RelationalWorker(
        ToklongDbContext context,
        BookingProvider provider,
        DateTimeOffset now) => new(
            new ShippingOperationRepository(context),
            new TransactionRepository(context), provider, provider, context,
            new FixedClock(now), new TransactionTransitionService());

    private static (SaleTransaction, ShippingOperation)
        PendingAcceptance(bool includedOnly = false)
    {
        var transaction = TestTransactionFactory.CreateBuyerOffer(
            Guid.NewGuid(),
            "ผู้ซื้อ ทดสอบ",
            "0800000000",
            FulfillmentType.PhysicalShipment,
            "กล้อง",
            "กล้องพร้อมเลนส์",
            ConditionCode.UsedGood,
            "",
            null,
            120_000,
            "terms-v1",
            Now,
            new TransactionTransitionService());
        var quote = new AcceptedShippingQuote(
            TestTransactionFactory.ShippingOriginAddress,
            TestTransactionFactory.DeliveryProvinceName,
            TestTransactionFactory.DeliveryPostalCode,
            1_200,
            20,
            30,
            15,
            "shippop",
            "quote-001",
            "THAIPOST",
            "EMST",
            "ไปรษณีย์ไทย EMS",
            5_200,
            includedOnly ? 0 : 1_100,
            includedOnly ? 0 : 120_000,
            includedOnly ? null : "FULL_VALUE",
            Now.AddHours(2),
            TestTransactionFactory.DeliveryDistrictName,
            TestTransactionFactory.DeliverySubdistrictName,
            OriginAddressLine:
                TestTransactionFactory.ShippingOriginAddress);
        var shipment = ManagedShipment.CreateOutbound(
            transaction.Id,
            new ManagedShipmentDraft(
                "shippop",
                "origin-ref",
                "destination-ref",
                transaction.ProductName,
                1_200,
                20,
                30,
                15,
                "THAIPOST",
                "EMST",
                "ไปรษณีย์ไทย EMS",
                5_200,
                includedOnly ? 0 : 1_100,
                includedOnly ? 0 : 120_000,
                includedOnly ? null : "FULL_VALUE",
                "quote-001",
                Now.AddHours(2)),
            Now);
        var operation = ShippingOperation.Queue(
            transaction.Id,
            shipment.Id,
            ShippingOperationType.BookOutbound,
            $"book-outbound:{transaction.Id:N}:test",
            ManagedShippingOperationQueue.BookingFingerprint(
                shipment),
            Now);
        transaction.BeginManagedSellerAcceptance(
            Guid.NewGuid(),
            "ผู้ขาย ทดสอบ",
            "0811111111",
            "KBANK",
            "ผู้ขาย ทดสอบ",
            "1234567890",
            true,
            Now,
            5_900,
            0,
            120_000,
            "fee-v1",
            quote,
            shipment,
            operation);
        return (transaction, operation);
    }

    private static (SaleTransaction, ShippingOperation)
        PendingBuyerCheckoutBooking(bool addProtection = true)
    {
        var transaction = TestTransactionFactory.CreateBuyerOffer(
            Guid.NewGuid(),
            "ผู้ซื้อ ทดสอบ",
            "0800000000",
            FulfillmentType.PhysicalShipment,
            "กล้อง",
            "กล้องพร้อมเลนส์",
            ConditionCode.UsedGood,
            "",
            null,
            450_000,
            "terms-v1",
            Now,
            new TransactionTransitionService());
        var quote = new AcceptedShippingQuote(
            TestTransactionFactory.ShippingOriginAddress,
            TestTransactionFactory.DeliveryProvinceName,
            TestTransactionFactory.DeliveryPostalCode,
            1_200,
            20,
            30,
            15,
            "shippop",
            "quote-001",
            "THAIPOST",
            "EMST",
            "ไปรษณีย์ไทย EMS",
            5_200,
            0,
            0,
            null,
            Now.AddHours(2),
            TestTransactionFactory.DeliveryDistrictName,
            TestTransactionFactory.DeliverySubdistrictName,
            OriginAddressLine: TestTransactionFactory.ShippingOriginAddress);
        transaction.AcceptBuyerOffer(
            Guid.NewGuid(),
            "ผู้ขาย ทดสอบ",
            "0811111111",
            "KBANK",
            "ผู้ขาย ทดสอบ",
            "1234567890",
            true,
            Now,
            new TransactionTransitionService(),
            5_900,
            0,
            450_000,
            "fee-v1",
            quote);
        var selection = addProtection
            ? new ParcelProtectionSelection(
                ParcelProtectionElectionStatus.Accepted,
                6_000,
                4_500,
                SaleTransaction.ParcelProtectionServiceFeeAmountSatang,
                100_000,
                450_000,
                "parcel-protection-v1",
                "protected-option",
                Now,
                Now.AddMinutes(30))
            : new ParcelProtectionSelection(
                ParcelProtectionElectionStatus.Declined,
                0,
                0,
                0,
                100_000,
                100_000,
                "parcel-protection-included-v1",
                null,
                Now,
                Now.AddMinutes(30));
        transaction.RecordParcelProtectionElection(
            transaction.BuyerId!.Value,
            selection,
            Now.AddSeconds(1));
        var shipment = ManagedShipment.CreateOutbound(
            transaction.Id,
            new ManagedShipmentDraft(
                "shippop",
                "origin-ref",
                "destination-ref",
                transaction.ProductName,
                1_200,
                20,
                30,
                15,
                "THAIPOST",
                "EMST",
                "ไปรษณีย์ไทย EMS",
                5_200,
                addProtection ? 4_500 : 0,
                addProtection ? 450_000 : 0,
                addProtection ? "FULL_VALUE" : null,
                "quote-001",
                Now.AddHours(2),
                selection.TermsVersion,
                selection.ProviderOptionReference,
                selection.Election,
                selection.ProviderCostSatang,
                100_000,
                selection.SelectedCoverageLimitSatang),
            Now.AddSeconds(1));
        var operation = ShippingOperation.Queue(
            transaction.Id,
            shipment.Id,
            ShippingOperationType.BookOutbound,
            $"book-outbound:{transaction.Id:N}:buyer-choice",
            ManagedShippingOperationQueue.BookingFingerprint(shipment),
            Now.AddSeconds(1));
        transaction.QueueManagedShipment(
            shipment,
            operation,
            ActorRole.System,
            "test",
            Now.AddSeconds(1));
        return (transaction, operation);
    }

    private static ManagedShipmentDraft DraftWithProtection(
        string termsVersion,
        string optionReference) =>
        new(
            "shippop",
            "origin-ref",
            "destination-ref",
            "กล้อง",
            1_200,
            20,
            30,
            15,
            "THAIPOST",
            "EMST",
            "ไปรษณีย์ไทย EMS",
            5_200,
            4_500,
            450_000,
            "FULL_VALUE",
            "quote-001",
            Now.AddHours(2),
            termsVersion,
            optionReference);

    private static ToklongDbContext CreateDatabase()
    {
        var options =
            new DbContextOptionsBuilder<ToklongDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
        return new ToklongDbContext(options);
    }

    private sealed class RelationalDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection anchor;
        private readonly DbContextOptions<ToklongDbContext> options;

        private RelationalDatabase(
            SqliteConnection anchor,
            DbContextOptions<ToklongDbContext> options)
        {
            this.anchor = anchor;
            this.options = options;
        }

        public static async Task<RelationalDatabase> CreateAsync()
        {
            var connectionString =
                $"Data Source={Guid.NewGuid():N};Mode=Memory;Cache=Shared";
            var anchor = new SqliteConnection(connectionString);
            await anchor.OpenAsync();
            var options = new DbContextOptionsBuilder<ToklongDbContext>()
                .UseSqlite(connectionString).Options;
            await using var context = new ToklongDbContext(options);
            await context.Database.EnsureCreatedAsync();
            return new RelationalDatabase(anchor, options);
        }

        public ToklongDbContext CreateContext() => new(options);

        public ValueTask DisposeAsync() => anchor.DisposeAsync();
    }

    private sealed class SingleOperationRepository(
        ShippingOperation operation)
        : IShippingOperationRepository
    {
        public Task<ShippingOperation?> ClaimDueAsync(
            string workerId,
            DateTimeOffset now,
            TimeSpan leaseDuration,
            CancellationToken cancellationToken)
        {
            if (operation.Status is not (
                    ShippingOperationStatus.Pending or
                    ShippingOperationStatus.RetryScheduled or
                    ShippingOperationStatus.Processing))
                return Task.FromResult<ShippingOperation?>(null);
            if (operation.Status ==
                    ShippingOperationStatus.Processing &&
                operation.LeaseExpiresAt > now)
                return Task.FromResult<ShippingOperation?>(null);
            operation.Claim(workerId, now, leaseDuration);
            return Task.FromResult<ShippingOperation?>(operation);
        }

        public Task<ShippingOperation?> GetByIdAsync(
            Guid operationId,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                operation.Id == operationId
                    ? operation
                    : null);
    }

    private sealed class BookingProvider : IShipmentProvider,
        IParcelProtectionQuoteProvider
    {
        public static readonly ProviderParcelProtectionOption
            DefaultProtectionOption = new(
                "shippop",
                "protected-option",
                100_000,
                450_000,
                4_500,
                "parcel-protection-v1",
                "FULL_VALUE",
                Now,
                Now.AddMinutes(30));

        public string ProviderName => "shippop";
        public Exception? Failure { get; init; }
        public Exception? CancelFailure { get; set; }
        public long? ReservationFeeOverride { get; set; }
        public int ReserveCalls { get; private set; }
        public int ValidateProtectionCalls { get; private set; }
        public ProviderParcelProtectionOption ProtectionOption { get; init; } =
            DefaultProtectionOption;
        public ShipmentReservationRequest? LastReservationRequest
        { get; private set; }

        public Task<ShipmentReservation> ReserveAsync(
            ShipmentReservationRequest request,
            CancellationToken cancellationToken)
        {
            ReserveCalls++;
            LastReservationRequest = request;
            if (Failure is not null)
                throw Failure;
            return Task.FromResult(new ShipmentReservation(
                ProviderName,
                request.IsReturn
                    ? "return-purchase-001"
                    : "purchase-001",
                request.IsReturn
                    ? "return-provider-track-001"
                    : "provider-track-001",
                null,
                request.Quote.CarrierCode,
                request.Quote.ServiceCode,
                ReservationFeeOverride ?? request.Quote.FeeSatang,
                request.Quote.InsuranceFeeSatang,
                request.Quote.DeclaredValueSatang,
                request.Quote.InsuranceCode,
                Now.AddMinutes(1)));
        }

        public Task<ShipmentTrackingUpdate> GetTrackingAsync(
            string providerTrackingCode,
            string carrierCode,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ShipmentConfirmation> ConfirmAsync(
            string purchaseReference,
            string providerTrackingCode,
            string carrierCode,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<string> GetLabelHtmlAsync(
            ShipmentLabelRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task CancelAsync(
            string courierTrackingCode,
            CancellationToken cancellationToken)
        {
            if (CancelFailure is not null)
                throw CancelFailure;
            return Task.CompletedTask;
        }

        public Task<ParcelProtectionAvailability> GetAvailabilityAsync(
            ParcelProtectionQuoteRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ParcelProtectionAvailability(
                100_000,
                ProtectionOption,
                ProviderCapabilityCertified: true));

        public Task<ProviderParcelProtectionOption> ValidateOptionAsync(
            ParcelProtectionQuoteRequest request,
            string optionReference,
            CancellationToken cancellationToken)
        {
            ValidateProtectionCalls++;
            return Task.FromResult(ProtectionOption);
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }
}
