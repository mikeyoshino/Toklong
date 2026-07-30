using System.Windows.Input;
using Toklong.Mobile.Core;
using Toklong.Mobile.ViewModels;

namespace Toklong.Mobile.Core.Tests;

public sealed class TransactionDetailParcelProtectionViewModelTests
{
    [Fact]
    public async Task Fresh_instance_resumes_ready_election_without_resubmitting()
    {
        var service = new ParcelProtectionService
        {
            Protection = ReadyProtection(),
            Transaction = Transaction(amountSatang: 456_00)
        };
        var sheet = new RecordingSheet(PaymentSheetOutcome.Completed);
        var analytics = new RecordingAnalytics();
        var viewModel = ViewModel(service, sheet, analytics);

        await viewModel.LoadAsync(service.Transaction.Id);
        viewModel.AcceptedTerms = true;
        await ExecuteAsync(viewModel.PrimaryActionCommand);

        Assert.Equal(0, service.PrepareCalls);
        Assert.Equal(0, service.ChooseCalls);
        Assert.Equal(1, sheet.Calls);
        Assert.Contains(analytics.Events, e =>
            e.Name == "parcel_protection_checkout_converted");
        Assert.DoesNotContain(analytics.Events.SelectMany(e => e.Properties.Keys),
            key => key.Contains("seller", StringComparison.OrdinalIgnoreCase) ||
                   key.Contains("service", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Pending_saved_election_opens_payment_without_polling()
    {
        var service = new ParcelProtectionService
        {
            Protection = PendingProtection(),
            Transaction = Transaction()
        };
        var sheet = new RecordingSheet();
        var viewModel = ViewModel(service, sheet);

        await viewModel.LoadAsync(service.Transaction.Id);
        viewModel.AcceptedTerms = true;
        await ExecuteAsync(viewModel.PrimaryActionCommand);

        Assert.Equal(2, service.GetProtectionCalls);
        Assert.Equal(0, service.PrepareCalls);
        Assert.Equal(0, service.ChooseCalls);
        Assert.Equal(1, sheet.Calls);
    }

    [Fact]
    public async Task Retrying_a_pending_election_uses_checkout_again_without_resubmitting()
    {
        var service = new ParcelProtectionService
        {
            Protection = PendingProtection(),
            Transaction = Transaction()
        };
        var sheet = new RecordingSheet();
        var viewModel = ViewModel(service, sheet);

        await viewModel.LoadAsync(service.Transaction.Id);
        viewModel.AcceptedTerms = true;
        await ExecuteAsync(viewModel.PrimaryActionCommand);
        await ExecuteAsync(viewModel.PrimaryActionCommand);

        Assert.Equal(3, service.GetProtectionCalls);
        Assert.Equal(0, service.PrepareCalls);
        Assert.Equal(0, service.ChooseCalls);
        Assert.Equal(2, sheet.Calls);
    }

    [Fact]
    public async Task Retryable_preparation_failure_uses_a_new_checkout_key()
    {
        var service = new ParcelProtectionService
        {
            Protection = ReadyProtection(),
            Transaction = Transaction()
        };
        var sheet = new RecordingSheet();
        sheet.Failures.Enqueue(
            new PaymentPreparationException(
                "shipping_retry_required",
                true,
                "เตรียมการจัดส่งไม่สำเร็จ"));
        var viewModel = ViewModel(service, sheet);

        await viewModel.LoadAsync(
            service.Transaction.Id);
        viewModel.AcceptedTerms = true;
        await ExecuteAsync(
            viewModel.PrimaryActionCommand);
        await ExecuteAsync(
            viewModel.PrimaryActionCommand);

        Assert.Equal(2, sheet.Calls);
        Assert.Equal(2, sheet.Keys.Count);
        Assert.NotEqual(
            sheet.Keys[0],
            sheet.Keys[1]);
    }

    [Fact]
    public async Task Choosing_protection_refreshes_transaction_before_payment_sheet()
    {
        var service = new ParcelProtectionService
        {
            Protection = ChoiceProtection(),
            PreparedProtection = ChoiceProtection(),
            Transaction = Transaction(amountSatang: 456_00),
            UpdatedTransaction = Transaction(amountSatang: 462_00),
            ProtectionAfterChoice = ReadyProtection()
        };
        var sheet = new RecordingSheet();
        var viewModel = ViewModel(service, sheet);

        await viewModel.LoadAsync(service.Transaction.Id);
        viewModel.AcceptedTerms = true;
        await ExecuteAsync(viewModel.PrimaryActionCommand);
        Assert.True(viewModel.IsParcelProtectionChoiceVisible);

        await ExecuteAsync(viewModel.AcceptParcelProtectionCommand);

        Assert.Equal(1, service.ChooseCalls);
        Assert.True(service.GetTransactionCalls >= 3);
        Assert.Equal(462_00, viewModel.Transaction!.AmountSatang);
        Assert.Equal(1, sheet.Calls);
    }

    [Fact]
    public async Task Closing_choice_then_retrying_starts_a_fresh_choice_without_election()
    {
        var service = new ParcelProtectionService
        {
            Protection = ChoiceProtection(),
            PreparedProtection = ChoiceProtection(),
            Transaction = Transaction()
        };
        var sheet = new RecordingSheet();
        var viewModel = ViewModel(service, sheet);

        await viewModel.LoadAsync(service.Transaction.Id);
        viewModel.AcceptedTerms = true;
        await ExecuteAsync(viewModel.PrimaryActionCommand);
        viewModel.DismissParcelProtectionCommand
            .Execute(null);
        await ExecuteAsync(viewModel.PrimaryActionCommand);

        Assert.True(viewModel.IsParcelProtectionChoiceVisible);
        Assert.Equal(2, service.PrepareCalls);
        Assert.Equal(0, service.ChooseCalls);
        Assert.Equal(0, sheet.Calls);
    }

    [Fact]
    public async Task Reconfirmation_without_an_offer_hides_values_and_blocks_payment()
    {
        var unavailable = new BuyerParcelProtection(
            false, false, 5_000, null, null, null, "terms-v1", null,
            "ReconfirmationRequired", false, true);
        var service = new ParcelProtectionService
        {
            Protection = unavailable,
            PreparedProtection = unavailable,
            Transaction = Transaction()
        };
        var sheet = new RecordingSheet();
        var viewModel = ViewModel(service, sheet);

        await viewModel.LoadAsync(service.Transaction.Id);
        viewModel.AcceptedTerms = true;
        await ExecuteAsync(viewModel.PrimaryActionCommand);

        Assert.True(viewModel.IsParcelProtectionChoiceVisible);
        Assert.True(viewModel.IsParcelProtectionUnavailable);
        Assert.False(viewModel.HasParcelProtectionOfferDetails);
        Assert.Equal("", viewModel.MaximumCoverageText);
        Assert.Equal("", viewModel.ParcelProtectionPriceText);
        Assert.Equal(0, service.ChooseCalls);
        Assert.Equal(0, sheet.Calls);
    }

    [Fact]
    public async Task Load_restores_change_action_before_payment_is_pending()
    {
        var service = new ParcelProtectionService
        {
            Protection = ReadyProtection(),
            PreparedProtection = ChoiceProtection(),
            Transaction = Transaction()
        };
        var viewModel = ViewModel(service);

        await viewModel.LoadAsync(service.Transaction.Id);
        Assert.True(viewModel.CanChangeParcelProtection);

        await ExecuteAsync(viewModel.ChangeParcelProtectionCommand);

        Assert.Equal(1, service.PrepareCalls);
        Assert.True(viewModel.IsParcelProtectionChoiceVisible);
    }

    private static TransactionDetailViewModel ViewModel(
        ParcelProtectionService service,
        RecordingSheet? sheet = null,
        RecordingAnalytics? analytics = null) =>
        new(
            service,
            sheet ?? new RecordingSheet(),
            analytics ?? new RecordingAnalytics());

    private static async Task ExecuteAsync(ICommand command)
    {
        var completed = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var started = false;
        command.CanExecuteChanged += (_, _) =>
        {
            if (!command.CanExecute(null))
                started = true;
            else if (started)
                completed.TrySetResult();
        };
        command.Execute(null);
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    private static AppTransaction Transaction(long amountSatang = 456_00) =>
        new(
            Guid.NewGuid(), "กล้อง", amountSatang, "THB",
            AppTransactionRole.Buyer, AppFulfillmentType.Physical,
            "SellerAcceptedAwaitingPayment",
            DateTimeOffset.Parse("2026-07-30T10:00:00+07:00"),
            DateTimeOffset.Parse("2026-07-31T10:00:00+07:00"), "ผู้ขาย",
            ItemPriceSatang: 450_00,
            ParcelInsuranceFeeSatang: amountSatang - 450_00);

    private static BuyerParcelProtection ChoiceProtection() =>
        new(true, true, 5_000, 10_000, 600, "option-1", "terms-v1", null,
            "Pending", false, false);

    private static BuyerParcelProtection PendingProtection() =>
        ChoiceProtection() with { RequiresChoice = false, Election = "Accepted" };

    private static BuyerParcelProtection ReadyProtection() =>
        ChoiceProtection() with
        {
            Election = "Accepted", BookingReady = true,
            RequiresChoice = true
        };

    private sealed class RecordingSheet(
        PaymentSheetOutcome outcome = PaymentSheetOutcome.Cancelled)
        : IStripePaymentSheetService
    {
        public int Calls { get; private set; }
        public List<string> Keys { get; } = [];
        public Queue<Exception> Failures { get; } = [];

        public Task<PaymentSheetOutcome> PresentAsync(
            Guid transactionId,
            string idempotencyKey,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            Keys.Add(idempotencyKey);
            if (Failures.TryDequeue(
                    out var failure))
                throw failure;
            return Task.FromResult(outcome);
        }
    }

    private sealed class RecordingAnalytics : IMobileAnalytics
    {
        public List<MobileAnalyticsEvent> Events { get; } = [];

        public void Track(MobileAnalyticsEvent value) => Events.Add(value);
    }

    private sealed class ParcelProtectionService : ITransactionService
    {
        public required AppTransaction Transaction { get; init; }
        public AppTransaction? UpdatedTransaction { get; init; }
        public required BuyerParcelProtection Protection { get; set; }
        public BuyerParcelProtection? PreparedProtection { get; init; }
        public BuyerParcelProtection? ProtectionAfterChoice { get; init; }
        public int GetProtectionCalls { get; private set; }
        public int GetTransactionCalls { get; private set; }
        public int PrepareCalls { get; private set; }
        public int ChooseCalls { get; private set; }
        public Task<AppTransaction?> GetTransactionAsync(
            Guid transactionId,
            CancellationToken cancellationToken = default)
        {
            GetTransactionCalls++;
            return Task.FromResult<AppTransaction?>(
                UpdatedTransaction is not null && ChooseCalls > 0
                    ? UpdatedTransaction
                    : Transaction);
        }

        public Task<BuyerParcelProtection> GetParcelProtectionAsync(
            Guid transactionId,
            CancellationToken cancellationToken = default)
        {
            GetProtectionCalls++;
            return Task.FromResult(Protection);
        }

        public Task<BuyerParcelProtection> PrepareParcelProtectionAsync(
            Guid transactionId,
            string idempotencyKey,
            CancellationToken cancellationToken = default)
        {
            PrepareCalls++;
            return Task.FromResult(PreparedProtection ?? Protection);
        }

        public Task<string> ChooseParcelProtectionAsync(
            Guid transactionId,
            bool addProtection,
            string? optionReference,
            long? disclosedCustomerPriceSatang,
            string idempotencyKey,
            CancellationToken cancellationToken = default)
        {
            ChooseCalls++;
            Protection = ProtectionAfterChoice ?? Protection;
            return Task.FromResult("preparing");
        }

        public Task<BuyerCostPreview> GetBuyerCostPreviewAsync(long itemPriceSatang,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<CarrierOption>> GetSupportedCarriersAsync(
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<AppTransaction>> GetTransactionsAsync(
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AppTransaction> CreateBuyerOfferAsync(CreateBuyerOfferRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AppTransaction> SubmitTrackingAsync(Guid transactionId, string carrierCode,
            string trackingNumber, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AppTransaction> SubmitDigitalHandoffAsync(Guid transactionId, string statement,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AppTransaction> ConfirmReceiptAsync(Guid transactionId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AppTransaction> OpenDisputeAsync(Guid transactionId, AppDisputeReason reason,
            string statement, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
