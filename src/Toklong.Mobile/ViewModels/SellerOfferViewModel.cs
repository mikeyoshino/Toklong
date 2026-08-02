using System.Collections.ObjectModel;
using System.Windows.Input;
using Toklong.Mobile.Core;

namespace Toklong.Mobile.ViewModels;

public sealed class SellerOfferViewModel(
    ISellerOfferService sellerOffers,
    IAddressService addresses,
    IMobileAnalytics analytics) : ObservableViewModel
{
    private string publicToken = "";
    private SellerOfferInvitation? invitation;
    private MobilePayoutAccount? selectedPayoutAccount;
    private BankOption? selectedBank;
    private string accountName = "";
    private string accountNumber = "";
    private string message = "";
    private bool transferRightsAttested;
    private bool acceptedTerms;
    private bool isBusy;
    private bool hasSavedOrigin;
    private bool useSavedOrigin = true;
    private bool rememberOrigin;
    private string savedOrigin = "";
    private string originAddressLine = "";
    private AddressOption? selectedOriginProvince;
    private AddressOption? selectedOriginDistrict;
    private SubdistrictOption? selectedOriginSubdistrict;
    private string weightGrams = "";
    private string widthCentimeters = "";
    private string lengthCentimeters = "";
    private string heightCentimeters = "";
    private MobileShippingQuote? selectedShippingQuote;
    private string shippingQuoteMessage = "";
    private bool isLoadingShippingQuotes;
    private int shippingQuoteInputVersion;

    public AppTransaction? Transaction => invitation?.Transaction;
    public string ProductName => Transaction?.ProductName ?? "";
    public string AgreementDetails => Transaction?.AgreementDetails ?? "";
    public string ItemPriceText => Transaction?.ItemPriceText ?? "";
    public string NetText => invitation is null
        ? ""
        : MoneyFormatter.Format(invitation.SellerExpectedNetSatang, "THB");
    public string DeadlineText => Transaction?.DeadlineText ?? "";
    public string PhotoUrl => Transaction?.PhotoUrl ?? Transaction?.ProductIcon ?? "";
    public bool HasDeliveryRegion =>
        Transaction?.HasDeliveryRegion == true;
    public string DeliveryRegionText =>
        Transaction?.DeliveryRegionText ?? "";
    public bool IsPhysical =>
        Transaction?.FulfillmentType ==
        AppFulfillmentType.Physical;

    public ObservableCollection<MobilePayoutAccount> PayoutAccounts { get; } = [];
    public ObservableCollection<AddressOption> OriginProvinces { get; } = [];
    public ObservableCollection<AddressOption> OriginDistricts { get; } = [];
    public ObservableCollection<SubdistrictOption> OriginSubdistricts { get; } = [];
    public ObservableCollection<MobileShippingQuote> ShippingQuotes { get; } = [];

    public IReadOnlyList<BankOption> Banks =>
        ThaiBankCatalog.Supported;

    public MobilePayoutAccount? SelectedPayoutAccount
    {
        get => selectedPayoutAccount;
        set => SetProperty(ref selectedPayoutAccount, value);
    }

    public BankOption? SelectedBank
    {
        get => selectedBank;
        set => SetProperty(ref selectedBank, value);
    }

    public string AccountName
    {
        get => accountName;
        set => SetProperty(ref accountName, value);
    }

    public string AccountNumber
    {
        get => accountNumber;
        set => SetProperty(
            ref accountNumber,
            new string((value ?? "")
                .Where(char.IsDigit)
                .Take(15)
                .ToArray()));
    }

    public bool HasPayoutAccounts => PayoutAccounts.Count > 0;
    public bool NeedsPayoutAccount => !HasPayoutAccounts;

    public bool HasSavedOrigin
    {
        get => hasSavedOrigin;
        private set
        {
            if (SetProperty(ref hasSavedOrigin, value))
            {
                OnPropertyChanged(nameof(ShowOriginEditor));
                OnPropertyChanged(nameof(ShowSavedOrigin));
            }
        }
    }

    public bool UseSavedOrigin
    {
        get => useSavedOrigin;
        set
        {
            if (SetProperty(ref useSavedOrigin, value))
            {
                ResetQuotes();
                OnPropertyChanged(nameof(ShowOriginEditor));
                OnPropertyChanged(nameof(ShowSavedOrigin));
            }
        }
    }

    public bool ShowOriginEditor =>
        IsPhysical &&
        (!HasSavedOrigin || !UseSavedOrigin);

    public bool ShowSavedOrigin =>
        IsPhysical &&
        HasSavedOrigin &&
        UseSavedOrigin;

    public string SavedOrigin
    {
        get => savedOrigin;
        private set => SetProperty(ref savedOrigin, value);
    }

    public string OriginAddressLine
    {
        get => originAddressLine;
        set
        {
            if (SetProperty(ref originAddressLine, value ?? ""))
                ResetQuotes();
        }
    }

    public bool RememberOrigin
    {
        get => rememberOrigin;
        set => SetProperty(ref rememberOrigin, value);
    }

    public AddressOption? SelectedOriginProvince
    {
        get => selectedOriginProvince;
        set
        {
            if (!SetProperty(ref selectedOriginProvince, value))
                return;
            SelectedOriginDistrict = null;
            OriginDistricts.Clear();
            OriginSubdistricts.Clear();
            ResetQuotes();
            if (value is not null)
                _ = LoadOriginDistrictsAsync(value.Id);
        }
    }

    public AddressOption? SelectedOriginDistrict
    {
        get => selectedOriginDistrict;
        set
        {
            if (!SetProperty(ref selectedOriginDistrict, value))
                return;
            SelectedOriginSubdistrict = null;
            OriginSubdistricts.Clear();
            ResetQuotes();
            if (value is not null)
                _ = LoadOriginSubdistrictsAsync(value.Id);
        }
    }

    public SubdistrictOption? SelectedOriginSubdistrict
    {
        get => selectedOriginSubdistrict;
        set
        {
            if (SetProperty(ref selectedOriginSubdistrict, value))
                ResetQuotes();
        }
    }

    public string WeightGrams
    {
        get => weightGrams;
        set => SetPackageValue(ref weightGrams, value);
    }

    public string WidthCentimeters
    {
        get => widthCentimeters;
        set => SetPackageValue(ref widthCentimeters, value);
    }

    public string LengthCentimeters
    {
        get => lengthCentimeters;
        set => SetPackageValue(ref lengthCentimeters, value);
    }

    public string HeightCentimeters
    {
        get => heightCentimeters;
        set => SetPackageValue(ref heightCentimeters, value);
    }

    public MobileShippingQuote? SelectedShippingQuote
    {
        get => selectedShippingQuote;
        set
        {
            if (SetProperty(ref selectedShippingQuote, value))
                OnPropertyChanged(nameof(ShippingFeeText));
        }
    }

    public bool HasShippingQuotes => ShippingQuotes.Count > 0;
    public string ShippingFeeText =>
        SelectedShippingQuote?.FeeText ?? "—";

    public string ShippingQuoteMessage
    {
        get => shippingQuoteMessage;
        private set
        {
            if (SetProperty(ref shippingQuoteMessage, value))
                OnPropertyChanged(nameof(HasShippingQuoteMessage));
        }
    }

    public bool HasShippingQuoteMessage =>
        !string.IsNullOrWhiteSpace(ShippingQuoteMessage);

    public bool IsLoadingShippingQuotes
    {
        get => isLoadingShippingQuotes;
        private set
        {
            if (SetProperty(ref isLoadingShippingQuotes, value))
                OnPropertyChanged(nameof(CanLoadShippingQuotes));
        }
    }

    public bool CanLoadShippingQuotes => !IsLoadingShippingQuotes;

    public bool TransferRightsAttested
    {
        get => transferRightsAttested;
        set => SetProperty(ref transferRightsAttested, value);
    }

    public bool AcceptedTerms
    {
        get => acceptedTerms;
        set => SetProperty(ref acceptedTerms, value);
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

    public bool IsBusy
    {
        get => isBusy;
        private set => SetProperty(ref isBusy, value);
    }

    public ICommand SavePayoutCommand => new AsyncCommand(SavePayoutAsync);
    public ICommand LoadShippingQuotesCommand =>
        new AsyncCommand(LoadShippingQuotesAsync);
    public ICommand EditOriginCommand => new Command(
        () => UseSavedOrigin = false);
    public ICommand UseSavedOriginCommand => new Command(
        () => UseSavedOrigin = true);
    public ICommand ConfirmReadyCommand =>
        new AsyncCommand(ConfirmReadyAsync);
    public ICommand DeclineCommand => new AsyncCommand(DeclineAsync);

    public async Task LoadAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            Message = "ลิงก์ข้อเสนอไม่ถูกต้อง";
            return;
        }

        publicToken = token.Trim();
        IsBusy = true;
        Message = "";
        try
        {
            invitation = await sellerOffers.GetAsync(publicToken);
            RaiseOfferChanged();
            ReplacePayoutAccounts(invitation.PayoutAccounts);
            ApplySavedOrigin(invitation.SavedShippingOrigin);
            if (IsPhysical)
                await LoadOriginProvincesAsync();
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

    private async Task SavePayoutAsync()
    {
        if (SelectedBank is null ||
            string.IsNullOrWhiteSpace(AccountName) ||
            AccountNumber.Length is < 10 or > 15)
        {
            Message = "เลือกธนาคารและกรอกบัญชีรับเงิน 10–15 หลักให้ครบ";
            return;
        }

        await RunAsync(async () =>
        {
            var accounts = await sellerOffers.SavePayoutAccountAsync(
                null,
                SelectedBank.Code,
                AccountName,
                AccountNumber);
            ReplacePayoutAccounts(accounts);
            AccountNumber = "";
            Message = "บันทึกบัญชีรับเงินแล้ว";
        });
    }

    private async Task ConfirmReadyAsync()
    {
        if (invitation is null)
            return;
        if (SelectedPayoutAccount is null)
        {
            analytics.Track(SellerReadinessAnalytics.ValidationFailed(
                invitation.Transaction.FulfillmentType,
                SellerReadinessFailureReason.PayoutAccount));
            Message = "เพิ่มหรือเลือกบัญชีรับเงินก่อนยืนยัน";
            return;
        }
        if (!TransferRightsAttested || !AcceptedTerms)
        {
            analytics.Track(SellerReadinessAnalytics.ValidationFailed(
                invitation.Transaction.FulfillmentType,
                SellerReadinessFailureReason.Confirmations));
            Message = "ยืนยันสิทธิ์ในสินค้าและยอมรับเงื่อนไขให้ครบ";
            return;
        }
        if (IsPhysical &&
            SelectedShippingQuote is null)
        {
            analytics.Track(SellerReadinessAnalytics.ValidationFailed(
                invitation.Transaction.FulfillmentType,
                SellerReadinessFailureReason.ShippingSelection));
            Message = "กรุณาระบุต้นทาง ขนาดพัสดุ และเลือกค่าจัดส่ง";
            return;
        }

        await RunAsync(async () =>
        {
            var transaction = await sellerOffers.AcceptAsync(
                publicToken,
                SelectedPayoutAccount.Id,
                TransferRightsAttested,
                AcceptedTerms,
                IsPhysical
                    ? BuildShippingSelection()
                    : null);
            analytics.Track(SellerReadinessAnalytics.Confirmed(
                invitation.Transaction.FulfillmentType));
            await Shell.Current.GoToAsync("..");
            await Shell.Current.GoToAsync(
                nameof(Pages.TransactionDetailPage),
                new Dictionary<string, object>
                {
                    ["TransactionId"] = transaction.Id
                });
        });
    }

    private async Task LoadShippingQuotesAsync()
    {
        if (IsLoadingShippingQuotes)
            return;
        ShippingQuoteMessage = "";
        if (!TryGetPackage(
                out var weight,
                out var width,
                out var length,
                out var height))
        {
            ShippingQuoteMessage =
                "กรอกน้ำหนักและขนาดพัสดุให้ครบ";
            return;
        }
        if (!UseSavedOrigin &&
            (string.IsNullOrWhiteSpace(OriginAddressLine) ||
             SelectedOriginProvince is null ||
             SelectedOriginDistrict is null ||
             SelectedOriginSubdistrict is null))
        {
            ShippingQuoteMessage = "กรอกที่อยู่ต้นทางให้ครบ";
            return;
        }

        var inputVersion = shippingQuoteInputVersion;
        ShippingQuotes.Clear();
        SelectedShippingQuote = null;
        OnPropertyChanged(nameof(HasShippingQuotes));
        IsLoadingShippingQuotes = true;
        try
        {
            var quotes = await sellerOffers.GetShippingQuotesAsync(
                publicToken,
                new SellerShippingQuoteRequest(
                    UseSavedOrigin,
                    UseSavedOrigin ? null : OriginAddressLine,
                    UseSavedOrigin
                        ? null
                        : SelectedOriginProvince?.Id,
                    UseSavedOrigin
                        ? null
                        : SelectedOriginDistrict?.Id,
                    UseSavedOrigin
                        ? null
                        : SelectedOriginSubdistrict?.Id,
                    weight,
                    width,
                    length,
                    height));
            if (inputVersion != shippingQuoteInputVersion)
                return;

            foreach (var quote in quotes)
                ShippingQuotes.Add(quote);
            SelectedShippingQuote = ShippingQuotes.FirstOrDefault();
            OnPropertyChanged(nameof(HasShippingQuotes));
            ShippingQuoteMessage = quotes.Count == 0
                ? "ยังไม่พบตัวเลือกจัดส่งสำหรับพัสดุนี้"
                : "";
        }
        catch (Exception)
        {
            if (inputVersion == shippingQuoteInputVersion)
            {
                ShippingQuoteMessage =
                    "ยังดูค่าจัดส่งไม่ได้ กรุณาลองอีกครั้ง";
            }
        }
        finally
        {
            IsLoadingShippingQuotes = false;
        }
    }

    private SellerShippingSelection BuildShippingSelection()
    {
        if (SelectedShippingQuote is null ||
            !TryGetPackage(
                out var weight,
                out var width,
                out var length,
                out var height))
            throw new InvalidOperationException(
                "กรุณาดูราคาและเลือกค่าจัดส่งใหม่");
        return new SellerShippingSelection(
            UseSavedOrigin,
            UseSavedOrigin ? null : OriginAddressLine,
            UseSavedOrigin
                ? null
                : SelectedOriginProvince?.Id,
            UseSavedOrigin
                ? null
                : SelectedOriginDistrict?.Id,
            UseSavedOrigin
                ? null
                : SelectedOriginSubdistrict?.Id,
            RememberOrigin,
            weight,
            width,
            length,
            height,
            SelectedShippingQuote.QuoteReference,
            SelectedShippingQuote.FeeSatang);
    }

    private void ApplySavedOrigin(
        MobileSavedShippingOrigin? origin)
    {
        HasSavedOrigin = origin is not null;
        UseSavedOrigin = origin is not null;
        SavedOrigin = origin?.DisplayText ?? "";
    }

    private async Task LoadOriginProvincesAsync()
    {
        if (OriginProvinces.Count > 0)
            return;
        var options =
            await addresses.GetProvincesAsync();
        foreach (var option in options)
            OriginProvinces.Add(option);
    }

    private async Task LoadOriginDistrictsAsync(
        int provinceId)
    {
        var options =
            await addresses.GetDistrictsAsync(provinceId);
        OriginDistricts.Clear();
        foreach (var option in options)
            OriginDistricts.Add(option);
    }

    private async Task LoadOriginSubdistrictsAsync(
        int districtId)
    {
        var options =
            await addresses.GetSubdistrictsAsync(districtId);
        OriginSubdistricts.Clear();
        foreach (var option in options)
            OriginSubdistricts.Add(option);
    }

    private bool TryGetPackage(
        out int weight,
        out int width,
        out int length,
        out int height)
    {
        var weightValid =
            int.TryParse(WeightGrams, out weight);
        var widthValid =
            int.TryParse(WidthCentimeters, out width);
        var lengthValid =
            int.TryParse(LengthCentimeters, out length);
        var heightValid =
            int.TryParse(HeightCentimeters, out height);
        return weightValid &&
               widthValid &&
               lengthValid &&
               heightValid &&
               weight is >= 1 and <= 30_000 &&
               width is >= 1 and <= 200 &&
               length is >= 1 and <= 200 &&
               height is >= 1 and <= 200;
    }

    private void SetPackageValue(
        ref string field,
        string? value)
    {
        var normalized = new string(
            (value ?? "")
                .Where(char.IsAsciiDigit)
                .Take(5)
                .ToArray());
        if (SetProperty(ref field, normalized))
            ResetQuotes();
    }

    private void ResetQuotes()
    {
        shippingQuoteInputVersion++;
        ShippingQuotes.Clear();
        SelectedShippingQuote = null;
        ShippingQuoteMessage = "";
        OnPropertyChanged(nameof(HasShippingQuotes));
    }

    private async Task DeclineAsync() =>
        await RunAsync(async () =>
        {
            await sellerOffers.DeclineAsync(publicToken);
            if (invitation is not null)
            {
                analytics.Track(SellerReadinessAnalytics.Declined(
                    invitation.Transaction.FulfillmentType));
            }
            await Shell.Current.GoToAsync("..");
        });

    private async Task RunAsync(Func<Task> action)
    {
        if (IsBusy)
            return;
        IsBusy = true;
        Message = "";
        try
        {
            await action();
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

    private void ReplacePayoutAccounts(
        IReadOnlyList<MobilePayoutAccount> accounts)
    {
        PayoutAccounts.Clear();
        foreach (var account in accounts)
            PayoutAccounts.Add(account);
        SelectedPayoutAccount =
            PayoutAccounts.FirstOrDefault(account => account.IsDefault) ??
            PayoutAccounts.FirstOrDefault();
        OnPropertyChanged(nameof(HasPayoutAccounts));
        OnPropertyChanged(nameof(NeedsPayoutAccount));
    }

    private void RaiseOfferChanged()
    {
        OnPropertyChanged(nameof(Transaction));
        OnPropertyChanged(nameof(ProductName));
        OnPropertyChanged(nameof(AgreementDetails));
        OnPropertyChanged(nameof(ItemPriceText));
        OnPropertyChanged(nameof(NetText));
        OnPropertyChanged(nameof(DeadlineText));
        OnPropertyChanged(nameof(PhotoUrl));
        OnPropertyChanged(nameof(HasDeliveryRegion));
        OnPropertyChanged(nameof(DeliveryRegionText));
        OnPropertyChanged(nameof(IsPhysical));
        OnPropertyChanged(nameof(ShowOriginEditor));
        OnPropertyChanged(nameof(ShowSavedOrigin));
    }
}
