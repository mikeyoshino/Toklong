using System.Windows.Input;
using Toklong.Mobile.Core;
using Toklong.Mobile.ViewModels;

namespace Toklong.Mobile.Core.Tests;

public sealed class TransactionDetailParcelProtectionViewModelTests
{
    [Fact]
    public async Task Ready_checkout_starts_without_a_separate_acceptance_toggle()
    {
        var service = new ParcelProtectionService
        {
            Protection = ReadyProtection(),
            Transaction = Transaction(amountSatang: 456_00)
        };
        var sheet = new RecordingSheet(PaymentSheetOutcome.Completed);
        var viewModel = ViewModel(service, sheet);

        await viewModel.LoadAsync(service.Transaction.Id);

        Assert.True(viewModel.CanStartPayment);
        Assert.Equal(
            "ยืนยันและชำระ ฿456",
            viewModel.PaymentActionText);

        await ExecuteAsync(viewModel.PrimaryActionCommand);

        Assert.Equal(1, sheet.Calls);
    }

    [Fact]
    public async Task First_visit_prepares_toggle_without_opening_modal()
    {
        var service = new ParcelProtectionService
        {
            Protection = ChoiceProtection(),
            PreparedProtection = ChoiceProtection(),
            Transaction = Transaction(amountSatang: 450_00)
        };
        var sheet = new RecordingSheet();
        var analytics = new RecordingAnalytics();
        var viewModel = ViewModel(service, sheet, analytics);

        await viewModel.LoadAsync(service.Transaction.Id);

        Assert.False(viewModel.IsParcelProtectionChoiceVisible);
        Assert.True(viewModel.ShowParcelProtectionToggle);
        Assert.False(viewModel.IsParcelProtectionToggleOn);
        Assert.Equal(1, service.PrepareCalls);
        Assert.Equal(0, service.ChooseCalls);
        Assert.Equal(0, sheet.Calls);
        Assert.Contains(
            analytics.Events,
            value => value.Name == "parcel_protection_offered");
    }

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
    public async Task Payment_sheet_loading_state_stays_visible_until_the_sheet_returns()
    {
        var service = new ParcelProtectionService
        {
            Protection = ReadyProtection(),
            Transaction = Transaction()
        };
        var sheet = new ControlledSheet();
        var viewModel = ViewModel(service, sheet);

        await viewModel.LoadAsync(service.Transaction.Id);
        var execution = ExecuteAsync(
            viewModel.PrimaryActionCommand);

        await sheet.Started.Task.WaitAsync(
            TimeSpan.FromSeconds(2));

        Assert.True(viewModel.IsPaymentSheetOpening);
        Assert.Equal(
            "กำลังเตรียมการจัดส่งและเปิดหน้าจ่ายเงิน…",
            viewModel.Message);

        sheet.Complete(PaymentSheetOutcome.Cancelled);
        await execution;

        Assert.False(viewModel.IsPaymentSheetOpening);
        Assert.Equal(
            "ยังไม่ได้จ่ายเงิน กดชำระอีกครั้งได้",
            viewModel.Message);
    }

    [Fact]
    public async Task Loading_another_transaction_never_reuses_its_checkout_key()
    {
        var first = Transaction();
        var second = Transaction();
        var service = new ParcelProtectionService
        {
            Protection = ReadyProtection(),
            Transaction = first
        };
        var sheet = new RecordingSheet();
        var viewModel = ViewModel(service, sheet);

        await viewModel.LoadAsync(first.Id);
        await ExecuteAsync(viewModel.PrimaryActionCommand);

        service.Transaction = second;
        await viewModel.LoadAsync(second.Id);
        await ExecuteAsync(viewModel.PrimaryActionCommand);

        Assert.Equal(2, sheet.Keys.Count);
        Assert.NotEqual(sheet.Keys[0], sheet.Keys[1]);
        Assert.Contains(first.Id.ToString("N"), sheet.Keys[0]);
        Assert.Contains(second.Id.ToString("N"), sheet.Keys[1]);
    }

