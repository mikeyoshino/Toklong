using System.Collections.ObjectModel;
using System.Windows.Input;
using Toklong.Mobile.Core;
using Toklong.Mobile.Pages;

namespace Toklong.Mobile.ViewModels;

public sealed class TransactionDetailViewModel(
    ITransactionService transactionService,
    IStripePaymentSheetService stripePaymentSheet) : ObservableViewModel
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
    private DisputeReasonOption selectedDisputeReason =
        DisputeReasonOption.All[^1];
    private string disputeStatement = "";
    private DisputeEvidenceTypeOption selectedEvidenceType =
        DisputeEvidenceTypeOption.All[0];
    private string evidenceDescription = "";
    private bool carrierDataLoaded;

    public AppTransaction? Transaction
    {
        get => transaction;
        private set
        {
            if (SetProperty(ref transaction, value))
            {
                IsAgreementDetailsExpanded =
                    value?.Role != AppTransactionRole.Seller;
                OnPropertyChanged(nameof(IsPaymentAction));
                OnPropertyChanged(nameof(ShowSellerInvitation));
                OnPropertyChanged(nameof(SellerInvitationUrl));
                OnPropertyChanged(nameof(IsFulfillmentAction));
                OnPropertyChanged(nameof(IsPhysicalFulfillmentAction));
                OnPropertyChanged(nameof(IsDigitalFulfillmentAction));
                OnPropertyChanged(nameof(IsBuyerConfirmationAction));
                OnPropertyChanged(nameof(IsStatusOnly));
                OnPropertyChanged(nameof(DetailHeadline));
                OnPropertyChanged(nameof(CanDownloadAgreementEvidence));
                OnPropertyChanged(nameof(CanDownloadShippingLabel));
                OnPropertyChanged(nameof(IsSellerDetail));
                OnPropertyChanged(nameof(ShowAgreementDetailsContent));
                OnPropertyChanged(nameof(CanManageDisputeEvidence));
            }
        }
    }

    public string Message
    {
        get => message;
        private set
        {
            if (SetProperty(ref message, value))
                OnPropertyChanged(nameof(HasMessage));
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

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (SetProperty(ref isBusy, value))
                OnPropertyChanged(nameof(CanSubmitTracking));
        }
    }

    public bool IsPaymentAction =>
        Transaction?.Presentation.PrimaryAction == TransactionAction.ReviewAndPay;

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

    public bool IsBuyerConfirmationAction =>
        Transaction?.Presentation.PrimaryAction ==
        TransactionAction.ConfirmReceipt;

    public bool IsStatusOnly =>
        Transaction?.Presentation.PrimaryAction == TransactionAction.ViewStatus;

    public string DetailHeadline => Transaction?.Role == AppTransactionRole.Buyer
        ? "รายการซื้อ"
        : "รายการขาย";

    public bool IsSellerDetail =>
        Transaction?.Role == AppTransactionRole.Seller;

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

    public ObservableCollection<CarrierOption> Carriers { get; } = [];

    public bool AcceptedTerms
    {
        get => acceptedTerms;
        set => SetProperty(ref acceptedTerms, value);
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
        DisputeEvidence { get; } = [];

    public bool HasDisputeEvidence =>
        DisputeEvidence.Count > 0;

    public ICommand PrimaryActionCommand => new AsyncCommand(ExecutePrimaryActionAsync);

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

    public ICommand ReportProblemCommand =>
        new AsyncCommand(ReportProblemAsync);

    public ICommand AddDisputeEvidenceCommand =>
        new AsyncCommand(AddDisputeEvidenceAsync);

    public ICommand DownloadAgreementEvidenceCommand =>
        new AsyncCommand(DownloadAgreementEvidenceAsync);

    public ICommand OpenShippingLabelCommand =>
        new AsyncCommand(OpenShippingLabelAsync);

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
            IsBusy = true;
        try
        {
            Transaction = await transactionService.GetTransactionAsync(transactionId);
            Message = Transaction is null ? "ไม่พบรายการนี้" : "";
            if (CanManageDisputeEvidence)
                await LoadDisputeEvidenceAsync();
            if (IsPhysicalFulfillmentAction && !carrierDataLoaded)
            {
                await LoadCarrierDataAsync();
                carrierDataLoaded = true;
            }
        }
        catch (Exception exception) when (!showBusy)
        {
            Message =
                $"อัปเดตสถานะไม่สำเร็จ · {exception.Message}";
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

            IsBusy = true;
            Message = "";
            try
            {
                var outcome = await stripePaymentSheet.PresentAsync(
                    transactionId);
                Message = outcome == PaymentSheetOutcome.Completed
                    ? "ส่งข้อมูลการจ่ายเงินแล้ว กำลังรอ Stripe ยืนยัน"
                    : "ยังไม่ได้จ่ายเงิน";
                Transaction = await transactionService.GetTransactionAsync(
                    transactionId);
            }
            catch (Exception exception)
            {
                Message = exception.Message;
                Transaction = await transactionService.GetTransactionAsync(
                    transactionId);
            }
            finally
            {
                IsBusy = false;
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

    private async Task ConfirmReceiptAsync()
    {
        if (Transaction is null)
            return;

        if (Shell.Current is not null)
        {
            var confirmed = await Shell.Current.DisplayAlertAsync(
                "ยืนยันหลังตรวจสินค้า",
                "คุณตรวจสินค้าแล้วและไม่พบปัญหา เมื่อยืนยัน ระบบจะเริ่มขั้นตอนจ่ายเงินให้ผู้ขาย",
                "ยืนยันและเริ่มจ่ายให้ผู้ขาย",
                "กลับไปตรวจสินค้า");
            if (!confirmed)
                return;
        }

        IsBusy = true;
        Message = "";
        try
        {
            Transaction = await transactionService.ConfirmReceiptAsync(
                Transaction.Id);
            Message = "ยืนยันว่าตรวจแล้ว ระบบจะเริ่มขั้นตอนจ่ายเงินให้ผู้ขาย";
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
        All { get; } =
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
