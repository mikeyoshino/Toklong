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
    private bool isReviewSheetOpen;
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
    private BuyerCostPreview? costPreview;
    private bool isReviewPricing;
    private CancellationTokenSource? reviewPricingCancellation;
    private readonly BuyerCostPreviewRequestTracker costPreviewTracker = new();

    public string AgreementDetails
    {
        get => agreementDetails;
        set
        {
            if (SetProperty(ref agreementDetails, value ?? ""))
            {
                OnPropertyChanged(nameof(OptionalDetailsLabel));
                OnPropertyChanged(nameof(OptionalDetailsSummary));
            }
        }
    }

    public string SellerPhoneNumber
    {
        get => sellerPhoneNumber;
        set => SetProperty(
            ref sellerPhoneNumber,
            ThaiMobilePhoneInput.Format(value));
    }

    public string ProductName
    {
        get => productName;
        set => SetProperty(ref productName, value ?? "");
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
            }
        }
    }

    public string KnownDefects
    {
        get => knownDefects;
        set
        {
            if (SetProperty(ref knownDefects, value ?? ""))
                OnPropertyChanged(nameof(ReviewSummary));
        }
    }

    public string AmountBaht
    {
        get => amountBaht;
        set
        {
            if (SetProperty(ref amountBaht, value ?? ""))
                InvalidateReviewPricing();
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
        set => SetProperty(
            ref addressLine,
            value);
    }

    public bool RememberAddress
    {
        get => rememberAddress;
        set => SetProperty(
            ref rememberAddress,
            value);
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

    public bool IsReviewSheetOpen
    {
        get => isReviewSheetOpen;
        private set => SetProperty(ref isReviewSheetOpen, value);
    }

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
                $"ผู้ขาย: {SellerPhoneNumber}",
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
    public ICommand ReviewCommand =>
        new AsyncCommand(OpenReviewAsync);
    public ICommand CloseReviewCommand =>
        new Command(CloseReview);
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
        if (addressDataLoaded)
            return;
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

    private void SelectFulfillment(AppFulfillmentType value)
    {
        if (fulfillmentType == value)
            return;

        InvalidateReviewPricing();
        fulfillmentType = value;
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
        IsReviewSheetOpen = false;
    }

    private async Task OpenReviewAsync()
    {
        Message = "";
        if (!ValidateQuickDeal(out _, out var amount))
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
            IsReviewSheetOpen = true;
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

    private void CloseReview()
    {
        InvalidateReviewPricing();
        Message = "";
    }

    private async Task SubmitAsync()
    {
        Message = "";
        if (!ValidateQuickDeal(
                out var cleanSellerPhone,
                out var amount))
            return;
        if (SelectedConditionIndex is < 0 or > 2)
        {
            Message = "เลือกสภาพสินค้า";
            return;
        }
        if (HasDefectCondition &&
            string.IsNullOrWhiteSpace(KnownDefects))
        {
            Message = "ระบุตำหนิที่ตกลงกัน";
            return;
        }

        var satang = checked((long)(amount * 100m));
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
            IsReviewSheetOpen = false;
            await Shell.Current.GoToAsync("//transactions");
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

    private bool ValidateQuickDeal(
        out string cleanSellerPhone,
        out decimal amount)
    {
        cleanSellerPhone =
            ThaiMobilePhoneInput.Sanitize(SellerPhoneNumber);
        amount = 0;
        if (!ThaiMobilePhoneInput.IsValid(cleanSellerPhone))
        {
            Message =
                "กรอกเบอร์มือถือผู้ขาย 10 หลัก เช่น 081-234-5678";
            return false;
        }
        if (string.IsNullOrWhiteSpace(ProductName))
        {
            Message = "ใส่ชื่อสินค้า";
            return false;
        }
        if (IsPhysical &&
            (!HasSavedAddress ||
             !UseSavedAddress) &&
            (string.IsNullOrWhiteSpace(AddressLine) ||
             SelectedProvince is null ||
             SelectedDistrict is null ||
             SelectedSubdistrict is null))
        {
            Message =
                "กรอกบ้านเลขที่และเลือกพื้นที่จัดส่งให้ครบ";
            return false;
        }
        if (!string.IsNullOrWhiteSpace(
                selectedPhotoPath) &&
            !File.Exists(selectedPhotoPath))
        {
            draftPhotoStore.Delete(selectedPhotoPath);
            selectedPhotoPath = "";
            SelectedPhotoName = "";
            Message = "ไม่พบรูปที่เลือก กรุณาเลือกรูปใหม่";
            return false;
        }
        if (!TryParseAmount(out amount))
        {
            Message = "ใส่ราคาที่ตกลงกันให้ถูกต้อง";
            return false;
        }
        if (amount is < 1_000 or > 30_000 ||
            decimal.Round(amount, 2) != amount)
        {
            Message =
                "ราคาต้องอยู่ระหว่าง 1,000–30,000 บาท และมีทศนิยมไม่เกิน 2 ตำแหน่ง";
            return false;
        }
        return true;
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