    [Fact]
    public async Task Choosing_protection_updates_total_before_payment_and_submits_once()
    {
        var service = new ParcelProtectionService
        {
            Protection = ChoiceProtection(),
            PreparedProtection = ChoiceProtection(),
            Transaction = Transaction(amountSatang: 450_00),
            UpdatedTransaction = Transaction(amountSatang: 456_00),
            ProtectionAfterChoice = ReadyProtection()
        };
        var sheet = new RecordingSheet();
        var viewModel = ViewModel(service, sheet);

        await viewModel.LoadAsync(service.Transaction.Id);
        Assert.False(viewModel.IsParcelProtectionChoiceVisible);
        Assert.Equal(1, service.PrepareCalls);

        await ExecuteAsync(viewModel.ToggleParcelProtectionCommand);

        Assert.True(viewModel.IsParcelProtectionChoiceVisible);
        Assert.True(viewModel.IsParcelProtectionToggleOn);
        Assert.Equal(0, service.ChooseCalls);

        await ExecuteAsync(viewModel.ConfirmParcelProtectionCommand);

        Assert.True(viewModel.IsParcelProtectionAddOnSelected);
        Assert.Equal("฿456", viewModel.CheckoutAmountText);
        Assert.Equal("ยืนยันและชำระ ฿456", viewModel.PaymentActionText);
        Assert.Equal(1, service.ChooseCalls);
        Assert.Equal(0, sheet.Calls);
        Assert.False(viewModel.IsParcelProtectionChoiceVisible);
        Assert.Equal(456_00, viewModel.Transaction!.AmountSatang);

        await ExecuteAsync(viewModel.PrimaryActionCommand);

        Assert.Equal(1, service.ChooseCalls);
        Assert.Equal(1, sheet.Calls);
    }

    [Fact]
    public async Task Changed_protection_quote_keeps_modal_open_with_reconfirmation_message()
    {
        var refreshed = ChoiceProtection() with
        {
            OptionReference = "refreshed-option",
            CustomerPriceSatang = 7_00
        };
        var service = new ParcelProtectionService
        {
            Protection = ChoiceProtection(),
            PreparedProtection = refreshed,
            Transaction = Transaction(amountSatang: 450_00)
        };
        service.ChooseStatuses.Enqueue("reconfirmation_required");
        var viewModel = ViewModel(service);

        await viewModel.LoadAsync(service.Transaction.Id);
        await ExecuteAsync(viewModel.ToggleParcelProtectionCommand);
        await ExecuteAsync(viewModel.ConfirmParcelProtectionCommand);

        Assert.True(viewModel.IsParcelProtectionChoiceVisible);
        Assert.True(viewModel.IsParcelProtectionToggleOn);
        Assert.Equal("refreshed-option", viewModel.ParcelProtection!.OptionReference);
        Assert.Equal(
            "ข้อมูลความคุ้มครองเปลี่ยน กรุณาตรวจสอบอีกครั้งแล้วกดตกลง",
            viewModel.Message);
        Assert.Equal(2, service.PrepareCalls);
        Assert.Equal(1, service.ChooseCalls);
    }

    [Fact]
    public async Task Cancelling_add_on_confirmation_restores_toggle_and_total_without_saving()
    {
        var service = new ParcelProtectionService
        {
            Protection = ChoiceProtection(),
            PreparedProtection = ChoiceProtection(),
            Transaction = Transaction(amountSatang: 450_00)
        };
        var viewModel = ViewModel(service);

        await viewModel.LoadAsync(service.Transaction.Id);
        await ExecuteAsync(viewModel.ToggleParcelProtectionCommand);

        Assert.True(viewModel.IsParcelProtectionToggleOn);
        Assert.Equal("฿456", viewModel.CheckoutAmountText);

        viewModel.CancelParcelProtectionCommand.Execute(null);

        Assert.False(viewModel.IsParcelProtectionChoiceVisible);
        Assert.False(viewModel.IsParcelProtectionToggleOn);
        Assert.Equal("฿450", viewModel.CheckoutAmountText);
        Assert.Equal(0, service.ChooseCalls);
    }

