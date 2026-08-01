using System.Collections.ObjectModel;
using System.Windows.Input;
using Toklong.Mobile.Core;
using Toklong.Mobile.Pages;

namespace Toklong.Mobile.ViewModels;

public sealed class TransactionDetailViewModel(
    ITransactionService transactionService,
    IStripePaymentSheetService stripePaymentSheet,
    IMobileAnalytics analytics) : ObservableViewModel
{
    private AppTransaction? transaction;
    private string message = "";
    private string invitationFeedback = "";
    private bool isBusy;
    private bool acceptedTerms;
    private CarrierOption? selectedCarrier;
    private string trackingNumber = "";
    private string digitalHandoffStatement = "";
    private bool isAgreementDetailsExpanded;
    private bool isProblemFormExpanded;
    private DisputeReasonOption selectedDisputeReason =
        DisputeReasonOption.All[^1];
    private string disputeStatement = "";
    private DisputeEvidenceTypeOption selectedEvidenceType =
        DisputeEvidenceTypeOption.All[0];
    private string evidenceDescription = "";
    private bool carrierDataLoaded;
    private BuyerParcelProtection? parcelProtection;
    private bool isParcelProtectionChoiceVisible;
    private bool? selectedParcelProtection;
    private bool? parcelProtectionSelectionBeforeModal;
    private string? parcelProtectionIdempotencyKey;
    private bool? parcelProtectionIdempotencySelection;
    private string? parcelProtectionPreparationIdempotencyKey;
    private string? checkoutIdempotencyKey;
    private bool parcelProtectionOfferedTracked;
    private bool isPaymentSheetOpening;

    public AppTransaction? Transaction
    {
        get => transaction;
        private set
        {
            var transactionChanged =
                transaction?.Id != value?.Id;
            if (SetProperty(ref transaction, value))
            {
                if (transactionChanged)
                {
                    IsProblemFormExpanded = false;
                    DisputeStatement = "";
                    SelectedDisputeReason =
                        DisputeReasonOption.All[^1];
                    selectedParcelProtection = null;
                    parcelProtectionSelectionBeforeModal = null;
                    parcelProtectionIdempotencyKey = null;
                    parcelProtectionIdempotencySelection = null;
                    parcelProtectionPreparationIdempotencyKey =
                        null;
                    checkoutIdempotencyKey = null;
                    parcelProtectionOfferedTracked = false;
                }
                IsAgreementDetailsExpanded =
                    value?.Role != AppTransactionRole.Seller;
                OnPropertyChanged(nameof(IsPaymentAction));
                OnPropertyChanged(nameof(ShowSellerInvitation));
                OnPropertyChanged(nameof(SellerInvitationUrl));
                OnPropertyChanged(nameof(IsFulfillmentAction));
                OnPropertyChanged(nameof(IsPhysicalFulfillmentAction));
                OnPropertyChanged(nameof(IsDigitalFulfillmentAction));
                OnPropertyChanged(nameof(BuyerConfirmation));
                OnPropertyChanged(nameof(IsBuyerConfirmationAction));
                OnPropertyChanged(nameof(ProblemFormToggleText));
                OnPropertyChanged(nameof(IsStatusOnly));
                OnPropertyChanged(nameof(HasTransaction));
                OnPropertyChanged(nameof(ShowInitialLoading));
                OnPropertyChanged(nameof(ShowInitialMessage));
                OnPropertyChanged(nameof(ShowShippingStatusDetails));
                OnPropertyChanged(nameof(DetailHeadline));
                OnPropertyChanged(nameof(IsBuyerDetail));
                OnPropertyChanged(nameof(CanDownloadAgreementEvidence));
                OnPropertyChanged(nameof(CanDownloadShippingLabel));
                OnPropertyChanged(
                    nameof(CanDownloadReturnShippingLabel));
                OnPropertyChanged(nameof(IsSellerDetail));
                OnPropertyChanged(nameof(ShowAgreementDetailsContent));
                OnPropertyChanged(nameof(CanManageDisputeEvidence));
                OnPropertyChanged(nameof(CanChangeParcelProtection));
                OnPropertyChanged(nameof(ShowParcelProtectionToggle));
                OnPropertyChanged(nameof(CanToggleParcelProtection));
                OnPropertyChanged(nameof(IsParcelProtectionChoiceLocked));
                NotifyCheckoutPresentationChanged();
            }
        }
    }

    public string Message
    {
        get => message;
        private set
        {
            if (SetProperty(ref message, value))
            {
                OnPropertyChanged(nameof(HasMessage));
                OnPropertyChanged(nameof(ShowInitialLoading));
                OnPropertyChanged(nameof(ShowInitialMessage));
            }
        }
    }

    public bool HasMessage => !string.IsNullOrWhiteSpace(Message);

    public bool CanDownloadAgreementEvidence =>
        Transaction?.HasAgreementEvidence == true &&
        Transaction.SellerAcceptedAt.HasValue &&
        Transaction.BuyerAcceptedAt.HasValue;

    public bool CanDownloadShippingLabel =>
        Transaction?.Role == AppTransactionRole.Seller &&
        Transaction.ShippingLabelAvailable;

    public bool CanDownloadReturnShippingLabel =>
        Transaction?.Role == AppTransactionRole.Buyer &&
        Transaction.ReturnShippingLabelAvailable;

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (SetProperty(ref isBusy, value))
            {
                OnPropertyChanged(nameof(CanSubmitTracking));
                OnPropertyChanged(nameof(CanStartPayment));
                OnPropertyChanged(nameof(CanToggleParcelProtection));
                OnPropertyChanged(nameof(CanConfirmParcelProtection));
                OnPropertyChanged(nameof(CanCancelParcelProtection));
                OnPropertyChanged(nameof(ShowInitialLoading));
                OnPropertyChanged(nameof(ShowInitialMessage));
            }
        }
    }

    public bool IsPaymentAction =>
        Transaction?.Presentation.PrimaryAction == TransactionAction.ReviewAndPay;

    public bool IsPaymentSheetOpening
    {
        get => isPaymentSheetOpening;
        private set => SetProperty(
            ref isPaymentSheetOpening,
            value);
    }

    public BuyerParcelProtection? ParcelProtection
    {
        get => parcelProtection;
        private set
        {
            var previousOptionReference =
                parcelProtection?.OptionReference;
            if (!SetProperty(ref parcelProtection, value))
                return;
            switch (value?.Election)
            {
                case "Accepted":
                    selectedParcelProtection = true;
                    break;
                case "Declined":
                    selectedParcelProtection = false;
                    break;
                case "ReconfirmationRequired":
                    selectedParcelProtection = null;
                    break;
                default:
                    if (previousOptionReference is not null &&
                    !string.Equals(
                        previousOptionReference,
                        value?.OptionReference,
                        StringComparison.Ordinal))
                    {
                        selectedParcelProtection = null;
                    }
                    break;
            }
            OnPropertyChanged(nameof(MaximumCoverageText));
            OnPropertyChanged(nameof(ParcelProtectionPriceText));
            OnPropertyChanged(nameof(ParcelProtectionPriceAmountText));
            OnPropertyChanged(nameof(IsParcelProtectionUnavailable));
            OnPropertyChanged(nameof(IsParcelProtectionChoiceAvailable));
            OnPropertyChanged(nameof(HasParcelProtectionOfferDetails));
            OnPropertyChanged(nameof(ParcelProtectionPrimaryActionText));
            OnPropertyChanged(nameof(ParcelProtectionDeclineActionText));
            OnPropertyChanged(nameof(CanChangeParcelProtection));
            OnPropertyChanged(nameof(ShowParcelProtectionToggle));
            OnPropertyChanged(nameof(CanToggleParcelProtection));
            OnPropertyChanged(nameof(IsParcelProtectionChoiceLocked));
            OnPropertyChanged(nameof(ParcelProtectionToggleDetailText));
            OnPropertyChanged(nameof(ParcelProtectionModalTitle));
            OnPropertyChanged(nameof(ParcelProtectionModalDescription));
            NotifyCheckoutPresentationChanged();
        }
    }

    public bool IsParcelProtectionChoiceVisible
    {
        get => isParcelProtectionChoiceVisible;
        private set
        {
            if (SetProperty(
                    ref isParcelProtectionChoiceVisible,
                    value))
            {
                OnPropertyChanged(nameof(CanStartPayment));
                OnPropertyChanged(nameof(CanToggleParcelProtection));
                OnPropertyChanged(nameof(CanConfirmParcelProtection));
                OnPropertyChanged(nameof(CanCancelParcelProtection));
            }
        }
    }

    public string MaximumCoverageText =>
        ParcelProtection?.MaximumCoverageLimitSatang is { } limit
            ? $"คุ้มครองสูงสุด {MoneyFormatter.Format(
                limit,
                Transaction?.Currency ?? "THB")}" : "";

    public string ParcelProtectionPriceText =>
        ParcelProtection?.CustomerPriceSatang is { } price
            ? $"เพิ่มความคุ้มครอง {MoneyFormatter.Format(
                price,
                Transaction?.Currency ?? "THB")}" : "";

    public string ParcelProtectionPriceAmountText =>
        ParcelProtection?.CustomerPriceSatang is { } price
            ? $"+{MoneyFormatter.Format(
                price,
                Transaction?.Currency ?? "THB")}" : "";

    public bool IsParcelProtectionAddOnSelected =>
        selectedParcelProtection == true;

    public bool IsParcelProtectionIncludedSelected =>
        selectedParcelProtection == false;

    public bool IsParcelProtectionToggleOn =>
        IsParcelProtectionAddOnSelected;

    public bool ShowParcelProtectionToggle =>
        Transaction is
        {
            Role: AppTransactionRole.Buyer,
            FulfillmentType: AppFulfillmentType.Physical,
            State: "SellerAcceptedAwaitingPayment" or
                "CheckoutStarted" or "PaymentPending"
        } &&
        ParcelProtection is not null &&
        (ParcelProtection.AddOnAvailable ||
         ParcelProtection.ReconfirmationRequired ||
         ParcelProtection.Election == "Accepted" ||
         Transaction.ItemPriceSatang >
            ParcelProtection.IncludedCoverageLimitSatang);

    private bool IsParcelProtectionEditableState =>
        Transaction is
        {
            Role: AppTransactionRole.Buyer,
            FulfillmentType: AppFulfillmentType.Physical,
            State: "SellerAcceptedAwaitingPayment"
        };

    public bool CanToggleParcelProtection =>
        ShowParcelProtectionToggle &&
        IsParcelProtectionEditableState &&
        !IsBusy &&
        !IsParcelProtectionChoiceVisible &&
        !IsParcelProtectionUnavailable;

    public bool IsParcelProtectionChoiceLocked =>
        ShowParcelProtectionToggle &&
        !IsParcelProtectionEditableState;

    public bool CanConfirmParcelProtection =>
        IsParcelProtectionChoiceVisible &&
        selectedParcelProtection.HasValue &&
        !IsBusy &&
        (selectedParcelProtection == false ||
         IsParcelProtectionChoiceAvailable);

    public bool CanCancelParcelProtection =>
        IsParcelProtectionChoiceVisible && !IsBusy;

    public string ParcelProtectionToggleDetailText =>
        IsParcelProtectionChoiceLocked
            ? IsParcelProtectionToggleOn
                ? "เพิ่มแล้ว · เริ่มการชำระแล้ว เปลี่ยนไม่ได้"
                : "ไม่เพิ่ม · เริ่มการชำระแล้ว เปลี่ยนไม่ได้"
            : IsParcelProtectionToggleOn
            ? "เลือกเพิ่มแล้ว · แตะเพื่อเปลี่ยน"
            : IsParcelProtectionUnavailable
                ? "ไม่มีวงเงินเสริม ใช้วงเงินที่รวมมา"
                : "เปิดเพื่อดูวงเงิน ราคา และยืนยัน";

    public string ParcelProtectionModalTitle =>
        IsParcelProtectionAddOnSelected
            ? "ยืนยันเพิ่มความคุ้มครองพัสดุ"
            : "ยืนยันไม่เพิ่มความคุ้มครอง";

    public string ParcelProtectionModalDescription =>
        IsParcelProtectionAddOnSelected
            ? "ตรวจวงเงินและราคาก่อนเพิ่มในยอดชำระ"
            : "ยอดชำระจะไม่รวมค่าความคุ้มครองพัสดุเพิ่มเติม";

    public string CheckoutAmountText
    {
        get
        {
            if (Transaction is null)
                return "";

            var amount = Transaction.AmountSatang;
            if (IsParcelProtectionChoiceVisible &&
                selectedParcelProtection.HasValue)
            {
                amount -= Transaction.ParcelInsuranceFeeSatang;
                if (selectedParcelProtection == true)
                    amount += ParcelProtection?.CustomerPriceSatang ?? 0;
            }

            return MoneyFormatter.Format(
                amount,
                Transaction.Currency);
        }
    }

    public string PaymentActionText =>
        $"ชำระ {CheckoutAmountText}";

    public string PaymentSemanticDescription =>
        $"เปิดหน้าจ่ายเงินยอด {CheckoutAmountText}";

    public bool CanStartPayment =>
        !IsBusy &&
        AcceptedTerms &&
        !IsParcelProtectionChoiceVisible;

    public bool IsParcelProtectionUnavailable =>
        ParcelProtection is
        {
            ReconfirmationRequired: true,
            AddOnAvailable: false
        };

    public bool IsParcelProtectionChoiceAvailable =>
        !IsParcelProtectionUnavailable;

    public bool HasParcelProtectionOfferDetails =>
        !IsParcelProtectionUnavailable &&
        ParcelProtection?.MaximumCoverageLimitSatang is not null &&
        ParcelProtection.CustomerPriceSatang is not null;

    public string ParcelProtectionPrimaryActionText =>
        IsParcelProtectionUnavailable
            ? "ดำเนินการต่อด้วยวงเงินที่รวมอยู่"
            : ParcelProtectionPriceText;

    public string ParcelProtectionDeclineActionText =>
        IsParcelProtectionUnavailable
            ? "กลับไปตรวจรายละเอียด"
            : "ไม่เพิ่มความคุ้มครอง";

    public bool CanChangeParcelProtection =>
        IsParcelProtectionEditableState &&
        ParcelProtection is
        {
            Election: not "Pending" and not "ReconfirmationRequired"
        };

    public bool ShowSellerInvitation =>
        Transaction?.Role == AppTransactionRole.Buyer &&
        Transaction.State == "AwaitingSellerAcceptance" &&
        !string.IsNullOrWhiteSpace(
            Transaction.SellerInvitationUrl);

    public string SellerInvitationUrl =>
        Transaction?.SellerInvitationUrl ?? "";

    public string InvitationFeedback
    {
        get => invitationFeedback;
        private set
        {
            if (SetProperty(ref invitationFeedback, value))
                OnPropertyChanged(nameof(HasInvitationFeedback));
        }
    }

    public bool HasInvitationFeedback =>
        !string.IsNullOrWhiteSpace(InvitationFeedback);

    public bool IsFulfillmentAction =>
        Transaction?.Presentation.PrimaryAction is
            TransactionAction.AddTracking or
            TransactionAction.ConfirmDigitalHandoff;

    public bool IsPhysicalFulfillmentAction =>
        Transaction?.Presentation.PrimaryAction ==
        TransactionAction.AddTracking;

    public bool IsDigitalFulfillmentAction =>
        Transaction?.Presentation.PrimaryAction ==
        TransactionAction.ConfirmDigitalHandoff;

    public BuyerReceiptConfirmationPresentation?
        BuyerConfirmation =>
            BuyerReceiptConfirmationPresenter.Present(
                Transaction);

    public bool IsBuyerConfirmationAction =>
        BuyerConfirmation is not null;

    public bool IsStatusOnly =>
        Transaction?.Presentation.PrimaryAction == TransactionAction.ViewStatus;

    public bool HasTransaction => Transaction is not null;

    public bool ShowInitialLoading =>
        !HasTransaction && IsBusy && !HasMessage;

    public bool ShowInitialMessage =>
        !HasTransaction && !IsBusy && HasMessage;

    public bool ShowShippingStatusDetails =>
        IsStatusOnly &&
        (CanDownloadShippingLabel ||
         Transaction?.HasTrackingNumber == true);

    public string DetailHeadline => Transaction?.Role switch
    {
        AppTransactionRole.Buyer => "รายการซื้อ",
        AppTransactionRole.Seller => "รายการขาย",
        _ => "รายการ"
    };

    public bool IsSellerDetail =>
        Transaction?.Role == AppTransactionRole.Seller;

    public bool IsBuyerDetail =>
        Transaction?.Role == AppTransactionRole.Buyer;

    public bool IsAgreementDetailsExpanded
    {
        get => isAgreementDetailsExpanded;
        private set
        {
            if (SetProperty(ref isAgreementDetailsExpanded, value))
            {
                OnPropertyChanged(nameof(ShowAgreementDetailsContent));
                OnPropertyChanged(nameof(AgreementDetailsChevron));
            }
        }
    }

    public bool ShowAgreementDetailsContent =>
        !IsSellerDetail || IsAgreementDetailsExpanded;

    public string AgreementDetailsChevron =>
        IsAgreementDetailsExpanded ? "⌃" : "⌄";

    public bool IsProblemFormExpanded
    {
        get => isProblemFormExpanded;
        private set
        {
            if (SetProperty(
                    ref isProblemFormExpanded,
                    value))
                OnPropertyChanged(
                    nameof(ProblemFormToggleText));
        }
    }

    public string ProblemFormToggleText =>
        IsProblemFormExpanded
            ? "ปิดแบบฟอร์ม"
            : BuyerConfirmation?.ProblemActionText ?? "";

    public ObservableCollection<CarrierOption> Carriers { get; } = [];

    public bool AcceptedTerms
    {
        get => acceptedTerms;
        set
        {
            if (SetProperty(ref acceptedTerms, value))
                OnPropertyChanged(nameof(CanStartPayment));
        }
    }

    public CarrierOption? SelectedCarrier
    {
        get => selectedCarrier;
        set
        {
            if (!SetProperty(ref selectedCarrier, value))
                return;
            TrackingNumber = value?.NormalizeTracking(TrackingNumber)
                ?? TrackingNumber;
            OnPropertyChanged(nameof(TrackingHint));
            OnPropertyChanged(nameof(TrackingPlaceholder));
            OnPropertyChanged(nameof(TrackingValidationMessage));
            OnPropertyChanged(nameof(HasTrackingValidationMessage));
            OnPropertyChanged(nameof(HasTrackingHint));
            OnPropertyChanged(nameof(CanSubmitTracking));
        }
    }

    public string TrackingNumber
    {
        get => trackingNumber;
        set
        {
            var normalized = SelectedCarrier?.NormalizeTracking(value)
                ?? new((value ?? "")
                    .Where(char.IsAsciiLetterOrDigit)
                    .Select(char.ToUpperInvariant)
                    .Take(20)
                    .ToArray());
            if (!SetProperty(ref trackingNumber, normalized))
                return;
            OnPropertyChanged(nameof(TrackingValidationMessage));
            OnPropertyChanged(nameof(HasTrackingValidationMessage));
            OnPropertyChanged(nameof(HasTrackingHint));
            OnPropertyChanged(nameof(CanSubmitTracking));
        }
    }

    public string TrackingHint => SelectedCarrier?.TrackingHint
        ?? "เลือกบริษัทขนส่งก่อนกรอกเลขพัสดุ";

    public string TrackingPlaceholder => SelectedCarrier?.Placeholder
        ?? "เลขพัสดุ";

    public string TrackingValidationMessage =>
        SelectedCarrier is not null &&
        !string.IsNullOrEmpty(TrackingNumber) &&
        !SelectedCarrier.IsValidTrackingNumber(TrackingNumber)
            ? SelectedCarrier.ValidationMessage
            : "";

    public bool HasTrackingValidationMessage =>
        !string.IsNullOrEmpty(TrackingValidationMessage);

    public bool HasTrackingHint => !HasTrackingValidationMessage;

    public bool CanSubmitTracking =>
        SelectedCarrier is not null &&
        SelectedCarrier.IsValidTrackingNumber(TrackingNumber) &&
        !IsBusy;

    public string DigitalHandoffStatement
    {
        get => digitalHandoffStatement;
        set => SetProperty(ref digitalHandoffStatement, value);
    }

    public IReadOnlyList<DisputeReasonOption> DisputeReasons =>
        DisputeReasonOption.All;

    public DisputeReasonOption SelectedDisputeReason
    {
        get => selectedDisputeReason;
        set => SetProperty(ref selectedDisputeReason, value);
    }

    public string DisputeStatement
    {
        get => disputeStatement;
        set => SetProperty(ref disputeStatement, value);
    }

    public bool CanManageDisputeEvidence =>
        Transaction is
        {
            State: "Disputed" or "ResolutionPending",
            Role: AppTransactionRole.Buyer or
                AppTransactionRole.Seller
        };

    public IReadOnlyList<DisputeEvidenceTypeOption>
        EvidenceTypes => DisputeEvidenceTypeOption.All;

    public DisputeEvidenceTypeOption SelectedEvidenceType
    {
        get => selectedEvidenceType;
        set => SetProperty(ref selectedEvidenceType, value);
    }

    public string EvidenceDescription
    {
        get => evidenceDescription;
        set => SetProperty(ref evidenceDescription, value);
    }

    public ObservableCollection<DisputeEvidenceSummary>
        DisputeEvidence
    { get; } = [];

    public bool HasDisputeEvidence =>
        DisputeEvidence.Count > 0;

    public ICommand PrimaryActionCommand => new AsyncCommand(ExecutePrimaryActionAsync);

    public ICommand ToggleParcelProtectionCommand =>
        new AsyncCommand(ToggleParcelProtectionAsync);

    public ICommand ConfirmParcelProtectionCommand =>
        new AsyncCommand(ConfirmParcelProtectionAsync);

    public ICommand CancelParcelProtectionCommand =>
        new Command(CancelParcelProtection);

    public ICommand ChangeParcelProtectionCommand =>
        new AsyncCommand(ChangeParcelProtectionAsync);

    public ICommand ToggleAgreementDetailsCommand =>
        new Command(() =>
        {
            if (IsSellerDetail)
                IsAgreementDetailsExpanded =
                    !IsAgreementDetailsExpanded;
        });

    public ICommand CopyInvitationLinkCommand =>
        new AsyncCommand(CopyInvitationLinkAsync);

    public ICommand ShareInvitationLinkCommand =>
        new AsyncCommand(ShareInvitationLinkAsync);

    public ICommand ConfirmReceiptCommand =>
        new AsyncCommand(ConfirmReceiptAsync);

    public ICommand ToggleProblemFormCommand =>
        new Command(() =>
            IsProblemFormExpanded =
                !IsProblemFormExpanded);

    public ICommand ReportProblemCommand =>
        new AsyncCommand(ReportProblemAsync);

    public ICommand AddDisputeEvidenceCommand =>
        new AsyncCommand(AddDisputeEvidenceAsync);

    public ICommand DownloadAgreementEvidenceCommand =>
        new AsyncCommand(DownloadAgreementEvidenceAsync);

    public ICommand OpenShippingLabelCommand =>
        new AsyncCommand(OpenShippingLabelAsync);

    public ICommand OpenReturnShippingLabelCommand =>
        new AsyncCommand(OpenReturnShippingLabelAsync);

    public Task LoadAsync(Guid transactionId) =>
        LoadCoreAsync(transactionId, showBusy: true);

    public Task RefreshAsync(Guid transactionId) =>
        LoadCoreAsync(transactionId, showBusy: false);

    private async Task LoadCoreAsync(
        Guid transactionId,
        bool showBusy)
    {
        if (IsBusy)
            return;
        if (showBusy)
        {
            IsBusy = true;
            Transaction = null;
            Message = "";
        }
        try
        {
            Transaction = await transactionService.GetTransactionAsync(transactionId);
            Message = Transaction is null ? "ไม่พบรายการนี้" : "";
            if (CanLoadParcelProtection())
            {
                ParcelProtection = await transactionService
                    .GetParcelProtectionAsync(transactionId);
                if (showBusy)
                    await InitializeParcelProtectionOnEntryAsync(
                        transactionId);
                else if (ParcelProtection.ReconfirmationRequired)
                {
                    await RefreshParcelProtectionForReconfirmationAsync(
                        transactionId);
                    OpenParcelProtectionModal(
                        ParcelProtection.AddOnAvailable);
                }
                else if (ParcelProtection.Election is
                    "Accepted" or "Declined")
                    IsParcelProtectionChoiceVisible = false;
            }
            if (CanManageDisputeEvidence)
                await LoadDisputeEvidenceAsync();
            if (IsPhysicalFulfillmentAction && !carrierDataLoaded)
            {
                await LoadCarrierDataAsync();
                carrierDataLoaded = true;
            }
        }
        catch (Exception exception)
        {
            Message = showBusy
                ? exception.Message
                : $"อัปเดตสถานะไม่สำเร็จ · {exception.Message}";
        }
        finally
        {
            if (showBusy)
                IsBusy = false;
        }
    }

    private async Task ExecutePrimaryActionAsync()
    {
        if (Transaction is null)
            return;

        if (Transaction.Presentation.PrimaryAction == TransactionAction.ReviewAndPay)
        {
            var transactionId = Transaction.Id;

            if (!AcceptedTerms)
            {
                Message = "กดยืนยันก่อนว่าตรวจรายละเอียดและเงื่อนไขแล้ว";
                return;
            }

            IsPaymentSheetOpening = true;
            Message =
                "กำลังเตรียมการจัดส่งและเปิดหน้าจ่ายเงิน…";
            try
            {
                await StartPaymentAsync(transactionId);
            }
            catch (Exception exception)
            {
                Message = exception.Message;
            }
            finally
            {
                IsPaymentSheetOpening = false;
            }
            return;
        }

        if (Transaction.Presentation.PrimaryAction ==
            TransactionAction.AddTracking)
        {
            if (SelectedCarrier is null)
            {
                Message = "กรุณาเลือกบริษัทขนส่ง";
                return;
            }
            if (!SelectedCarrier.IsValidTrackingNumber(TrackingNumber))
            {
                Message = SelectedCarrier.ValidationMessage;
                return;
            }
        }

        IsBusy = true;
        Message = "";
        try
        {
            var action = Transaction.Presentation.PrimaryAction;
            Transaction = action switch
            {
                TransactionAction.AddTracking =>
                    await transactionService.SubmitTrackingAsync(
                        Transaction.Id,
                        SelectedCarrier!.Code,
                        TrackingNumber),
                TransactionAction.ConfirmDigitalHandoff =>
                    await transactionService.SubmitDigitalHandoffAsync(
                        Transaction.Id,
                        DigitalHandoffStatement),
                _ => Transaction
            };
            Message = action switch
            {
                TransactionAction.AddTracking =>
                    "เพิ่มเลขพัสดุแล้ว กำลังรอบริษัทขนส่งตรวจสอบ",
                TransactionAction.ConfirmDigitalHandoff =>
                    "บันทึกการส่งมอบแล้ว รอผู้ซื้อยืนยัน",
                _ => ""
            };
        }
        catch (Exception exception)
        {
            Message = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task StartPaymentAsync(Guid transactionId)
    {
        if (IsParcelProtectionChoiceVisible)
        {
            Message =
                "เลือกความคุ้มครองพัสดุให้เสร็จก่อนชำระเงิน";
            return;
        }

        var persisted = await transactionService.GetParcelProtectionAsync(
            transactionId);
        ParcelProtection = persisted;

        switch (ParcelProtectionCheckoutPresentation.Next(persisted))
        {
            case ParcelProtectionCheckoutStep.Reconfirm:
                await RefreshParcelProtectionForReconfirmationAsync(
                    transactionId);
                OpenParcelProtectionModal(
                    ParcelProtection?.AddOnAvailable == true);
                return;
            case ParcelProtectionCheckoutStep.Choose
                when HasParcelProtectionOfferDetails:
                TrackParcelProtectionOffered();
                await ChooseParcelProtectionAsync(
                    IsParcelProtectionAddOnSelected,
                    continueToPayment: true);
                return;
            case ParcelProtectionCheckoutStep.PresentPayment:
                await PresentPaymentSheetAsync(transactionId);
                return;
        }

        var prepared = await transactionService.PrepareParcelProtectionAsync(
            transactionId,
            NewParcelProtectionPreparationIdempotencyKey());
        ParcelProtection = prepared;
        await ContinuePreparedParcelProtectionAsync(transactionId, prepared);
    }

    private async Task ContinuePreparedParcelProtectionAsync(
        Guid transactionId,
        BuyerParcelProtection protection)
    {
        switch (ParcelProtectionCheckoutPresentation.Next(protection))
        {
            case ParcelProtectionCheckoutStep.Reconfirm:
                await RefreshParcelProtectionForReconfirmationAsync(
                    transactionId);
                OpenParcelProtectionModal(
                    ParcelProtection?.AddOnAvailable == true);
                return;
            case ParcelProtectionCheckoutStep.Choose:
                TrackParcelProtectionOffered();
                await ChooseParcelProtectionAsync(
                    IsParcelProtectionAddOnSelected,
                    continueToPayment: true);
                return;
            case ParcelProtectionCheckoutStep.PresentPayment:
                await PresentPaymentSheetAsync(transactionId);
                return;
            case ParcelProtectionCheckoutStep.SubmitIncludedCoverage:
                await ChooseParcelProtectionAsync(
                    false,
                    continueToPayment: true);
                return;
            default:
                await PresentPaymentSheetAsync(
                    transactionId);
                return;
        }
    }

    private async Task InitializeParcelProtectionOnEntryAsync(
        Guid transactionId)
    {
        if (ParcelProtection is null)
            return;

        if (ParcelProtection.Election == "Pending")
        {
            ParcelProtection = await transactionService
                .PrepareParcelProtectionAsync(
                    transactionId,
                    NewParcelProtectionPreparationIdempotencyKey());
        }

        switch (ParcelProtectionCheckoutPresentation.Next(
                    ParcelProtection))
        {
            case ParcelProtectionCheckoutStep.Reconfirm:
                await RefreshParcelProtectionForReconfirmationAsync(
                    transactionId);
                OpenParcelProtectionModal(
                    ParcelProtection?.AddOnAvailable == true);
                return;
            case ParcelProtectionCheckoutStep.Choose:
                TrackParcelProtectionOffered();
                IsParcelProtectionChoiceVisible = false;
                return;
            case ParcelProtectionCheckoutStep.SubmitIncludedCoverage:
                await ChooseParcelProtectionAsync(
                    false,
                    continueToPayment: false);
                return;
            case ParcelProtectionCheckoutStep.PresentPayment:
                IsParcelProtectionChoiceVisible = false;
                return;
        }
    }

    private void TrackParcelProtectionOffered()
    {
        if (parcelProtectionOfferedTracked)
            return;

        analytics.Track(ParcelProtectionAnalytics.Offered());
        parcelProtectionOfferedTracked = true;
    }

    private async Task ChangeParcelProtectionAsync()
    {
        if (Transaction is null || !CanChangeParcelProtection)
            return;

        await ToggleParcelProtectionAsync();
    }

    private async Task ToggleParcelProtectionAsync()
    {
        if (Transaction is null || !CanToggleParcelProtection)
            return;

        var addProtection = !IsParcelProtectionToggleOn;
        if (addProtection &&
            (!HasParcelProtectionOfferDetails ||
             ParcelProtection?.Election == "Declined"))
        {
            IsBusy = true;
            try
            {
                parcelProtectionPreparationIdempotencyKey = null;
                ParcelProtection = await transactionService
                    .PrepareParcelProtectionAsync(
                        Transaction.Id,
                        NewParcelProtectionPreparationIdempotencyKey());
            }
            catch (Exception exception)
            {
                Message = exception.Message;
                OnPropertyChanged(
                    nameof(IsParcelProtectionToggleOn));
                return;
            }
            finally
            {
                IsBusy = false;
            }

            if (!HasParcelProtectionOfferDetails)
            {
                Message =
                    "ยังเปิดความคุ้มครองเพิ่มไม่ได้ กรุณาลองใหม่ภายหลัง";
                OnPropertyChanged(
                    nameof(IsParcelProtectionToggleOn));
                return;
            }
        }

        analytics.Track(ParcelProtectionAnalytics.Changed());
        OpenParcelProtectionModal(addProtection);
    }

    private void OpenParcelProtectionModal(bool addProtection)
    {
        parcelProtectionSelectionBeforeModal =
            selectedParcelProtection ??
            (Transaction?.ParcelInsuranceFeeSatang > 0
                ? true
                : null);
        SetParcelProtectionSelection(addProtection);
        Message = "";
        IsParcelProtectionChoiceVisible = true;
        if (addProtection)
            TrackParcelProtectionOffered();
    }

    private async Task ConfirmParcelProtectionAsync()
    {
        if (!IsParcelProtectionChoiceVisible ||
            !selectedParcelProtection.HasValue ||
            IsBusy)
            return;

        if (selectedParcelProtection == true &&
            !IsParcelProtectionChoiceAvailable)
            return;

        Message = "";
        try
        {
            await ChooseParcelProtectionAsync(
                selectedParcelProtection.Value,
                continueToPayment: false);
            if (!IsParcelProtectionChoiceVisible)
                parcelProtectionSelectionBeforeModal = null;
        }
        catch (Exception exception)
        {
            Message = exception.Message;
            IsParcelProtectionChoiceVisible = true;
        }
    }

    private void CancelParcelProtection()
    {
        if (!IsParcelProtectionChoiceVisible || IsBusy)
            return;

        SetParcelProtectionSelection(
            parcelProtectionSelectionBeforeModal);
        parcelProtectionSelectionBeforeModal = null;
        IsParcelProtectionChoiceVisible = false;
        Message = "";
    }

    private void SetParcelProtectionSelection(bool? value)
    {
        if (selectedParcelProtection == value)
            return;

        parcelProtectionIdempotencyKey = null;
        parcelProtectionIdempotencySelection = null;
        selectedParcelProtection = value;
        NotifyCheckoutPresentationChanged();
    }

    private void NotifyCheckoutPresentationChanged()
    {
        OnPropertyChanged(nameof(IsParcelProtectionAddOnSelected));
        OnPropertyChanged(nameof(IsParcelProtectionIncludedSelected));
        OnPropertyChanged(nameof(IsParcelProtectionToggleOn));
        OnPropertyChanged(nameof(CanConfirmParcelProtection));
        OnPropertyChanged(nameof(ParcelProtectionToggleDetailText));
        OnPropertyChanged(nameof(ParcelProtectionModalTitle));
        OnPropertyChanged(nameof(ParcelProtectionModalDescription));
        OnPropertyChanged(nameof(CheckoutAmountText));
        OnPropertyChanged(nameof(PaymentActionText));
        OnPropertyChanged(nameof(PaymentSemanticDescription));
        OnPropertyChanged(nameof(CanStartPayment));
    }

    private async Task ChooseParcelProtectionAsync(
        bool addProtection,
        bool continueToPayment)
    {
        if (Transaction is null || ParcelProtection is null)
            return;

        var transactionId = Transaction.Id;

        addProtection = addProtection && !IsParcelProtectionUnavailable;
        if (addProtection &&
            (string.IsNullOrWhiteSpace(ParcelProtection.OptionReference) ||
             !ParcelProtection.CustomerPriceSatang.HasValue))
        {
            Message = "ข้อมูลความคุ้มครองเปลี่ยน กรุณาตรวจและเลือกใหม่ก่อนชำระ";
            return;
        }

        IsBusy = true;
        Message = "";
        try
        {
            var status = await transactionService.ChooseParcelProtectionAsync(
                transactionId,
                addProtection,
                addProtection ? ParcelProtection.OptionReference : null,
                addProtection
                    ? ParcelProtection.CustomerPriceSatang
                    : null,
                NewParcelProtectionIdempotencyKey(
                    addProtection));
            if (status == "reconfirmation_required")
            {
                await RefreshParcelProtectionForReconfirmationAsync(
                    transactionId);
                OpenParcelProtectionModal(
                    addProtection &&
                    ParcelProtection?.AddOnAvailable == true);
                Message =
                    "ข้อมูลความคุ้มครองเปลี่ยน กรุณาตรวจสอบอีกครั้งแล้วกดตกลง";
                return;
            }

            analytics.Track(addProtection
                ? ParcelProtectionAnalytics.Accepted(
                    ParcelProtection.CustomerPriceSatang!.Value)
                : ParcelProtectionAnalytics.Declined());
            IsParcelProtectionChoiceVisible = false;
            Transaction = await transactionService.GetTransactionAsync(
                transactionId);
            ParcelProtection = await transactionService
                .GetParcelProtectionAsync(transactionId);
            checkoutIdempotencyKey = null;
            if (status == "cancelling_shipping")
            {
                Message =
                    "กำลังปรับรายการจัดส่ง กรุณาลองชำระอีกครั้ง";
                return;
            }
            if (continueToPayment)
            {
                Message = "กำลังเตรียมการจัดส่ง…";
                await PresentPaymentSheetAsync(
                    transactionId);
            }
            else
            {
                Message = addProtection
                    ? "เพิ่มความคุ้มครองพัสดุในยอดชำระแล้ว"
                    : "ใช้ความคุ้มครองที่รวมมาแล้ว ไม่มีค่าใช้จ่ายเพิ่ม";
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RefreshParcelProtectionForReconfirmationAsync(
        Guid transactionId)
    {
        parcelProtectionIdempotencyKey = null;
        parcelProtectionPreparationIdempotencyKey = null;
        ParcelProtection = await transactionService.PrepareParcelProtectionAsync(
            transactionId,
            NewParcelProtectionPreparationIdempotencyKey());
        analytics.Track(ParcelProtectionAnalytics.PriceChanged());
        if (!ParcelProtection.AddOnAvailable)
            analytics.Track(ParcelProtectionAnalytics.Unavailable());
    }

    private async Task PresentPaymentSheetAsync(Guid transactionId)
    {
        IsBusy = true;
        IsPaymentSheetOpening = true;
        Message =
            "กำลังเตรียมการจัดส่งและเปิดหน้าจ่ายเงิน…";
        try
        {
            Transaction = await transactionService.GetTransactionAsync(transactionId);
            var outcome = await stripePaymentSheet.PresentAsync(
                transactionId,
                NewCheckoutIdempotencyKey());
            if (outcome == PaymentSheetOutcome.Completed)
                analytics.Track(ParcelProtectionAnalytics.CheckoutConverted());
            Message = outcome == PaymentSheetOutcome.Completed
                ? "ส่งข้อมูลการจ่ายเงินแล้ว กำลังตรวจสอบการชำระ"
                : "ยังไม่ได้จ่ายเงิน กดชำระอีกครั้งได้";
            Transaction = await transactionService.GetTransactionAsync(transactionId);
        }
        catch (PaymentPreparationException exception)
        {
            if (exception.CanRetry)
                checkoutIdempotencyKey = null;
            Message = exception.ConsumerMessage;
        }
        finally
        {
            IsPaymentSheetOpening = false;
            IsBusy = false;
        }
    }

    private string NewParcelProtectionIdempotencyKey(
        bool addProtection)
    {
        if (parcelProtectionIdempotencyKey is null ||
            parcelProtectionIdempotencySelection !=
                addProtection)
        {
            parcelProtectionIdempotencyKey =
                MobileIdempotencyKey.Create(
                    Transaction?.Id ??
                        throw new InvalidOperationException(
                            "ไม่พบรายการสำหรับเลือกความคุ้มครอง"),
                    MobileIdempotencyOperation
                        .ParcelProtectionElection);
            parcelProtectionIdempotencySelection =
                addProtection;
        }

        return parcelProtectionIdempotencyKey;
    }

    private string NewParcelProtectionPreparationIdempotencyKey() =>
        parcelProtectionPreparationIdempotencyKey ??=
            MobileIdempotencyKey.Create(
                Transaction?.Id ??
                    throw new InvalidOperationException(
                        "ไม่พบรายการสำหรับเตรียมความคุ้มครอง"),
                MobileIdempotencyOperation
                    .ParcelProtectionPreparation);

    private string NewCheckoutIdempotencyKey() =>
        checkoutIdempotencyKey ??=
            MobileIdempotencyKey.Create(
                Transaction?.Id ??
                    throw new InvalidOperationException(
                        "ไม่พบรายการสำหรับชำระเงิน"),
                MobileIdempotencyOperation.Checkout);

    private bool CanLoadParcelProtection() =>
        Transaction is
        {
            Role: AppTransactionRole.Buyer,
            FulfillmentType: AppFulfillmentType.Physical,
            State: "SellerAcceptedAwaitingPayment" or
                "CheckoutStarted" or "PaymentPending"
        };

    private async Task CopyInvitationLinkAsync()
    {
        if (string.IsNullOrWhiteSpace(SellerInvitationUrl))
            return;

        try
        {
            await Clipboard.Default.SetTextAsync(SellerInvitationUrl);
            InvitationFeedback = "คัดลอกลิงก์แล้ว";
        }
        catch
        {
            InvitationFeedback =
                "คัดลอกลิงก์ไม่สำเร็จ กรุณาลองอีกครั้ง";
        }
    }

    private async Task ShareInvitationLinkAsync()
    {
        if (string.IsNullOrWhiteSpace(SellerInvitationUrl))
            return;

        try
        {
            await Share.Default.RequestAsync(
                new ShareTextRequest
                {
                    Title = "ส่งข้อเสนอให้ผู้ขาย",
                    Text = SellerInvitationUrl
                });
        }
        catch
        {
            InvitationFeedback =
                "เปิดเมนูแชร์ไม่สำเร็จ กรุณาลองอีกครั้ง";
        }
    }

    private async Task DownloadAgreementEvidenceAsync()
    {
        if (Transaction is null ||
            !CanDownloadAgreementEvidence)
            return;

        IsBusy = true;
        Message = "";
        try
        {
            var evidence =
                await transactionService
                    .DownloadAgreementEvidenceAsync(
                        Transaction.Id);
            var safeFileName = Path.GetFileName(
                evidence.FileName);
            var path = Path.Combine(
                FileSystem.CacheDirectory,
                safeFileName);
            await File.WriteAllBytesAsync(
                path,
                evidence.Content);
            await Share.Default.RequestAsync(
                new ShareFileRequest(
                    "บันทึกหลักฐานข้อตกลง",
                    new ShareFile(path)));
        }
        catch (Exception exception)
        {
            Message = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task OpenShippingLabelAsync()
    {
        if (Transaction is null ||
            !CanDownloadShippingLabel)
            return;

        if (Shell.Current is null)
        {
            Message =
                "เปิดใบปะหน้าไม่สำเร็จ กรุณาลองอีกครั้ง";
            return;
        }

        await Shell.Current.GoToAsync(
            $"{nameof(ShippingLabelPage)}" +
            $"?TransactionId={Transaction.Id:D}");
    }

    private async Task OpenReturnShippingLabelAsync()
    {
        if (Transaction is null ||
            !CanDownloadReturnShippingLabel)
            return;
        if (Shell.Current is null)
        {
            Message =
                "เปิดใบปะหน้าส่งคืนไม่สำเร็จ กรุณาลองอีกครั้ง";
            return;
        }

        await Shell.Current.GoToAsync(
            $"{nameof(ShippingLabelPage)}" +
            $"?TransactionId={Transaction.Id:D}" +
            "&IsReturn=true");
    }

    private async Task ConfirmReceiptAsync()
    {
        var presentation = BuyerConfirmation;
        if (Transaction is null ||
            presentation is null)
            return;

        if (Shell.Current is not null)
        {
            var confirmed = await Shell.Current.DisplayAlertAsync(
                presentation.ConfirmationTitle,
                presentation.ConfirmationMessage,
                presentation.ConfirmationAcceptText,
                presentation.ConfirmationCancelText);
            if (!confirmed)
                return;
        }

        IsBusy = true;
        Message = "";
        try
        {
            Transaction = await transactionService.ConfirmReceiptAsync(
                Transaction.Id);
            Message = presentation.SuccessMessage;
        }
        catch (Exception exception)
        {
            Message = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ReportProblemAsync()
    {
        if (Transaction is null)
            return;
        if (string.IsNullOrWhiteSpace(DisputeStatement))
        {
            Message = "กรุณาอธิบายปัญหาที่พบ";
            return;
        }
        IsBusy = true;
        Message = "";
        try
        {
            Transaction = await transactionService.OpenDisputeAsync(
                Transaction.Id,
                SelectedDisputeReason.Value,
                DisputeStatement);
            IsProblemFormExpanded = false;
            Message = "รับเรื่องแล้ว และหยุดขั้นตอนจ่ายเงินไว้ระหว่างตรวจสอบ";
            OnPropertyChanged(nameof(CanManageDisputeEvidence));
        }
        catch (Exception exception)
        {
            Message = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task AddDisputeEvidenceAsync()
    {
        if (Transaction is null ||
            !CanManageDisputeEvidence)
            return;
        if (string.IsNullOrWhiteSpace(
                EvidenceDescription))
        {
            Message = "กรุณาอธิบายว่ารูปนี้แสดงอะไร";
            return;
        }
        if (Shell.Current is null)
        {
            Message = "ยังไม่สามารถเปิดตัวเลือกรูปได้";
            return;
        }
        var confirmed = await Shell.Current.DisplayAlertAsync(
            "ตรวจข้อมูลก่อนส่ง",
            "ส่งเฉพาะรูปที่เกี่ยวข้องและปิดข้อมูลส่วนตัวที่ไม่จำเป็น ห้ามส่งรหัสผ่าน รหัสกู้คืน private key หรือ seed phrase",
            "เลือกรูป",
            "ยกเลิก");
        if (!confirmed)
            return;
        FileResult? selected;
        try
        {
            selected = await MediaPicker.Default
                .PickPhotoAsync();
        }
        catch (Exception exception)
        {
            Message = exception.Message;
            return;
        }
        if (selected is null)
            return;

        IsBusy = true;
        Message = "";
        try
        {
            await using var source =
                await selected.OpenReadAsync();
            if (source.CanSeek &&
                source.Length > 6_000_000)
                throw new InvalidOperationException(
                    "รูปหลักฐานต้องมีขนาดไม่เกิน 6 MB");
            using var memory = new MemoryStream();
            await source.CopyToAsync(memory);
            if (memory.Length > 6_000_000)
                throw new InvalidOperationException(
                    "รูปหลักฐานต้องมีขนาดไม่เกิน 6 MB");
            var party = Transaction.Role ==
                        AppTransactionRole.Buyer
                ? AppDisputeEvidenceParty.Buyer
                : AppDisputeEvidenceParty.Seller;
            var uploaded =
                await transactionService
                    .SubmitDisputeEvidenceAsync(
                        Transaction.Id,
                        party,
                        SelectedEvidenceType.Value,
                        EvidenceDescription.Trim(),
                        new DisputeEvidenceUpload(
                            selected.FileName,
                            string.IsNullOrWhiteSpace(
                                selected.ContentType)
                                ? ContentType(
                                    selected.FileName)
                                : selected.ContentType,
                            memory.ToArray()),
                        Guid.NewGuid().ToString("N"));
            DisputeEvidence.Insert(0, uploaded);
            OnPropertyChanged(
                nameof(HasDisputeEvidence));
            EvidenceDescription = "";
            Message =
                "ส่งรูปหลักฐานแล้ว ทีมตรวจสอบจะเห็นไฟล์ที่ผ่านการแปลงและเข้ารหัส";
        }
        catch (Exception exception)
        {
            Message = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadDisputeEvidenceAsync()
    {
        if (Transaction is null)
            return;
        var party = Transaction.Role ==
                    AppTransactionRole.Buyer
            ? AppDisputeEvidenceParty.Buyer
            : AppDisputeEvidenceParty.Seller;
        var evidence = await transactionService
            .GetOwnDisputeEvidenceAsync(
                Transaction.Id,
                party);
        DisputeEvidence.Clear();
        foreach (var item in evidence)
            DisputeEvidence.Add(item);
        OnPropertyChanged(nameof(HasDisputeEvidence));
    }

    private static string ContentType(string fileName) =>
        Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => "image/jpeg"
        };

    private async Task LoadCarrierDataAsync()
    {
        var carriers =
            await transactionService.GetSupportedCarriersAsync();
        Carriers.Clear();
        foreach (var carrier in carriers)
            Carriers.Add(carrier);
    }

}

public sealed record DisputeReasonOption(
    AppDisputeReason Value,
    string Label)
{
    public static IReadOnlyList<DisputeReasonOption> All { get; } =
    [
        new(AppDisputeReason.NotReceived, "ยังไม่ได้รับสินค้า"),
        new(AppDisputeReason.WrongItem, "ได้รับสินค้าผิด"),
        new(AppDisputeReason.NotAsDescribed, "สินค้าไม่ตรงรายละเอียด"),
        new(AppDisputeReason.UndisclosedDamage, "พบตำหนิที่ไม่ได้แจ้ง"),
        new(AppDisputeReason.SuspectedCounterfeit, "สงสัยว่าเป็นของปลอม"),
        new(AppDisputeReason.EmptyOrTamperedParcel, "พัสดุว่างหรือถูกแกะ"),
        new(AppDisputeReason.Other, "ปัญหาอื่น")
    ];
}

public sealed record DisputeEvidenceTypeOption(
    AppDisputeEvidenceType Value,
    string Label)
{
    public static IReadOnlyList<DisputeEvidenceTypeOption>
        All
    { get; } =
    [
        new(AppDisputeEvidenceType.Item, "ภาพสินค้า"),
        new(AppDisputeEvidenceType.Packaging, "บรรจุภัณฑ์"),
        new(AppDisputeEvidenceType.ShippingLabel, "ฉลากขนส่ง"),
        new(
            AppDisputeEvidenceType.SerialOrIdentifier,
            "Serial/จุดระบุสินค้า"),
        new(
            AppDisputeEvidenceType.ReceiptOrProvenance,
            "ใบเสร็จ/ที่มา"),
        new(
            AppDisputeEvidenceType.HandoffRecord,
            "หลักฐานส่งมอบ"),
        new(AppDisputeEvidenceType.Other, "หลักฐานอื่น")
    ];
}
