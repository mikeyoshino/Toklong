using System.Globalization;
using System.Collections.ObjectModel;
using System.Windows.Input;
using Toklong.Mobile.Core;
using Toklong.Mobile.Pages;

namespace Toklong.Mobile.ViewModels;

public sealed class CreateOfferViewModel(
    ITransactionService transactionService,
    IAgreementDraftService agreementDraftService,
    IDraftPhotoStore draftPhotoStore,
    IAddressService addresses,
    IAuthenticationService authentication) : ObservableViewModel
{
    private AppFulfillmentType fulfillmentType = AppFulfillmentType.Physical;
    private string agreementDetails = "";
    private string productName = "";
    private string sellerPhoneNumber = "";
    private string knownDefects = "";
    private int selectedConditionIndex = -1;
    private string amountBaht = "";
    private string selectedPhotoPath = "";
    private string selectedPhotoName = "";
    private string aiSourcePhotoPath = "";
    private string aiSourcePhotoName = "";
    private string aiChatText = "";
    private AgreementDraft? aiDraft;
    private bool isAiSheetOpen;
    private bool isAiAnalyzing;
    private bool showOptionalDetails;
    private string message = "";
    private bool isBusy;
    private AddressOption? selectedProvince;
    private AddressOption? selectedDistrict;
    private SubdistrictOption? selectedSubdistrict;
    private bool hasSavedAddress;
    private bool useSavedAddress = true;
    private bool rememberAddress;
    private string savedAddress = "";
    private string addressLine = "";
    private bool addressDataLoaded;
    private bool isAddressLoading;
    private string addressLoadError = "";
    private BuyerCostPreview? costPreview;
    private bool isReviewPricing;
    private CancellationTokenSource? reviewPricingCancellation;
    private readonly BuyerCostPreviewRequestTracker costPreviewTracker = new();
    private readonly CreateOfferWizardState wizard = new();
    private bool isInitializing;
    private string sellerPhoneError = "";
    private string productNameError = "";
    private string productPhotoError = "";
    private string amountError = "";
    private string deliveryAddressError = "";
    private string conditionError = "";
    private string knownDefectsError = "";

    public event EventHandler<CreateOfferValidationTarget>? ValidationFailed;

    public string AgreementDetails
    {
        get => agreementDetails;
        set
        {
            if (SetProperty(ref agreementDetails, value ?? ""))
            {
                OnPropertyChanged(nameof(OptionalDetailsLabel));
                OnPropertyChanged(nameof(OptionalDetailsSummary));
                MarkWizardDirty();
            }
        }
    }

    public string SellerPhoneNumber
    {
        get => sellerPhoneNumber;
        set
        {
            if (SetProperty(
                    ref sellerPhoneNumber,
                    ThaiMobilePhoneInput.Format(value)))
            {
                SellerPhoneError = "";
                OnPropertyChanged(nameof(MaskedSellerPhoneNumber));
                OnPropertyChanged(nameof(ReviewSummary));
                MarkWizardDirty();
            }
        }
    }

    public string ProductName
    {
        get => productName;
        set
        {
            if (SetProperty(ref productName, value ?? ""))
            {
                ProductNameError = "";
                MarkWizardDirty();
            }
        }
    }

    public IReadOnlyList<string> ConditionOptions { get; } =
        ["ใหม่", "มือสอง สภาพดี", "มือสอง มีตำหนิ"];

    public int SelectedConditionIndex
    {
        get => selectedConditionIndex;
        set
        {
            if (SetProperty(ref selectedConditionIndex, value))
            {
                OnPropertyChanged(nameof(HasDefectCondition));
                OnPropertyChanged(nameof(IsNewCondition));
                OnPropertyChanged(nameof(IsUsedGoodCondition));
                OnPropertyChanged(nameof(IsUsedDefectCondition));
                OnPropertyChanged(nameof(ReviewSummary));
                ConditionError = "";
                if (!HasDefectCondition)
                    KnownDefectsError = "";
                MarkWizardDirty();
            }
        }
    }

    public string KnownDefects
    {
        get => knownDefects;
        set
        {
            if (SetProperty(ref knownDefects, value ?? ""))
            {
                OnPropertyChanged(nameof(ReviewSummary));
                KnownDefectsError = "";
                MarkWizardDirty();
            }
        }
    }

    public string AmountBaht
    {
        get => amountBaht;
        set
        {
            if (SetProperty(ref amountBaht, value ?? ""))
            {
                InvalidateReviewPricing();
                AmountError = "";
                MarkWizardDirty();
            }
        }
    }

    public bool HasCostPreview => CostPreview is not null;

    public string CostItemPriceText =>
        CostPreview?.FormattedItemPrice ?? "";

    public string CostProtectionFeeText =>
        CostPreview?.FormattedProtectionFee ?? "";

    public string CostTotalText =>
        CostPreview?.FormattedTotalBeforeShipping ?? "";

    public string CostSummaryLabel =>
        CostPreview?.SummaryLabel(fulfillmentType) ?? "";

    public string CostShippingText =>
        CostPreview?.ShippingText(fulfillmentType) ?? "";

    public bool IsReviewPricing
    {
        get => isReviewPricing;
        private set => SetProperty(
            ref isReviewPricing,
            value);
    }

    private BuyerCostPreview? CostPreview
    {
        get => costPreview;
        set
        {
            if (!SetProperty(ref costPreview, value))
                return;
            OnPropertyChanged(nameof(HasCostPreview));
            OnPropertyChanged(nameof(CostItemPriceText));
            OnPropertyChanged(nameof(CostProtectionFeeText));
            OnPropertyChanged(nameof(CostTotalText));
            OnPropertyChanged(nameof(CostSummaryLabel));
            OnPropertyChanged(nameof(CostShippingText));
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

    public CreateOfferStep CurrentStep => wizard.CurrentStep;
    public bool IsDealStep => CurrentStep == CreateOfferStep.Deal;
    public bool IsFulfillmentStep =>
        CurrentStep == CreateOfferStep.Fulfillment;
    public bool IsReviewStep => CurrentStep == CreateOfferStep.Review;
    public bool IsWizardDirty => wizard.IsDirty;
    public string ProgressText => CurrentStep switch
    {
        CreateOfferStep.Deal => "ขั้นที่ 1 จาก 3",
        CreateOfferStep.Fulfillment => "ขั้นที่ 2 จาก 3",
        _ => "ขั้นที่ 3 จาก 3"
    };

    public string SellerPhoneError
    {
        get => sellerPhoneError;
        private set => SetValidationError(
            ref sellerPhoneError,
            value,
            nameof(HasSellerPhoneError));
    }

    public bool HasSellerPhoneError =>
        !string.IsNullOrWhiteSpace(SellerPhoneError);

    public string ProductNameError
    {
        get => productNameError;
        private set => SetValidationError(
            ref productNameError,
            value,
            nameof(HasProductNameError));
    }

    public bool HasProductNameError =>
        !string.IsNullOrWhiteSpace(ProductNameError);

    public string ProductPhotoError
    {
        get => productPhotoError;
        private set => SetValidationError(
            ref productPhotoError,
            value,
            nameof(HasProductPhotoError));
    }

    public bool HasProductPhotoError =>
        !string.IsNullOrWhiteSpace(ProductPhotoError);

    public string AmountError
    {
        get => amountError;
        private set => SetValidationError(
            ref amountError,
            value,
            nameof(HasAmountError));
    }

    public bool HasAmountError =>
        !string.IsNullOrWhiteSpace(AmountError);

    public string DeliveryAddressError
    {
        get => deliveryAddressError;
        private set => SetValidationError(
            ref deliveryAddressError,
            value,
            nameof(HasDeliveryAddressError));
    }

    public bool HasDeliveryAddressError =>
        !string.IsNullOrWhiteSpace(DeliveryAddressError);

    public string ConditionError
    {
        get => conditionError;
        private set => SetValidationError(
            ref conditionError,
            value,
            nameof(HasConditionError));
    }

    public bool HasConditionError =>
        !string.IsNullOrWhiteSpace(ConditionError);

    public string KnownDefectsError
    {
        get => knownDefectsError;
        private set => SetValidationError(
            ref knownDefectsError,
            value,
            nameof(HasKnownDefectsError));
    }

    public bool HasKnownDefectsError =>
        !string.IsNullOrWhiteSpace(KnownDefectsError);

    public bool IsPhysical => fulfillmentType == AppFulfillmentType.Physical;
    public bool IsDigital => fulfillmentType == AppFulfillmentType.Digital;
    public string FulfillmentTypeLabel =>
        IsPhysical ? "สินค้าที่จับต้องได้" : "สินค้าดิจิทัล";
    public string FulfillmentToggleLabel =>
        IsPhysical
            ? "เปลี่ยนเป็นสินค้าดิจิทัล"
            : "เปลี่ยนเป็นสินค้าที่จับต้องได้";

    public bool HasSavedAddress
    {
        get => hasSavedAddress;
        private set
        {
            if (SetProperty(ref hasSavedAddress, value))
            {
                OnPropertyChanged(
                    nameof(ShowAddressEditor));
                OnPropertyChanged(
                    nameof(ShowSavedAddressSummary));
                OnPropertyChanged(nameof(ReviewDeliveryText));
            }
        }
    }

    public bool UseSavedAddress
    {
        get => useSavedAddress;
        set
        {
            if (SetProperty(ref useSavedAddress, value))
            {
                OnPropertyChanged(
                    nameof(ShowAddressEditor));
                OnPropertyChanged(
                    nameof(DeliveryRegionText));
                OnPropertyChanged(
                    nameof(ShowSavedAddressSummary));
                OnPropertyChanged(nameof(ReviewDeliveryText));
                OnPropertyChanged(nameof(ReviewSummary));
                DeliveryAddressError = "";
                MarkWizardDirty();
            }
        }
    }

    public bool ShowAddressEditor =>
        IsPhysical &&
        (!HasSavedAddress || !UseSavedAddress);

    public bool ShowSavedAddressSummary =>
        IsPhysical &&
        HasSavedAddress &&
        UseSavedAddress;

    public string AddressLoadError
    {
        get => addressLoadError;
        private set
        {
            if (SetProperty(ref addressLoadError, value))
                OnPropertyChanged(nameof(HasAddressLoadError));
        }
    }

    public bool HasAddressLoadError =>
        !string.IsNullOrWhiteSpace(AddressLoadError);

    public string SavedAddress
    {
        get => savedAddress;
        private set
        {
            if (SetProperty(ref savedAddress, value))
            {
                OnPropertyChanged(nameof(ReviewDeliveryText));
                OnPropertyChanged(nameof(ReviewSummary));
            }
        }
    }

    public string AddressLine
    {
        get => addressLine;
        set
        {
            if (SetProperty(ref addressLine, value ?? ""))
            {
                DeliveryAddressError = "";
                MarkWizardDirty();
            }
        }
    }

    public bool RememberAddress
    {
        get => rememberAddress;
        set
        {
            if (SetProperty(ref rememberAddress, value))
                MarkWizardDirty();
        }
    }

    public ObservableCollection<AddressOption> Provinces { get; } = [];
    public ObservableCollection<AddressOption> Districts { get; } = [];
    public ObservableCollection<SubdistrictOption> Subdistricts { get; } = [];

    public AddressOption? SelectedProvince
    {
        get => selectedProvince;
        set
        {
            if (!SetProperty(ref selectedProvince, value))
                return;
            OnPropertyChanged(nameof(ReviewDeliveryText));
            OnPropertyChanged(nameof(ReviewSummary));
            DeliveryAddressError = "";
            MarkWizardDirty();
            SelectedDistrict = null;
            Districts.Clear();
            Subdistricts.Clear();
            if (value is not null)
                _ = LoadDistrictsAsync(value.Id);
        }
    }

    public AddressOption? SelectedDistrict
    {
        get => selectedDistrict;
        set
        {
            if (!SetProperty(ref selectedDistrict, value))
                return;
            DeliveryAddressError = "";
            MarkWizardDirty();
            SelectedSubdistrict = null;
            Subdistricts.Clear();
            if (value is not null)
                _ = LoadSubdistrictsAsync(value.Id);
        }
    }

    public SubdistrictOption? SelectedSubdistrict
    {
        get => selectedSubdistrict;
        set
        {
            if (SetProperty(ref selectedSubdistrict, value))
            {
                OnPropertyChanged(nameof(DeliveryRegionText));
                OnPropertyChanged(nameof(ReviewDeliveryText));
                OnPropertyChanged(nameof(ReviewSummary));
                DeliveryAddressError = "";
                MarkWizardDirty();
            }
        }
    }

    public string DeliveryRegionText =>
        HasSavedAddress &&
        UseSavedAddress
            ? "ผู้ขายจะเห็นเฉพาะจังหวัดและรหัสไปรษณีย์จากที่อยู่นี้"
            : SelectedProvince is null ||
              SelectedSubdistrict is null
                ? ""
                : $"ผู้ขายจะเห็นปลายทาง {SelectedProvince.Name} {SelectedSubdistrict.PostalCode}";

    public string SelectedPhotoName
    {
        get => selectedPhotoName;
        private set
        {
            if (SetProperty(ref selectedPhotoName, value))
                OnPropertyChanged(nameof(HasPhoto));
        }
    }

    public bool HasPhoto => !string.IsNullOrWhiteSpace(SelectedPhotoName);

    public string AiChatText
    {
        get => aiChatText;
        set => SetProperty(ref aiChatText, value ?? "");
    }

    public string AiSourcePhotoName
    {
        get => aiSourcePhotoName;
        private set
        {
            if (SetProperty(ref aiSourcePhotoName, value))
                OnPropertyChanged(nameof(HasAiSourcePhoto));
        }
    }

    public bool HasAiSourcePhoto =>
        !string.IsNullOrWhiteSpace(AiSourcePhotoName);

    public AgreementDraft? AiDraft
    {
        get => aiDraft;
        private set
        {
            if (SetProperty(ref aiDraft, value))
            {
                OnPropertyChanged(nameof(HasAiDraft));
                OnPropertyChanged(nameof(ShowAiSourceInput));
                OnPropertyChanged(nameof(AiDraftSummary));
            }
        }
    }

    public bool HasAiDraft => AiDraft is not null;
    public bool ShowAiSourceInput => AiDraft is null;

    public string AiDraftSummary
    {
        get
        {
            if (AiDraft is null)
                return "";
            var lines = new List<string>();
            AddSummary(lines, "เบอร์ผู้ขาย", AiDraft.SellerPhoneNumber);
            AddSummary(lines, "สินค้า", AiDraft.ProductName);
            AddSummary(lines, "รายละเอียด", AiDraft.Description);
            AddSummary(lines, "ตำหนิ", AiDraft.KnownDefects);
            if (AiDraft.PriceBaht is > 0)
                lines.Add(
                    $"ราคา: ฿{AiDraft.PriceBaht.Value:N2}");
            if (AiDraft.Condition.HasValue)
                lines.Add(
                    $"สภาพ: {ConditionOptions[(int)AiDraft.Condition.Value]}");
            return lines.Count == 0
                ? "AI ยังไม่พบข้อมูลที่นำไปกรอกได้"
                : string.Join(Environment.NewLine, lines);
        }
    }

    public bool IsAiSheetOpen
    {
        get => isAiSheetOpen;
        private set => SetProperty(ref isAiSheetOpen, value);
    }

    public bool IsAiAnalyzing
    {
        get => isAiAnalyzing;
        private set => SetProperty(ref isAiAnalyzing, value);
    }

    public bool ShowOptionalDetails
    {
        get => showOptionalDetails;
        private set
        {
            if (SetProperty(ref showOptionalDetails, value))
                OnPropertyChanged(nameof(OptionalDetailsChevron));
        }
    }

    public string OptionalDetailsLabel =>
        string.IsNullOrWhiteSpace(AgreementDetails)
            ? "รายละเอียดเพิ่มเติม"
            : "รายละเอียดเพิ่มเติม · เพิ่มแล้ว";

    public string OptionalDetailsSummary =>
        string.IsNullOrWhiteSpace(AgreementDetails)
            ? "รุ่น สี อุปกรณ์ หรือสิ่งที่รวม (ไม่บังคับ)"
            : AgreementDetails.Trim();

    public string OptionalDetailsChevron =>
        ShowOptionalDetails ? "⌃" : "⌄";

    public bool HasDefectCondition =>
        SelectedConditionIndex == 2;
    public bool IsNewCondition =>
        SelectedConditionIndex == 0;
    public bool IsUsedGoodCondition =>
        SelectedConditionIndex == 1;
    public bool IsUsedDefectCondition =>
        SelectedConditionIndex == 2;

    public string ReviewSummary
    {
        get
        {
            var lines = new List<string>
            {
                $"ผู้ขาย: {MaskedSellerPhoneNumber}",
                $"สินค้า: {ProductName}",
                $"ราคา: {FormattedReviewAmount}",
                $"ประเภท: {FulfillmentTypeLabel}",
                $"รูปสินค้า: {(HasPhoto ? "เพิ่มแล้ว" : "ไม่ได้เพิ่ม (ไม่บังคับ)")}"
            };
            if (!string.IsNullOrWhiteSpace(AgreementDetails))
                lines.Add($"รายละเอียด: {AgreementDetails.Trim()}");
            if (SelectedConditionIndex is >= 0 and <= 2)
            {
                lines.Add(
                    $"สภาพ: {ConditionOptions[SelectedConditionIndex]}");
                if (HasDefectCondition &&
                    !string.IsNullOrWhiteSpace(KnownDefects))
                    lines.Add($"ตำหนิ: {KnownDefects.Trim()}");
            }
            if (IsPhysical)
                lines.Add($"จัดส่ง: {ReviewDeliveryText}");
            return string.Join(Environment.NewLine, lines);
        }
    }

    public string FormattedReviewAmount =>
        TryParseAmount(out var amount)
            ? $"฿{amount:N2}"
            : "ยังไม่ได้ระบุ";

    public string MaskedSellerPhoneNumber
    {
        get
        {
            var clean = ThaiMobilePhoneInput.Sanitize(
                SellerPhoneNumber);
            return clean.Length == 10
                ? $"{clean[..3]}-***-{clean[^4..]}"
                : SellerPhoneNumber;
        }
    }

    public string ReviewDeliveryText =>
        HasSavedAddress && UseSavedAddress
            ? SavedAddress
            : SelectedProvince is null ||
              SelectedSubdistrict is null
                ? "ยังไม่ได้เลือกที่อยู่"
                : $"{SelectedProvince.Name} {SelectedSubdistrict.PostalCode}";

    public bool IsBusy
    {
        get => isBusy;
        private set => SetProperty(ref isBusy, value);
    }

    public ICommand SelectPhysicalCommand =>
        new Command(() => SelectFulfillment(AppFulfillmentType.Physical));

    public ICommand SelectDigitalCommand =>
        new Command(() => SelectFulfillment(AppFulfillmentType.Digital));

    public ICommand ToggleFulfillmentCommand =>
        new Command(() => SelectFulfillment(
            IsPhysical
                ? AppFulfillmentType.Digital
                : AppFulfillmentType.Physical));
    public ICommand ToggleOptionalDetailsCommand =>
        new Command(() =>
            ShowOptionalDetails = !ShowOptionalDetails);
    public ICommand EditAddressCommand =>
        new Command(() => UseSavedAddress = false);
    public ICommand UseSavedAddressCommand =>
        new Command(() => UseSavedAddress = true);
    public ICommand ContinueFromDealCommand =>
        new Command(ContinueFromDeal);
    public ICommand ContinueFromFulfillmentCommand =>
        new AsyncCommand(ContinueFromFulfillmentAsync);
    public ICommand PreviousStepCommand =>
        new Command(MoveToPreviousStep);
    public ICommand RetryAddressCommand =>
        new AsyncCommand(LoadAsync);
    public ICommand SelectNewConditionCommand =>
        new Command(() => SelectedConditionIndex = 0);
    public ICommand SelectUsedGoodConditionCommand =>
        new Command(() => SelectedConditionIndex = 1);
    public ICommand SelectUsedDefectConditionCommand =>
        new Command(() => SelectedConditionIndex = 2);
    public ICommand SubmitCommand => new AsyncCommand(SubmitAsync);

    public ICommand PickPhotoCommand => new AsyncCommand(PickPhotoAsync);
    public ICommand OpenAiSheetCommand =>
        new Command(OpenAiSheet);
    public ICommand CloseAiSheetCommand =>
        new Command(CloseAiSheet);
    public ICommand PickAiSourcePhotoCommand =>
        new AsyncCommand(PickAiSourcePhotoAsync);
    public ICommand AnalyzeAiSourceCommand =>
        new AsyncCommand(AnalyzeAiSourceAsync);
    public ICommand ApplyAiDraftCommand =>
        new Command(ApplyAiDraft);

    public async Task LoadAsync()
    {
        if (addressDataLoaded || isAddressLoading)
            return;
        isAddressLoading = true;
        isInitializing = true;
        AddressLoadError = "";
        try
        {
            var profile =
                await authentication.GetProfileAsync();
            SavedAddress =
                profile.SavedAddress ?? "";
            HasSavedAddress =
                !string.IsNullOrWhiteSpace(
                    SavedAddress);
            UseSavedAddress =
                HasSavedAddress;
            var items = await addresses.GetProvincesAsync();
            Provinces.Clear();
            foreach (var item in items)
                Provinces.Add(item);
            addressDataLoaded = true;
        }
        catch
        {
            AddressLoadError =
                "โหลดข้อมูลที่อยู่ไม่สำเร็จ กรุณาลองอีกครั้ง";
        }
        finally
        {
            isInitializing = false;
            isAddressLoading = false;
        }
    }

    private void SelectFulfillment(AppFulfillmentType value)
    {
        if (fulfillmentType == value)
            return;

        InvalidateReviewPricing();
        fulfillmentType = value;
        DeliveryAddressError = "";
        MarkWizardDirty();
        OnPropertyChanged(nameof(IsPhysical));
        OnPropertyChanged(nameof(IsDigital));
        OnPropertyChanged(nameof(FulfillmentTypeLabel));
        OnPropertyChanged(nameof(FulfillmentToggleLabel));
        OnPropertyChanged(nameof(ShowAddressEditor));
        OnPropertyChanged(nameof(ShowSavedAddressSummary));
        OnPropertyChanged(nameof(ReviewSummary));
        OnPropertyChanged(nameof(CostSummaryLabel));
        OnPropertyChanged(nameof(CostShippingText));
    }

    public void CancelReviewPricing()
    {
        InvalidateReviewPricing();
    }

    private void InvalidateReviewPricing()
    {
        reviewPricingCancellation?.Cancel();
        reviewPricingCancellation?.Dispose();
        reviewPricingCancellation = null;
        costPreviewTracker.Invalidate();
        CostPreview = null;
        IsReviewPricing = false;
    }

    private void ContinueFromDeal()
    {
        Message = "";
        if (!ValidateDealStep(out _, out _))
            return;

        if (wizard.MoveNext())
            RaiseWizardProperties();
    }

    private async Task ContinueFromFulfillmentAsync()
    {
        Message = "";
        if (!ValidateDealStep(out _, out var amount) ||
            !ValidateFulfillmentStep())
            return;

        var itemPriceSatang =
            checked((long)(amount * 100m));
        InvalidateReviewPricing();
        var requestVersion = costPreviewTracker.Begin();
        var cancellation = new CancellationTokenSource();
        reviewPricingCancellation = cancellation;
        IsReviewPricing = true;
        try
        {
            var preview =
                await transactionService.GetBuyerCostPreviewAsync(
                    itemPriceSatang,
                    cancellation.Token);
            if (!costPreviewTracker.IsCurrent(requestVersion) ||
                cancellation.IsCancellationRequested ||
                preview.ItemPriceSatang != itemPriceSatang ||
                !TryGetPreviewPriceSatang(out var currentPriceSatang) ||
                currentPriceSatang != itemPriceSatang)
                return;

            CostPreview = preview;
            OnPropertyChanged(nameof(ReviewSummary));
            OnPropertyChanged(nameof(FormattedReviewAmount));
            OnPropertyChanged(nameof(ReviewDeliveryText));
            while (wizard.CurrentStep != CreateOfferStep.Review &&
                   wizard.MoveNext())
            {
            }
            RaiseWizardProperties();
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            if (costPreviewTracker.IsCurrent(requestVersion))
                Message =
                    "คำนวณค่าคุ้มครองผู้ซื้อไม่ได้ กรุณาลองอีกครั้ง";
        }
        finally
        {
            if (ReferenceEquals(
                    reviewPricingCancellation,
                    cancellation))
            {
                reviewPricingCancellation.Dispose();
                reviewPricingCancellation = null;
                IsReviewPricing = false;
            }
        }
    }

    private bool TryGetPreviewPriceSatang(
        out long itemPriceSatang)
    {
        itemPriceSatang = 0;
        if (!TryParseAmount(out var amount) ||
            amount is < 1_000 or > 30_000 ||
            decimal.Round(amount, 2) != amount)
            return false;
        itemPriceSatang = checked((long)(amount * 100m));
        return true;
    }

    private void MoveToPreviousStep()
    {
        Message = "";
        if (wizard.CurrentStep == CreateOfferStep.Review)
            InvalidateReviewPricing();
        if (wizard.MoveBack())
            RaiseWizardProperties();
    }

    private async Task SubmitAsync()
    {
        if (IsBusy)
            return;

        Message = "";
        if (!IsReviewStep ||
            !ValidateDealStep(
                out var cleanSellerPhone,
                out var amount) ||
            !ValidateFulfillmentStep())
            return;

        if (!ValidateReviewStep())
            return;

        var satang = checked((long)(amount * 100m));
        if (CostPreview is null ||
            CostPreview.ItemPriceSatang != satang)
        {
            Message =
                "ข้อมูลค่าใช้จ่ายเปลี่ยนแล้ว กรุณากลับไปตรวจข้อมูลอีกครั้ง";
            ValidationFailed?.Invoke(
                this,
                CreateOfferValidationTarget.CostPreview);
            return;
        }

        IsBusy = true;
        try
        {
            var condition = SelectedConditionIndex switch
            {
                0 => AppCondition.New,
                1 => AppCondition.UsedGood,
                _ => AppCondition.UsedDefects
            };
            var snapshotFields =
                QuickDealSnapshotComposer.Compose(
                    ProductName,
                    AgreementDetails,
                    condition,
                    KnownDefects);
            var created = await transactionService.CreateBuyerOfferAsync(
                new CreateBuyerOfferRequest(
                    cleanSellerPhone,
                    fulfillmentType,
                    condition,
                    ProductName.Trim(),
                    snapshotFields.Description,
                    snapshotFields.KnownDefects,
                    satang,
                    selectedPhotoPath,
                    IsPhysical &&
                    HasSavedAddress &&
                    UseSavedAddress,
                    IsPhysical &&
                    (!HasSavedAddress ||
                     !UseSavedAddress)
                        ? AddressLine.Trim()
                        : null,
                    IsPhysical
                        ? SelectedProvince?.Id
                        : null,
                    IsPhysical
                        ? SelectedDistrict?.Id
                        : null,
                    IsPhysical
                        ? SelectedSubdistrict?.Id
                        : null,
                    IsPhysical &&
                    (!HasSavedAddress ||
                     !UseSavedAddress) &&
                    RememberAddress));
            draftPhotoStore.Delete(selectedPhotoPath);
            selectedPhotoPath = "";
            SelectedPhotoName = "";
            wizard.Reset();
            RaiseWizardProperties();
            await Shell.Current.GoToAsync(
                AuthenticatedHomeRoutes.Transactions(
                    TransactionRoleRoute.Buying));
            await Shell.Current.GoToAsync(
                nameof(TransactionDetailPage),
                new Dictionary<string, object>
                {
                    ["TransactionId"] = created.Id
                });
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

    private bool ValidateDealStep(
        out string cleanSellerPhone,
        out decimal amount)
    {
        SellerPhoneError = "";
        ProductNameError = "";
        ProductPhotoError = "";
        AmountError = "";
        var hasSelectedPhoto =
            !string.IsNullOrWhiteSpace(selectedPhotoPath);
        var result = CreateOfferStepValidator.ValidateDeal(
            SellerPhoneNumber,
            ProductName,
            hasSelectedPhoto,
            hasSelectedPhoto && File.Exists(selectedPhotoPath),
            AmountBaht);
        cleanSellerPhone = result.CleanSellerPhone;
        amount = result.AmountBaht;

        foreach (var error in result.Errors)
        {
            switch (error.Target)
            {
                case CreateOfferValidationTarget.SellerPhone:
                    SellerPhoneError = error.Message;
                    break;
                case CreateOfferValidationTarget.ProductName:
                    ProductNameError = error.Message;
                    break;
                case CreateOfferValidationTarget.ProductPhoto:
                    ProductPhotoError = error.Message;
                    break;
                case CreateOfferValidationTarget.Amount:
                    AmountError = error.Message;
                    break;
            }
        }

        if (hasSelectedPhoto &&
            !File.Exists(selectedPhotoPath))
        {
            draftPhotoStore.Delete(selectedPhotoPath);
            selectedPhotoPath = "";
            SelectedPhotoName = "";
        }

        NotifyFirstInvalid(result.FirstInvalidTarget);
        return result.IsValid;
    }

    private bool ValidateFulfillmentStep()
    {
        DeliveryAddressError = "";
        var result =
            CreateOfferStepValidator.ValidateFulfillment(
                IsPhysical,
                HasSavedAddress && UseSavedAddress,
                !string.IsNullOrWhiteSpace(AddressLine),
                SelectedProvince is not null,
                SelectedDistrict is not null,
                SelectedSubdistrict is not null);
        if (!result.IsValid)
        {
            DeliveryAddressError =
                result.Errors[0].Message;
        }

        NotifyFirstInvalid(result.FirstInvalidTarget);
        return result.IsValid;
    }

    private bool ValidateReviewStep()
    {
        ConditionError = "";
        KnownDefectsError = "";
        var result = CreateOfferStepValidator.ValidateReview(
            SelectedConditionIndex,
            KnownDefects);
        if (!result.IsValid)
        {
            var error = result.Errors[0];
            if (error.Target ==
                CreateOfferValidationTarget.Condition)
            {
                ConditionError = error.Message;
            }
            else
            {
                KnownDefectsError = error.Message;
            }
        }

        NotifyFirstInvalid(result.FirstInvalidTarget);
        return result.IsValid;
    }

    private bool TryParseAmount(out decimal amount) =>
        decimal.TryParse(
            AmountBaht,
            NumberStyles.Number,
            CultureInfo.GetCultureInfo("th-TH"),
            out amount) ||
        decimal.TryParse(
            AmountBaht,
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out amount);

    public void DiscardDraft()
    {
        CancelReviewPricing();
        draftPhotoStore.Delete(selectedPhotoPath);
        selectedPhotoPath = "";
        DiscardAiSource();

        isInitializing = true;
        try
        {
            SellerPhoneNumber = "";
            ProductName = "";
            AgreementDetails = "";
            AmountBaht = "";
            SelectedConditionIndex = -1;
            KnownDefects = "";
            AddressLine = "";
            RememberAddress = false;
            SelectedProvince = null;
            SelectedDistrict = null;
            SelectedSubdistrict = null;
            UseSavedAddress = HasSavedAddress;
            if (!IsPhysical)
                SelectFulfillment(AppFulfillmentType.Physical);
            SelectedPhotoName = "";
            AiChatText = "";
            AiDraft = null;
            IsAiSheetOpen = false;
            Message = "";
            ClearValidationErrors();
        }
        finally
        {
            isInitializing = false;
        }

        wizard.Reset();
        RaiseWizardProperties();
    }

    private void MarkWizardDirty()
    {
        if (isInitializing || wizard.IsDirty)
            return;

        wizard.MarkDirty();
        OnPropertyChanged(nameof(IsWizardDirty));
    }

    private void RaiseWizardProperties()
    {
        OnPropertyChanged(nameof(CurrentStep));
        OnPropertyChanged(nameof(IsDealStep));
        OnPropertyChanged(nameof(IsFulfillmentStep));
        OnPropertyChanged(nameof(IsReviewStep));
        OnPropertyChanged(nameof(IsWizardDirty));
        OnPropertyChanged(nameof(ProgressText));
    }

    private void SetValidationError(
        ref string storage,
        string value,
        string hasErrorProperty)
    {
        if (SetProperty(ref storage, value))
            OnPropertyChanged(hasErrorProperty);
    }

    private void NotifyFirstInvalid(
        CreateOfferValidationTarget? target)
    {
        if (target.HasValue)
            ValidationFailed?.Invoke(this, target.Value);
    }

    private void ClearValidationErrors()
    {
        SellerPhoneError = "";
        ProductNameError = "";
        ProductPhotoError = "";
        AmountError = "";
        DeliveryAddressError = "";
        ConditionError = "";
        KnownDefectsError = "";
    }

    private async Task PickPhotoAsync()
    {
        try
        {
            var photos = await MediaPicker.Default.PickPhotosAsync(
                new MediaPickerOptions { Title = "เลือกรูปสินค้า" });
            var photo = photos.FirstOrDefault();
            if (photo is null)
                return;

            await using var source = await photo.OpenReadAsync();
            var savedPath = await draftPhotoStore.SaveAsync(
                source,
                photo.FileName);
            draftPhotoStore.Delete(selectedPhotoPath);
            selectedPhotoPath = savedPath;
            SelectedPhotoName = photo.FileName;
            ProductPhotoError = "";
            MarkWizardDirty();
            Message = "";
        }
        catch (Exception exception)
        {
            Message = $"เลือกรูปไม่ได้: {exception.Message}";
        }
    }

    private void OpenAiSheet()
    {
        Message = "";
        IsAiSheetOpen = true;
    }

    private void CloseAiSheet()
    {
        DiscardAiSource();
        AiChatText = "";
        AiDraft = null;
        IsAiSheetOpen = false;
        Message = "";
    }

    public void DiscardAiSource()
    {
        draftPhotoStore.Delete(aiSourcePhotoPath);
        aiSourcePhotoPath = "";
        AiSourcePhotoName = "";
    }

    private async Task PickAiSourcePhotoAsync()
    {
        try
        {
            var photos = await MediaPicker.Default.PickPhotosAsync(
                new MediaPickerOptions
                {
                    Title = "เลือกรูปหรือภาพแชต"
                });
            var photo = photos.FirstOrDefault();
            if (photo is null)
                return;

            await using var source = await photo.OpenReadAsync();
            var savedPath = await draftPhotoStore.SaveAsync(
                source,
                photo.FileName);
            DiscardAiSource();
            aiSourcePhotoPath = savedPath;
            AiSourcePhotoName = photo.FileName;
            Message = "";
        }
        catch (Exception exception)
        {
            Message = $"เลือกรูปไม่ได้: {exception.Message}";
        }
    }

    private async Task AnalyzeAiSourceAsync()
    {
        if (string.IsNullOrWhiteSpace(AiChatText) &&
            string.IsNullOrWhiteSpace(aiSourcePhotoPath))
        {
            Message =
                "เพิ่มรูปหรือวางข้อความแชตก่อนให้ AI ช่วยกรอก";
            return;
        }

        IsAiAnalyzing = true;
        Message = "";
        try
        {
            AiDraft = await agreementDraftService.ExtractAsync(
                AiChatText,
                string.IsNullOrWhiteSpace(aiSourcePhotoPath)
                    ? []
                    : [aiSourcePhotoPath]);
            DiscardAiSource();
        }
        catch (Exception exception)
        {
            Message = exception.Message;
        }
        finally
        {
            IsAiAnalyzing = false;
        }
    }

    private void ApplyAiDraft()
    {
        if (AiDraft is null)
            return;

        var result = AgreementDraftMerger.MergeBlankFields(
            new AgreementFormValues(
                SellerPhoneNumber,
                ProductName,
                AgreementDetails,
                KnownDefects,
                AmountBaht,
                SelectedConditionIndex),
            AiDraft);
        SellerPhoneNumber = result.Values.SellerPhoneNumber;
        ProductName = result.Values.ProductName;
        AgreementDetails = result.Values.AgreementDetails;
        KnownDefects = result.Values.KnownDefects;
        AmountBaht = result.Values.AmountBaht;
        SelectedConditionIndex =
            result.Values.SelectedConditionIndex;

        CloseAiSheet();
        Message = result.AppliedFieldCount == 0
            ? "ไม่มีช่องว่างที่ AI สามารถช่วยกรอกได้"
            : $"AI ช่วยกรอก {result.AppliedFieldCount} ช่อง กรุณาตรวจสอบก่อนส่ง";
    }

    private static void AddSummary(
        ICollection<string> lines,
        string label,
        string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            lines.Add($"{label}: {value}");
    }

    private async Task LoadDistrictsAsync(
        int provinceId)
    {
        try
        {
            var items =
                await addresses.GetDistrictsAsync(
                    provinceId);
            Districts.Clear();
            foreach (var item in items)
                Districts.Add(item);
        }
        catch (Exception exception)
        {
            Message = exception.Message;
        }
    }

    private async Task LoadSubdistrictsAsync(
        int districtId)
    {
        try
        {
            var items =
                await addresses.GetSubdistrictsAsync(
                    districtId);
            Subdistricts.Clear();
            foreach (var item in items)
                Subdistricts.Add(item);
        }
        catch (Exception exception)
        {
            Message = exception.Message;
        }
    }
}