    [Fact]
    public async Task Cancelling_removal_restores_saved_add_on()
    {
        var service = new ParcelProtectionService
        {
            Protection = ReadyProtection(),
            Transaction = Transaction(amountSatang: 456_00)
        };
        var viewModel = ViewModel(service);

        await viewModel.LoadAsync(service.Transaction.Id);
        await ExecuteAsync(viewModel.ToggleParcelProtectionCommand);

        Assert.False(viewModel.IsParcelProtectionToggleOn);
        Assert.True(viewModel.IsParcelProtectionChoiceVisible);

        viewModel.CancelParcelProtectionCommand.Execute(null);

        Assert.True(viewModel.IsParcelProtectionToggleOn);
        Assert.False(viewModel.IsParcelProtectionChoiceVisible);
        Assert.Equal(0, service.ChooseCalls);
    }

    [Fact]
    public async Task Confirming_removal_saves_decline_once()
    {
        var service = new ParcelProtectionService
        {
            Protection = ReadyProtection(),
            Transaction = Transaction(amountSatang: 456_00),
            UpdatedTransaction = Transaction(amountSatang: 450_00),
            ProtectionAfterChoice = DeclinedProtection()
        };
        var viewModel = ViewModel(service);

        await viewModel.LoadAsync(service.Transaction.Id);
        await ExecuteAsync(viewModel.ToggleParcelProtectionCommand);
        await ExecuteAsync(viewModel.ConfirmParcelProtectionCommand);

        Assert.Equal([false], service.Choices);
        Assert.False(viewModel.IsParcelProtectionToggleOn);
        Assert.False(viewModel.IsParcelProtectionChoiceVisible);
        Assert.Equal("฿450", viewModel.CheckoutAmountText);
    }

    [Fact]
    public async Task Toggle_preparation_failure_keeps_saved_decline_and_does_not_open_modal()
    {
        var service = new ParcelProtectionService
        {
            Protection = DeclinedProtection(),
            Transaction = Transaction(amountSatang: 450_00)
        };
        service.PrepareFailures.Enqueue(
            new HttpRequestException("network unavailable"));
        var viewModel = ViewModel(service);
        var changedProperties = new List<string?>();
        viewModel.PropertyChanged += (_, eventArgs) =>
            changedProperties.Add(eventArgs.PropertyName);

        await viewModel.LoadAsync(service.Transaction.Id);
        Assert.True(viewModel.ShowParcelProtectionToggle);
        Assert.True(viewModel.CanToggleParcelProtection);
        changedProperties.Clear();
        await ExecuteAsync(viewModel.ToggleParcelProtectionCommand);

        Assert.False(viewModel.IsParcelProtectionToggleOn);
        Assert.False(viewModel.IsParcelProtectionChoiceVisible);
        Assert.Equal(0, service.ChooseCalls);
        Assert.Equal("network unavailable", viewModel.Message);
        Assert.Contains(
            nameof(viewModel.IsParcelProtectionToggleOn),
            changedProperties);
    }

    [Fact]
    public async Task Payment_pending_decline_keeps_a_visible_locked_off_toggle()
    {
        var service = new ParcelProtectionService
        {
            Protection = DeclinedProtection(),
            Transaction = Transaction(
                amountSatang: 838_00,
                state: "PaymentPending")
        };
        var viewModel = ViewModel(service);

        await viewModel.LoadAsync(service.Transaction.Id);

        Assert.True(viewModel.ShowParcelProtectionToggle);
        Assert.True(viewModel.IsParcelProtectionChoiceLocked);
        Assert.False(viewModel.CanToggleParcelProtection);
        Assert.False(viewModel.IsParcelProtectionToggleOn);
        Assert.Equal(
            "ไม่เพิ่ม · เริ่มการชำระแล้ว เปลี่ยนไม่ได้",
            viewModel.ParcelProtectionToggleDetailText);
    }

    [Fact]
    public async Task Included_coverage_does_not_show_an_unnecessary_toggle()
    {
        var included = DeclinedProtection() with
        {
            AddOnAvailable = false,
            IncludedCoverageLimitSatang = 500_00
        };
        var service = new ParcelProtectionService
        {
            Protection = included,
            Transaction = Transaction(amountSatang: 456_00)
        };
        var viewModel = ViewModel(service);

        await viewModel.LoadAsync(service.Transaction.Id);

        Assert.False(viewModel.ShowParcelProtectionToggle);
        Assert.False(viewModel.CanToggleParcelProtection);
    }

    [Fact]
    public async Task Choosing_included_coverage_is_saved_before_payment()
    {
        var service = new ParcelProtectionService
        {
            Protection = ChoiceProtection(),
            PreparedProtection = ChoiceProtection(),
            Transaction = Transaction(),
            ProtectionAfterChoice = DeclinedProtection()
        };
        var sheet = new RecordingSheet();
        var viewModel = ViewModel(service, sheet);

        await viewModel.LoadAsync(service.Transaction.Id);
        await ExecuteAsync(viewModel.PrimaryActionCommand);

        Assert.Equal(1, service.ChooseCalls);
        Assert.Equal(1, sheet.Calls);
        Assert.True(viewModel.IsParcelProtectionIncludedSelected);
        Assert.False(viewModel.IsParcelProtectionChoiceVisible);
    }

    [Fact]
    public async Task Changing_choice_after_a_failed_request_uses_a_new_election_key()
    {
        var service = new ParcelProtectionService
        {
            Protection = ChoiceProtection(),
            PreparedProtection = ChoiceProtection(),
            Transaction = Transaction(amountSatang: 450_00)
        };
        service.ChooseFailures.Enqueue(
            new HttpRequestException("network unavailable"));
        var viewModel = ViewModel(service);

        await viewModel.LoadAsync(service.Transaction.Id);
        await ExecuteAsync(viewModel.ToggleParcelProtectionCommand);
        await ExecuteAsync(viewModel.ConfirmParcelProtectionCommand);
        viewModel.CancelParcelProtectionCommand.Execute(null);
        await ExecuteAsync(viewModel.ToggleParcelProtectionCommand);
        await ExecuteAsync(viewModel.ConfirmParcelProtectionCommand);

        Assert.Equal([true, true], service.Choices);
        Assert.Equal(2, service.ChooseKeys.Count);
        Assert.NotEqual(
            service.ChooseKeys[0],
            service.ChooseKeys[1]);
    }

    [Fact]
    public async Task Background_refresh_keeps_the_saved_choice_and_hides_the_modal()
    {
        var service = new ParcelProtectionService
        {
            Protection = ChoiceProtection(),
            PreparedProtection = ChoiceProtection(),
            Transaction = Transaction(amountSatang: 450_00),
            UpdatedTransaction = Transaction(amountSatang: 456_00),
            ProtectionAfterChoice = ReadyProtection()
        };
        var viewModel = ViewModel(service);

        await viewModel.LoadAsync(service.Transaction.Id);
        await ExecuteAsync(viewModel.ToggleParcelProtectionCommand);
        await ExecuteAsync(viewModel.ConfirmParcelProtectionCommand);

        await viewModel.RefreshAsync(service.Transaction.Id);

        Assert.False(viewModel.IsParcelProtectionChoiceVisible);
        Assert.True(viewModel.IsParcelProtectionAddOnSelected);
        Assert.Equal("ยืนยันและชำระ ฿456", viewModel.PaymentActionText);
        Assert.Equal(1, service.ChooseCalls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Resuming_after_a_saved_choice_does_not_prompt_or_resubmit(
        bool addProtection)
    {
        var service = new ParcelProtectionService
        {
            Protection = ChoiceProtection(),
            PreparedProtection = ChoiceProtection(),
            Transaction = Transaction(amountSatang: 450_00),
            UpdatedTransaction = Transaction(
                amountSatang: addProtection ? 456_00 : 450_00),
            ProtectionAfterChoice = addProtection
                ? ReadyProtection()
                : DeclinedProtection()
        };
        var firstViewModel = ViewModel(service);

        await firstViewModel.LoadAsync(service.Transaction.Id);
        if (addProtection)
        {
            await ExecuteAsync(
                firstViewModel.ToggleParcelProtectionCommand);
            await ExecuteAsync(
                firstViewModel.ConfirmParcelProtectionCommand);
        }
        else
        {
            await ExecuteAsync(
                firstViewModel.PrimaryActionCommand);
        }

        var resumedViewModel = ViewModel(service);
        await resumedViewModel.LoadAsync(
            service.Transaction.Id);

        Assert.Equal(1, service.ChooseCalls);
        Assert.False(
            resumedViewModel.IsParcelProtectionChoiceVisible);
        Assert.Equal(1, service.PrepareCalls);
    }

    [Fact]
    public async Task Payment_does_not_submit_a_new_choice_when_server_requires_one()
    {
        var service = new ParcelProtectionService
        {
            Protection = ReadyProtection(),
            PreparedProtection = ChoiceProtection(),
            Transaction = Transaction(amountSatang: 450_00),
            UpdatedTransaction = Transaction(amountSatang: 456_00),
            ProtectionAfterChoice = ReadyProtection()
        };
        var sheet = new RecordingSheet();
        var viewModel = ViewModel(service, sheet);

        await viewModel.LoadAsync(service.Transaction.Id);
        service.Protection = ChoiceProtection();

        await ExecuteAsync(viewModel.PrimaryActionCommand);

        Assert.Equal(0, service.PrepareCalls);
        Assert.Equal(1, service.ChooseCalls);
        Assert.Equal(1, sheet.Calls);
        Assert.False(viewModel.IsParcelProtectionChoiceVisible);
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
        await ExecuteAsync(viewModel.PrimaryActionCommand);

        Assert.True(viewModel.IsParcelProtectionChoiceVisible);
        Assert.True(viewModel.IsParcelProtectionUnavailable);
        Assert.False(viewModel.CanToggleParcelProtection);
        Assert.True(viewModel.CanConfirmParcelProtection);
        Assert.False(viewModel.HasParcelProtectionOfferDetails);
        Assert.Equal("", viewModel.MaximumCoverageText);
        Assert.Equal("", viewModel.ParcelProtectionPriceText);
        Assert.Equal(0, service.ChooseCalls);
        Assert.Equal(0, sheet.Calls);
    }

    [Fact]
    public async Task Changed_price_requires_modal_confirmation_before_saving()
    {
        var changed = ChoiceProtection() with
        {
            ReconfirmationRequired = true,
            Election = "ReconfirmationRequired",
            CustomerPriceSatang = 700,
            OptionReference = "option-2"
        };
        var service = new ParcelProtectionService
        {
            Protection = changed,
            PreparedProtection = changed with
            {
                ReconfirmationRequired = false,
                Election = "Pending"
            },
            Transaction = Transaction(amountSatang: 450_00),
            UpdatedTransaction = Transaction(amountSatang: 457_00),
            ProtectionAfterChoice = ReadyProtection() with
            {
                CustomerPriceSatang = 700,
                OptionReference = "option-2"
            }
        };
        var viewModel = ViewModel(service);

        await viewModel.LoadAsync(service.Transaction.Id);

        Assert.True(viewModel.IsParcelProtectionChoiceVisible);
        Assert.True(viewModel.IsParcelProtectionToggleOn);
        Assert.Equal("+฿7", viewModel.ParcelProtectionPriceAmountText);
        Assert.Equal(0, service.ChooseCalls);

        await ExecuteAsync(viewModel.ConfirmParcelProtectionCommand);

        Assert.Equal([true], service.Choices);
        Assert.False(viewModel.IsParcelProtectionChoiceVisible);
        Assert.Equal("฿457", viewModel.CheckoutAmountText);
    }

    [Fact]
    public async Task Background_price_change_reopens_confirmation_and_cancel_restores_saved_add_on()
    {
        var changed = ChoiceProtection() with
        {
            CustomerPriceSatang = 700,
            OptionReference = "option-2"
        };
        var service = new ParcelProtectionService
        {
            Protection = ReadyProtection(),
            PreparedProtection = changed,
            Transaction = Transaction(amountSatang: 456_00)
        };
        var viewModel = ViewModel(service);

        await viewModel.LoadAsync(service.Transaction.Id);
        service.Protection = changed with
        {
            Election = "ReconfirmationRequired",
            ReconfirmationRequired = true
        };

        await viewModel.RefreshAsync(service.Transaction.Id);

        Assert.True(viewModel.IsParcelProtectionChoiceVisible);
        Assert.True(viewModel.IsParcelProtectionToggleOn);
        Assert.Equal("+฿7", viewModel.ParcelProtectionPriceAmountText);

        viewModel.CancelParcelProtectionCommand.Execute(null);

        Assert.False(viewModel.IsParcelProtectionChoiceVisible);
        Assert.True(viewModel.IsParcelProtectionToggleOn);
        Assert.Equal("฿456", viewModel.CheckoutAmountText);
        Assert.Equal(0, service.ChooseCalls);
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

        Assert.Equal(0, service.PrepareCalls);
        Assert.True(viewModel.IsParcelProtectionChoiceVisible);
        Assert.True(viewModel.IsParcelProtectionIncludedSelected);
    }

    [Fact]
    public async Task Initial_load_uses_neutral_loading_then_reveals_the_role()
    {
        var transaction = Transaction(state: "InTransit");
        var response = new TaskCompletionSource<AppTransaction?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new ParcelProtectionService
        {
            Transaction = transaction,
            Protection = ChoiceProtection(),
            TransactionResponse = response
        };
        var viewModel = ViewModel(service);

        var loading = viewModel.LoadAsync(transaction.Id);

        Assert.True(viewModel.IsBusy);
        Assert.True(viewModel.ShowInitialLoading);
        Assert.False(viewModel.HasTransaction);
        Assert.Equal("รายการ", viewModel.DetailHeadline);

        response.SetResult(transaction);
        await loading;

        Assert.False(viewModel.ShowInitialLoading);
        Assert.True(viewModel.HasTransaction);
        Assert.Equal("รายการซื้อ", viewModel.DetailHeadline);
        Assert.True(viewModel.IsBuyerDetail);
        Assert.False(viewModel.IsSellerDetail);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Initial_missing_or_failed_load_shows_message_without_stale_content(
        bool fail)
    {
        var service = new ParcelProtectionService
        {
            Transaction = Transaction(state: "InTransit"),
            Protection = ChoiceProtection(),
            ReturnMissingTransaction = !fail,
            TransactionFailure = fail
                ? new InvalidOperationException("โหลดรายการไม่สำเร็จ")
                : null
        };
        var viewModel = ViewModel(service);

        await viewModel.LoadAsync(service.Transaction.Id);

        Assert.False(viewModel.IsBusy);
        Assert.False(viewModel.HasTransaction);
        Assert.False(viewModel.ShowInitialLoading);
        Assert.True(viewModel.ShowInitialMessage);
        Assert.Equal(
            fail ? "โหลดรายการไม่สำเร็จ" : "ไม่พบรายการนี้",
            viewModel.Message);
        Assert.Equal("รายการ", viewModel.DetailHeadline);
    }

    [Theory]
    [InlineData("InTransit", true, false, true)]
    [InlineData("InTransit", false, true, true)]
    [InlineData("InTransit", false, false, false)]
    [InlineData("PaidAwaitingShipment", false, true, false)]
    public async Task Shipping_status_details_require_view_status_and_a_resource(
        string state,
        bool hasTracking,
        bool hasLabel,
        bool expected)
    {
        var transaction = Transaction(state: state) with
        {
            Role = hasLabel
                ? AppTransactionRole.Seller
                : AppTransactionRole.Buyer,
            TrackingNumber = hasTracking ? "TH123456789" : null,
            ShippingLabelAvailable = hasLabel
        };
        var service = new ParcelProtectionService
        {
            Transaction = transaction,
            Protection = ChoiceProtection()
        };
        var viewModel = ViewModel(service);

        await viewModel.LoadAsync(transaction.Id);

        Assert.Equal(expected, viewModel.ShowShippingStatusDetails);
    }

    private static TransactionDetailViewModel ViewModel(
        ParcelProtectionService service,
        IStripePaymentSheetService? sheet = null,
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

    private static AppTransaction Transaction(
        long amountSatang = 456_00,
        string state = "SellerAcceptedAwaitingPayment") =>
        new(
            Guid.NewGuid(), "กล้อง", amountSatang, "THB",
            AppTransactionRole.Buyer, AppFulfillmentType.Physical,
            state,
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

    private static BuyerParcelProtection DeclinedProtection() =>
        ChoiceProtection() with
        {
            Election = "Declined",
            BookingReady = false,
            RequiresChoice = false,
            CustomerPriceSatang = null,
            OptionReference = null
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

    private sealed class ControlledSheet : IStripePaymentSheetService
    {
        private readonly TaskCompletionSource<PaymentSheetOutcome>
            completion = new(
                TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Started { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<PaymentSheetOutcome> PresentAsync(
            Guid transactionId,
            string idempotencyKey,
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            return await completion.Task.WaitAsync(
                cancellationToken);
        }

        public void Complete(PaymentSheetOutcome outcome) =>
            completion.TrySetResult(outcome);
    }

    private sealed class RecordingAnalytics : IMobileAnalytics
    {
        public List<MobileAnalyticsEvent> Events { get; } = [];

        public void Track(MobileAnalyticsEvent value) => Events.Add(value);
    }

    private sealed class ParcelProtectionService : ITransactionService
    {
        public required AppTransaction Transaction { get; set; }
        public AppTransaction? UpdatedTransaction { get; init; }
        public required BuyerParcelProtection Protection { get; set; }
        public BuyerParcelProtection? PreparedProtection { get; init; }
        public BuyerParcelProtection? ProtectionAfterChoice { get; init; }
        public Queue<Exception> ChooseFailures { get; } = [];
        public Queue<Exception> PrepareFailures { get; } = [];
        public Queue<string> ChooseStatuses { get; } = [];
        public TaskCompletionSource<AppTransaction?>? TransactionResponse { get; init; }
        public Exception? TransactionFailure { get; init; }
        public bool ReturnMissingTransaction { get; init; }
        public List<bool> Choices { get; } = [];
        public List<string> ChooseKeys { get; } = [];
        public int GetProtectionCalls { get; private set; }
        public int GetTransactionCalls { get; private set; }
        public int PrepareCalls { get; private set; }
        public int ChooseCalls { get; private set; }
        public Task<AppTransaction?> GetTransactionAsync(
            Guid transactionId,
            CancellationToken cancellationToken = default)
        {
            GetTransactionCalls++;
            if (TransactionFailure is not null)
                throw TransactionFailure;
            if (TransactionResponse is not null)
                return TransactionResponse.Task;
            if (ReturnMissingTransaction)
                return Task.FromResult<AppTransaction?>(null);
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
            if (PrepareFailures.TryDequeue(
                    out var failure))
                throw failure;
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
            Choices.Add(addProtection);
            ChooseKeys.Add(idempotencyKey);
            if (ChooseFailures.TryDequeue(
                    out var failure))
                throw failure;
            Protection = ProtectionAfterChoice ?? Protection;
            return Task.FromResult(
                ChooseStatuses.TryDequeue(out var status)
                    ? status
                    : "preparing");
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
