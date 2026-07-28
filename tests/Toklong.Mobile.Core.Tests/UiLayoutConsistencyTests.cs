using System.Xml.Linq;

namespace Toklong.Mobile.Core.Tests;

public sealed class UiLayoutConsistencyTests
{
    private static readonly XNamespace Maui = "http://schemas.microsoft.com/dotnet/2021/maui";

    [Fact]
    public void SharedFormTokens_KeepPhoneAndVerificationCodeAligned()
    {
        var resources = Load("Ui", "App.xaml");

        Assert.Equal("52", ResourceValue(resources, "InputMinimumHeight"));
        Assert.Equal("16", StyleSetterValue(resources, "RefinedEntry", "FontSize"));
        Assert.Equal(
            "{StaticResource InputMinimumHeight}",
            StyleSetterValue(resources, "RefinedInputBorder", "MinimumHeightRequest"));

        var signIn = Load("Ui", "Pages", "SignInPage.xaml");
        var sharedPhoneField = signIn
            .Descendants()
            .Single(element =>
                element.Name.LocalName ==
                "ThaiMobilePhoneField");
        Assert.Equal(
            "{Binding PhoneNumber, Mode=TwoWay}",
            AttributeValue(
                sharedPhoneField,
                "PhoneNumber"));

        var phoneField = Load(
            "Ui",
            "Controls",
            "ThaiMobilePhoneField.xaml");
        var phone = phoneField
            .Descendants()
            .Single(element =>
                element.Name.LocalName ==
                "ThaiMobilePhoneEntry");
        Assert.Equal("{StaticResource RefinedEntry}", AttributeValue(phone, "Style"));
        Assert.Equal(
            "{Binding PhoneNumber, Source={x:Reference Root}, Mode=TwoWay}",
            AttributeValue(phone, "PhoneNumber"));
        Assert.Null(phone.Attribute("Text"));
        Assert.Equal(
            "{StaticResource RefinedInputBorder}",
            AttributeValue(phone.Parent!.Parent!, "Style"));

        var verifyCode = Load("Ui", "Pages", "VerifyCodePage.xaml");
        var codeInput = verifyCode
            .Descendants()
            .Single(element => element.Name.LocalName == "OtpCodeInput");
        Assert.Equal(
            "{Binding Code, Mode=TwoWay}",
            AttributeValue(codeInput, "Code"));

        var otpControl = Load("Ui", "Controls", "OtpCodeInput.xaml");
        var codeEntry = otpControl.Descendants(Maui + "Entry").Single();
        Assert.Equal(
            "{StaticResource RefinedEntry}",
            AttributeValue(codeEntry, "Style"));
        Assert.Equal("6", AttributeValue(codeEntry, "MaxLength"));
        Assert.Equal("Numeric", AttributeValue(codeEntry, "Keyboard"));
        Assert.Equal("OtpCodeEntry", AttributeValue(codeEntry, "AutomationId"));
        Assert.Equal("0.01", AttributeValue(codeEntry, "Opacity"));
        Assert.Equal(6, otpControl.Descendants(Maui + "BoxView").Count());
        Assert.Null(codeEntry.Attribute("FontSize"));
    }

    [Fact]
    public void AuthenticationStartsAtWelcome_AndKeepsLoginAndSignupSeparate()
    {
        var shell = Load("Ui", "AppShell.xaml");
        var firstShellContent = shell
            .Descendants()
            .First(element => element.Name.LocalName == "ShellContent");
        Assert.Equal("welcome", AttributeValue(firstShellContent, "Route"));

        var welcome = Load("Ui", "Pages", "WelcomePage.xaml");
        var buttonLabels = welcome
            .Descendants(Maui + "Button")
            .Select(button => AttributeValue(button, "Text"))
            .ToArray();

        Assert.Contains("เข้าสู่ระบบ", buttonLabels);
        Assert.Contains("สมัครสมาชิก", buttonLabels);
        Assert.Contains(
            welcome.Descendants(),
            element =>
                element.Name.LocalName ==
                "CenteredAuthBrandView");
    }

    [Fact]
    public void AuthenticatedHome_UsesCenteredBrandAndBuyerFirstActions()
    {
        var home = Load(
            "Ui",
            "Pages",
            "AuthenticatedHomePage.xaml");
        var labels = home
            .Descendants(Maui + "Label")
            .Select(label => AttributeValue(label, "Text"))
            .ToArray();
        var buttons = home.Descendants(Maui + "Button").ToArray();

        Assert.Contains(
            home.Descendants(),
            element =>
                element.Name.LocalName ==
                "CenteredAuthBrandView");
        Assert.Contains("เริ่มดีลอย่างมั่นใจ", labels);
        Assert.Contains(
            "สร้างข้อเสนอซื้อ หรือจัดการรายการขาย",
            labels);
        Assert.Contains(
            buttons,
            button =>
                AttributeValue(button, "AutomationId") ==
                    "OpenBuyingHomeButton" &&
                AttributeValue(
                    button,
                    "SemanticProperties.Description") ==
                    "ซื้อ สร้างข้อเสนอ ตรวจรายละเอียด และติดตามรายการ");
        Assert.Contains(
            buttons,
            button =>
                AttributeValue(button, "AutomationId") ==
                    "OpenSellingHomeButton" &&
                AttributeValue(
                    button,
                    "SemanticProperties.Description") ==
                    "ขาย ตรวจข้อเสนอ ส่งสินค้า และติดตามยอดรับ");
        Assert.Contains(
            buttons,
            button =>
                AttributeValue(button, "AutomationId") ==
                    "OpenAllTransactionsButton" &&
                AttributeValue(button, "Text") ==
                    "รายการของฉัน");
        Assert.DoesNotContain("เข้าสู่ระบบ", labels);
        Assert.DoesNotContain("สมัครสมาชิก", labels);
        Assert.DoesNotContain("สร้างลิงก์ขาย", labels);
    }

    [Fact]
    public void Shell_RegistersAuthenticatedHomeOutsideTheMainTabBar()
    {
        var shell = Load("Ui", "AppShell.xaml");
        var home = shell
            .Descendants(Maui + "ShellContent")
            .Single(element =>
                AttributeValue(element, "Route") == "home");

        Assert.Equal(
            "{DataTemplate pages:AuthenticatedHomePage}",
            AttributeValue(home, "ContentTemplate"));
        Assert.Equal(
            "False",
            AttributeValue(home, "Shell.FlyoutItemIsVisible"));
        Assert.DoesNotContain(
            home.Ancestors(),
            element => element.Name.LocalName == "TabBar");
    }

    [Fact]
    public void RegistrationCompletionCollectsEmail_AndCheckoutDoesNotRenderAnEmailField()
    {
        var completion = Load(
            "Ui",
            "Pages",
            "CompleteRegistrationPage.xaml");
        var emailEntry = completion
            .Descendants(Maui + "Entry")
            .Single(entry =>
                AttributeValue(entry, "Text") == "{Binding Email}");
        Assert.Equal("Email", AttributeValue(emailEntry, "Keyboard"));
        Assert.Equal("254", AttributeValue(emailEntry, "MaxLength"));

        var detail = Load(
            "Ui",
            "Pages",
            "TransactionDetailPage.xaml");
        Assert.DoesNotContain(
            detail.Descendants(Maui + "Entry"),
            entry =>
                AttributeValue(entry, "Text") == "{Binding ReceiptEmail}");
    }

    [Fact]
    public void HomeActionSpotlightHasRequestedEmptyState()
    {
        var transactions = Load(
            "Ui",
            "Pages",
            "TransactionsPage.xaml");
        var labels = transactions
            .Descendants(Maui + "Label")
            .Select(label => AttributeValue(label, "Text"))
            .ToArray();
        var emptyState = transactions
            .Descendants(Maui + "Grid")
            .Single(element =>
                AttributeValue(element, "AutomationId") ==
                "ActionSpotlightEmptyState");
        var spotlight = transactions
            .Descendants(Maui + "Border")
            .Single(element =>
                AttributeValue(element, "AutomationId") ==
                "ActionSpotlightCard");

        Assert.Contains("ยังไม่มีรายการ", labels);
        Assert.DoesNotContain("รายการดำเนินเรียบร้อย", labels);
        Assert.Equal(
            AttributeValue(spotlight, "MinimumHeightRequest"),
            AttributeValue(emptyState, "MinimumHeightRequest"));
        Assert.Contains(
            emptyState.Descendants(Maui + "Label"),
            label => AttributeValue(label, "Text") == "ยังไม่มีรายการ");
    }

    [Fact]
    public void TransactionsUseTopLevelBuySellModes()
    {
        var transactions = Load(
            "Ui",
            "Pages",
            "TransactionsPage.xaml");
        var pageTitle = transactions
            .Descendants(Maui + "Label")
            .Single(label =>
                AttributeValue(label, "AutomationId") ==
                "TransactionsPageTitle");
        var modeSwitch = transactions
            .Descendants(Maui + "Border")
            .Single(border =>
                AttributeValue(border, "AutomationId") ==
                "TransactionRoleModeSwitch");
        var modeLabels = modeSwitch
            .Descendants(Maui + "Button")
            .Select(button => AttributeValue(button, "Text"))
            .ToArray();
        var buyerFilters = transactions
            .Descendants(Maui + "ScrollView")
            .Single(scroll =>
                AttributeValue(scroll, "AutomationId") ==
                "BuyerStatusFilters");
        var sellerFilters = transactions
            .Descendants(Maui + "ScrollView")
            .Single(scroll =>
                AttributeValue(scroll, "AutomationId") ==
                "SellerStatusFilters");

        Assert.Equal("รายการของคุณ", AttributeValue(pageTitle, "Text"));
        Assert.DoesNotContain(
            transactions.Descendants(Maui + "Label"),
            label => AttributeValue(label, "Text") == "TOKLONG");
        Assert.Equal(new[] { "ซื้อ", "ขาย" }, modeLabels);
        Assert.Equal(
            "{Binding IsBuying}",
            AttributeValue(buyerFilters, "IsVisible"));
        Assert.Equal(
            "{Binding IsSelling}",
            AttributeValue(sellerFilters, "IsVisible"));
        Assert.Contains(
            transactions.Descendants(Maui + "Label"),
            label =>
                AttributeValue(label, "Text") ==
                "{Binding ModeSectionTitle}");
        Assert.Contains(
            transactions.Descendants(Maui + "Button"),
            button =>
                AttributeValue(button, "Text") ==
                    "+ สร้างดีลซื้อ" &&
                AttributeValue(button, "IsVisible") ==
                    "{Binding IsBuying}");
    }

    [Fact]
    public void CreateOffer_UsesThreeFullPageStepsAndProgressText()
    {
        var create = Load("Ui", "Pages", "CreateOfferPage.xaml");
        var labels = create
            .Descendants(Maui + "Label")
            .Select(label => AttributeValue(label, "Text"))
            .ToArray();
        var progress = create
            .Descendants(Maui + "Grid")
            .Single(element =>
                AttributeValue(element, "AutomationId") ==
                    "CreateOfferProgress");
        var steps = create
            .Descendants(Maui + "ScrollView")
            .Where(element =>
                AttributeValue(element, "AutomationId") is
                    "CreateOfferDealStep" or
                    "CreateOfferFulfillmentStep" or
                    "CreateOfferReviewStep")
            .ToArray();

        Assert.Contains("สร้างข้อเสนอ", labels);
        Assert.Contains("ข้อมูลดีล", labels);
        Assert.Contains("การรับสินค้า", labels);
        Assert.Contains("ตรวจและส่ง", labels);
        Assert.Contains("{Binding ProgressText}", labels);
        Assert.Equal(3, progress.Elements(Maui + "BoxView").Count());
        Assert.Equal(3, steps.Length);
        Assert.Equal(
            "{Binding IsDealStep}",
            AttributeValue(steps[0], "IsVisible"));
        Assert.Equal(
            "{Binding IsFulfillmentStep}",
            AttributeValue(steps[1], "IsVisible"));
        Assert.Equal(
            "{Binding IsReviewStep}",
            AttributeValue(steps[2], "IsVisible"));
        Assert.DoesNotContain(
            create.Descendants(),
            element =>
                AttributeValue(element, "AutomationId") ==
                    "QuickDealReviewSheet");
    }

    [Fact]
    public void CreateOffer_EachStepHasOnePrimaryForwardAction()
    {
        var create = Load("Ui", "Pages", "CreateOfferPage.xaml");
        var expected = new[]
        {
            (
                Step: "CreateOfferDealStep",
                Command: "{Binding ContinueFromDealCommand}",
                Text: "ถัดไป: การรับสินค้า  →"),
            (
                Step: "CreateOfferFulfillmentStep",
                Command: "{Binding ContinueFromFulfillmentCommand}",
                Text: "ถัดไป: ตรวจข้อมูล  →"),
            (
                Step: "CreateOfferReviewStep",
                Command: "{Binding SubmitCommand}",
                Text: "ส่งข้อเสนอให้ผู้ขาย")
        };

        foreach (var item in expected)
        {
            var step = create
                .Descendants(Maui + "ScrollView")
                .Single(element =>
                    AttributeValue(element, "AutomationId") ==
                        item.Step);
            var primary = step
                .Descendants(Maui + "Button")
                .Single(button =>
                    AttributeValue(button, "Command") ==
                        item.Command);

            Assert.Equal(item.Text, AttributeValue(primary, "Text"));
        }
    }

    [Fact]
    public void CreateOffer_RendersInlineErrorsBesideNamedTargets()
    {
        var create = Load("Ui", "Pages", "CreateOfferPage.xaml");
        var errorBindings = create
            .Descendants(Maui + "Label")
            .Select(label => AttributeValue(label, "Text"))
            .Where(text => text is not null &&
                           text.EndsWith("Error}"))
            .ToArray();
        var targetNames = create
            .Descendants()
            .Select(element => AttributeValue(element, "Name"))
            .Where(name => name is not null)
            .ToArray();

        Assert.Contains("{Binding SellerPhoneError}", errorBindings);
        Assert.Contains("{Binding ProductNameError}", errorBindings);
        Assert.Contains("{Binding ProductPhotoError}", errorBindings);
        Assert.Contains("{Binding AmountError}", errorBindings);
        Assert.Contains("{Binding DeliveryAddressError}", errorBindings);
        Assert.Contains("{Binding ConditionError}", errorBindings);
        Assert.Contains("{Binding KnownDefectsError}", errorBindings);
        Assert.Contains("SellerPhoneEntry", targetNames);
        Assert.Contains("ProductNameEntry", targetNames);
        Assert.Contains("ProductPhotoButton", targetNames);
        Assert.Contains("AmountEntry", targetNames);
        Assert.Contains("DeliveryAddressAnchor", targetNames);
        Assert.Contains("ConditionPickerAnchor", targetNames);
        Assert.Contains("KnownDefectsEditor", targetNames);
    }

    [Fact]
    public void CreateOffer_ReviewContainsTheOnlyBuyerCostBreakdown()
    {
        var create = Load("Ui", "Pages", "CreateOfferPage.xaml");
        var review = create
            .Descendants(Maui + "ScrollView")
            .Single(element =>
                AttributeValue(element, "AutomationId") ==
                    "CreateOfferReviewStep");
        var costSummary = review
            .Descendants(Maui + "Border")
            .Single(border =>
                AttributeValue(border, "AutomationId") ==
                    "ReviewCostSummary");

        Assert.Single(
            create.Descendants(Maui + "Border"),
            border =>
                AttributeValue(border, "AutomationId") ==
                    "ReviewCostSummary");
        Assert.Contains(
            costSummary.Descendants(Maui + "Label"),
            label =>
                AttributeValue(label, "Text") ==
                    "{Binding CostProtectionFeeText}");
        Assert.Contains(
            costSummary.Descendants(Maui + "Label"),
            label =>
                AttributeValue(label, "Text") ==
                    "{Binding CostShippingText}");
        Assert.Contains(
            costSummary.Descendants(Maui + "Label"),
            label =>
                AttributeValue(label, "Text") ==
                    "{Binding CostTotalText}");
        Assert.Contains(
            costSummary.Descendants(Maui + "Label"),
            label =>
                AttributeValue(label, "Text") ==
                    "ยังไม่ตัดเงินในขั้นตอนนี้");
    }

    [Fact]
    public void CreateOffer_ConditionChoicesExposeTheirSelectedState()
    {
        var create = Load("Ui", "Pages", "CreateOfferPage.xaml");
        var review = create
            .Descendants(Maui + "ScrollView")
            .Single(element =>
                AttributeValue(element, "AutomationId") ==
                    "CreateOfferReviewStep");
        var selectedBindings = review
            .Descendants(Maui + "Button")
            .SelectMany(button =>
                button.Descendants(Maui + "DataTrigger"))
            .Select(trigger =>
                AttributeValue(trigger, "Binding"))
            .ToArray();

        Assert.Contains("{Binding IsNewCondition}", selectedBindings);
        Assert.Contains("{Binding IsUsedGoodCondition}", selectedBindings);
        Assert.Contains("{Binding IsUsedDefectCondition}", selectedBindings);
    }

    [Fact]
    public void CreateOffer_FulfillmentOffersAddressCatalogRetry()
    {
        var create = Load("Ui", "Pages", "CreateOfferPage.xaml");
        var fulfillment = create
            .Descendants(Maui + "ScrollView")
            .Single(element =>
                AttributeValue(element, "AutomationId") ==
                    "CreateOfferFulfillmentStep");

        Assert.Contains(
            fulfillment.Descendants(Maui + "Label"),
            label =>
                AttributeValue(label, "Text") ==
                    "{Binding AddressLoadError}");
        Assert.Contains(
            fulfillment.Descendants(Maui + "Button"),
            button =>
                AttributeValue(button, "Command") ==
                    "{Binding RetryAddressCommand}" &&
                AttributeValue(button, "Text") ==
                    "ลองอีกครั้ง");
    }

    [Fact]
    public void CreateOffer_AiHelperStaysCollapsedBehindOneAction()
    {
        var create = Load("Ui", "Pages", "CreateOfferPage.xaml");
        var openButton = create
            .Descendants(Maui + "Button")
            .Single(button =>
                AttributeValue(button, "AutomationId") ==
                    "OpenAiAgreementDraftButton");
        var sheet = create
            .Descendants(Maui + "Grid")
            .Single(grid =>
                AttributeValue(grid, "AutomationId") ==
                    "AiAgreementDraftSheet");

        Assert.Equal("ให้ AI ช่วยกรอก", AttributeValue(openButton, "Text"));
        Assert.Equal("ui_ai_assist.png", AttributeValue(openButton, "ImageSource"));
        Assert.Equal("{Binding IsAiSheetOpen}", AttributeValue(sheet, "IsVisible"));
        Assert.Contains(
            sheet.Descendants(Maui + "Editor"),
            editor =>
                AttributeValue(editor, "Text") ==
                    "{Binding AiChatText}");
        Assert.Contains(
            sheet.Descendants(Maui + "Button"),
            button =>
                AttributeValue(button, "AutomationId") ==
                    "ApplyAiAgreementDraftButton");
    }

    [Fact]
    public void OtpInput_UsesSixUnderlineSlots_InsteadOfVisibleTextBoxes()
    {
        var verifyCode = Load("Ui", "Pages", "VerifyCodePage.xaml");

        Assert.DoesNotContain(
            verifyCode.Descendants(),
            element => element.Name.LocalName == "Entry");
        Assert.DoesNotContain(
            verifyCode.Descendants(),
            element => element.Name.LocalName == "RefinedInputBorder");

        var otpControl = Load("Ui", "Controls", "OtpCodeInput.xaml");
        Assert.Equal(6, otpControl.Descendants(Maui + "BoxView").Count());
        Assert.Equal(
            6,
            otpControl
                .Descendants(Maui + "Label")
                .Count(label => AttributeValue(label, "FontSize") == "25"));
    }

    [Fact]
    public void TrackingForm_UsesSupportedCarrierPicker_NotFreeText()
    {
        var detail = Load(
            "Ui",
            "Pages",
            "TransactionDetailPage.xaml");
        var carrierPicker = detail
            .Descendants(Maui + "Picker")
            .Single(picker =>
                AttributeValue(
                    picker,
                    "ItemsSource") == "{Binding Carriers}");

        Assert.Equal(
            "{Binding SelectedCarrier}",
            AttributeValue(carrierPicker, "SelectedItem"));
        Assert.Equal(
            "{StaticResource RefinedPicker}",
            AttributeValue(carrierPicker, "Style"));
        Assert.DoesNotContain(
            detail.Descendants(Maui + "Entry"),
            entry => AttributeValue(
                entry,
                "Text") == "{Binding CarrierCode}");
    }

    [Fact]
    public void ManagedShippingLabelOpensAFullScreenViewer()
    {
        var detail = Load(
            "Ui",
            "Pages",
            "TransactionDetailPage.xaml");
        var labelCard = detail
            .Descendants(Maui + "Border")
            .Single(border =>
                AttributeValue(border, "AutomationId") ==
                "ManagedShippingLabelCard");
        Assert.Contains(
            labelCard.Descendants(Maui + "TapGestureRecognizer"),
            gesture =>
                AttributeValue(gesture, "Command") ==
                "{Binding OpenShippingLabelCommand}");
        Assert.Contains(
            labelCard.Descendants(Maui + "Label"),
            label =>
                AttributeValue(label, "Text") ==
                "›");
        Assert.DoesNotContain(
            labelCard.Descendants(Maui + "Label"),
            label =>
                AttributeValue(label, "Text") ==
                "↗");
        Assert.Contains(
            detail.Descendants(Maui + "Button"),
            button =>
                AttributeValue(button, "AutomationId") ==
                    "OpenFullShippingLabelButton" &&
                AttributeValue(button, "Text") ==
                    "แตะเพื่อดูใบปะหน้าเต็มจอ");

        var viewer = Load(
            "Ui",
            "Pages",
            "ShippingLabelPage.xaml");
        var webView = viewer
            .Descendants(Maui + "WebView")
            .Single();
        Assert.Equal(
            "{Binding LabelSource}",
            AttributeValue(webView, "Source"));
        Assert.Equal(
            "ShippingLabelWebView",
            AttributeValue(webView, "AutomationId"));
        Assert.Contains(
            viewer.Descendants(Maui + "Button"),
            button =>
                AttributeValue(button, "AutomationId") ==
                    "SaveShippingLabelButton" &&
                AttributeValue(button, "Command") ==
                    "{Binding SaveCommand}" &&
                AttributeValue(button, "Text") ==
                    "บันทึกลงเครื่อง");
        Assert.Contains(
            viewer.Descendants(Maui + "Button"),
            button =>
                AttributeValue(button, "AutomationId") ==
                    "ShareOrPrintShippingLabelButton" &&
                AttributeValue(button, "Command") ==
                    "{Binding ShareOrPrintCommand}" &&
                AttributeValue(button, "Text") ==
                    "แชร์หรือพิมพ์");
        Assert.DoesNotContain(
            viewer.Descendants(Maui + "Label"),
            label =>
                AttributeValue(label, "FontAttributes") ==
                    "Bold");
    }

    [Fact]
    public void SellerDetailShowsOnlyUsefulProductInformation()
    {
        var detail = Load(
            "Ui",
            "Pages",
            "TransactionDetailPage.xaml");
        var agreementDisclosure = detail
            .Descendants(Maui + "Grid")
            .Single(grid =>
                AttributeValue(grid, "AutomationId") ==
                "SellerAgreementDetailsDisclosure");

        Assert.Contains(
            agreementDisclosure.Descendants(Maui + "TapGestureRecognizer"),
            gesture =>
                AttributeValue(gesture, "Command") ==
                "{Binding ToggleAgreementDetailsCommand}");
        Assert.Contains(
            detail.Descendants(Maui + "VerticalStackLayout"),
            stack =>
                AttributeValue(stack, "IsVisible") ==
                "{Binding ShowAgreementDetailsContent}");
        Assert.Contains(
            detail.Descendants(Maui + "Label"),
            label =>
                AttributeValue(label, "Text") ==
                "{Binding Transaction.ConditionLabel}");
        Assert.Contains(
            detail.Descendants(Maui + "Label"),
            label =>
                AttributeValue(label, "Text") ==
                "{Binding Transaction.KnownDefects}");
        Assert.DoesNotContain(
            detail.Descendants(Maui + "Label"),
            label =>
                AttributeValue(label, "Text") ==
                "{Binding Transaction.AgreementEvidenceHash}");
        Assert.DoesNotContain(
            detail.Descendants(Maui + "Label"),
            label =>
                AttributeValue(label, "Text") ==
                "หลักฐานข้อตกลงร่วม");
        Assert.DoesNotContain(
            detail.Descendants(Maui + "Label"),
            label =>
                AttributeValue(label, "Text") ==
                "{Binding Transaction.TermsDisplayText}");
        Assert.DoesNotContain(
            detail.Descendants(),
            element =>
                AttributeValue(element, "AutomationId") ==
                    "TransactionRecordDisclosure");
    }

    [Fact]
    public void SellerOffer_HidesBuyerProtectionAndBuyerTotal()
    {
        var sellerOffer = Load(
            "Ui",
            "Pages",
            "SellerOfferPage.xaml");
        var labelTexts = sellerOffer
            .Descendants(Maui + "Label")
            .Select(label => AttributeValue(label, "Text"))
            .ToArray();

        Assert.DoesNotContain(
            "ค่าคุ้มครองที่ผู้ซื้อจ่าย",
            labelTexts);
        Assert.DoesNotContain("{Binding FeeText}", labelTexts);
        Assert.DoesNotContain("ยอดที่ผู้ซื้อชำระ", labelTexts);
        Assert.DoesNotContain(
            "{Binding BuyerTotalText}",
            labelTexts);
        Assert.Contains("ยอดที่คาดว่าจะได้รับ", labelTexts);
        Assert.Contains("{Binding NetText}", labelTexts);
        Assert.Contains(
            "ค่าจัดส่งที่ผู้ซื้อจ่าย",
            labelTexts);
    }

    [Fact]
    public void TransactionDetail_ShowsProtectionAndTotalOnlyToBuyer()
    {
        var detail = Load(
            "Ui",
            "Pages",
            "TransactionDetailPage.xaml");
        var buyerCost = detail
            .Descendants(Maui + "VerticalStackLayout")
            .Single(stack =>
                AttributeValue(stack, "AutomationId") ==
                    "BuyerCostDisclosure");

        Assert.Equal(
            "{Binding Transaction.IsBuyerRole}",
            AttributeValue(buyerCost, "IsVisible"));
        Assert.Contains(
            buyerCost.Descendants(Maui + "Label"),
            label =>
                AttributeValue(label, "Text") ==
                    "{Binding Transaction.ItemPriceText}");
        Assert.Contains(
            buyerCost.Descendants(Maui + "Label"),
            label =>
                AttributeValue(label, "Text") ==
                    "ค่าคุ้มครองผู้ซื้อ");
        Assert.Contains(
            buyerCost.Descendants(Maui + "Label"),
            label =>
                AttributeValue(label, "Text") ==
                    "{Binding Transaction.FeeText}");
        Assert.Contains(
            buyerCost.Descendants(Maui + "Label"),
            label =>
                AttributeValue(label, "Text") ==
                    "{Binding Transaction.FormattedAmount}");

        var sellerPayout = detail
            .Descendants(Maui + "VerticalStackLayout")
            .Single(stack =>
                AttributeValue(stack, "AutomationId") ==
                    "SellerPayoutDisclosure");
        Assert.Equal(
            "{Binding Transaction.IsSellerRole}",
            AttributeValue(sellerPayout, "IsVisible"));
        Assert.Contains(
            sellerPayout.Descendants(Maui + "Label"),
            label =>
                AttributeValue(label, "Text") ==
                    "{Binding Transaction.ItemPriceText}");
        Assert.Contains(
            sellerPayout.Descendants(Maui + "Label"),
            label =>
                AttributeValue(label, "Text") ==
                    "{Binding Transaction.SellerNetText}");
        Assert.DoesNotContain(
            sellerPayout.Descendants(Maui + "Label"),
            label =>
                AttributeValue(label, "Text") ==
                    "{Binding Transaction.FeeText}");
    }

    [Fact]
    public void BuyerPayment_IsInlineAndRendersTheLockedAddressOnce()
    {
        var detail = Load(
            "Ui",
            "Pages",
            "TransactionDetailPage.xaml");
        var labels = detail
            .Descendants(Maui + "Label")
            .Select(label => AttributeValue(label, "Text"))
            .ToArray();
        var payment = detail
            .Descendants(Maui + "VerticalStackLayout")
            .Single(stack =>
                AttributeValue(stack, "AutomationId") ==
                    "BuyerPaymentControls");

        Assert.Equal(
            "{Binding IsPaymentAction}",
            AttributeValue(payment, "IsVisible"));
        Assert.DoesNotContain(
            "เช็กให้ครบก่อนจ่าย",
            labels);
        Assert.DoesNotContain(
            "ระบบใช้อีเมลจากบัญชีของคุณส่งใบเสร็จและขั้นตอนคืนเงิน",
            labels);
        Assert.DoesNotContain(
            "ที่อยู่จัดส่งที่ล็อกกับดีล",
            labels);
        Assert.DoesNotContain(
            "ที่อยู่นี้เลือกไว้ตั้งแต่สร้างดีลและแก้ในขั้นชำระไม่ได้ หากไม่ถูกต้องให้สร้างข้อเสนอใหม่",
            labels);
        Assert.Single(
            detail.Descendants(Maui + "Label"),
            label =>
                AttributeValue(label, "Text") ==
                    "{Binding Transaction.DeliveryAddressText}");
        Assert.Contains(
            payment.Descendants(Maui + "CheckBox"),
            checkBox =>
                AttributeValue(checkBox, "IsChecked") ==
                    "{Binding AcceptedTerms}" &&
                AttributeValue(
                    checkBox,
                    "SemanticProperties.Description") ==
                    "ยืนยันว่าได้ตรวจรายละเอียดและเงื่อนไขแล้ว");
        Assert.Contains(
            payment.Descendants(Maui + "Button"),
            button =>
                AttributeValue(button, "Command") ==
                    "{Binding PrimaryActionCommand}" &&
                AttributeValue(button, "Text") ==
                    "{Binding Transaction.FormattedAmount, StringFormat='ชำระ {0}'}" &&
                AttributeValue(
                    button,
                    "SemanticProperties.Description") ==
                    "{Binding Transaction.FormattedAmount, StringFormat='เปิดหน้าจ่ายเงินยอด {0}'}");
    }

    [Fact]
    public void TransactionDetailGradients_HaveColorsBeforeTransactionLoads()
    {
        var detail = Load(
            "Ui",
            "Pages",
            "TransactionDetailPage.xaml");
        var dynamicGradientStops = detail
            .Descendants(Maui + "GradientStop")
            .Select(stop => AttributeValue(stop, "Color"))
            .Where(color =>
                color?.Contains(
                    "{Binding Transaction.",
                    StringComparison.Ordinal) == true)
            .ToArray();

        Assert.Equal(5, dynamicGradientStops.Length);
        Assert.All(
            dynamicGradientStops,
            color =>
            {
                Assert.Contains(
                    "FallbackValue=#",
                    color,
                    StringComparison.Ordinal);
                Assert.Contains(
                    "TargetNullValue=#",
                    color,
                    StringComparison.Ordinal);
            });
    }

    [Fact]
    public void BuyerCanSharePhoneProtectedOfferWithoutClipboardOpen()
    {
        var detail = Load(
            "Ui",
            "Pages",
            "TransactionDetailPage.xaml");
        var transactions = Load(
            "Ui",
            "Pages",
            "TransactionsPage.xaml");

        Assert.DoesNotContain(
            transactions.Descendants(),
            element =>
                AttributeValue(element, "AutomationId") ==
                "OpenCopiedSellerLinkButton");
        var invitationCard = detail
            .Descendants()
            .Single(element =>
                AttributeValue(element, "AutomationId") ==
                "SellerInvitationCard");
        Assert.Equal(
            "{Binding ShowSellerInvitation}",
            AttributeValue(invitationCard, "IsVisible"));
        Assert.Contains(
            invitationCard.Descendants(Maui + "Button"),
            button =>
                AttributeValue(button, "AutomationId") ==
                    "CopySellerInvitationLinkButton" &&
                AttributeValue(button, "Command") ==
                    "{Binding CopyInvitationLinkCommand}");
        Assert.Contains(
            invitationCard.Descendants(Maui + "Button"),
            button =>
                AttributeValue(button, "AutomationId") ==
                    "ShareSellerInvitationLinkButton" &&
                AttributeValue(button, "Command") ==
                    "{Binding ShareInvitationLinkCommand}");
    }

    [Fact]
    public void TransactionDetail_ReferencesOnlyDeclaredResources()
    {
        var app = Load("Ui", "App.xaml");
        var detail = Load(
            "Ui",
            "Pages",
            "TransactionDetailPage.xaml");
        var declaredKeys = app
            .Descendants()
            .Concat(detail.Descendants())
            .Select(element =>
                AttributeValue(element, "Key"))
            .Where(key =>
                !string.IsNullOrWhiteSpace(key))
            .ToHashSet(StringComparer.Ordinal);
        var resourcePrefix = "{StaticResource ";
        var referencedKeys = detail
            .Descendants()
            .Attributes()
            .Select(attribute => attribute.Value)
            .Where(value =>
                value.StartsWith(
                    resourcePrefix,
                    StringComparison.Ordinal) &&
                value.EndsWith(
                    '}'))
            .Select(value =>
                value[resourcePrefix.Length..^1])
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.All(
            referencedKeys,
            key => Assert.True(
                declaredKeys.Contains(key),
                $"TransactionDetailPage.xaml references missing resource '{key}'."));
    }

    [Fact]
    public void CreateOffer_RequiresTargetSellerPhoneAndProductName()
    {
        var create = Load(
            "Ui",
            "Pages",
            "CreateOfferPage.xaml");
        var sellerPhone = create
            .Descendants()
            .Single(element =>
                element.Name.LocalName ==
                    "ThaiMobilePhoneEntry" &&
                AttributeValue(
                    element,
                    "PhoneNumber") ==
                    "{Binding SellerPhoneNumber}");
        Assert.Equal(
            "{StaticResource RefinedEntry}",
            AttributeValue(sellerPhone, "Style"));

        var productName = create
            .Descendants(Maui + "Entry")
            .Single(entry =>
                AttributeValue(
                    entry,
                    "Text") ==
                    "{Binding ProductName}");
        Assert.Equal(
            "180",
            AttributeValue(productName, "MaxLength"));
        Assert.Equal(
            "{StaticResource RefinedEntry}",
            AttributeValue(productName, "Style"));
    }

    [Fact]
    public void ActivityPage_UsesApiBackedNotificationCollection()
    {
        var activity = Load(
            "Ui",
            "Pages",
            "ActivityPage.xaml");
        var collection = activity
            .Descendants(Maui + "CollectionView")
            .Single();
        Assert.Equal(
            "{Binding Items}",
            AttributeValue(collection, "ItemsSource"));
        Assert.DoesNotContain(
            activity.Descendants(Maui + "Label"),
            label => AttributeValue(label, "Text") ==
                     "ผู้ขายตกลงขายกล้องแล้ว");
    }

    [Fact]
    public void StartupLogo_IsNonInteractiveAndHasOneAccessibleName()
    {
        var page = Load("Ui", "Pages", "StartupLogoPage.xaml");
        var mark = Load(
            "Ui",
            "Controls",
            "TransactionRailMarkView.xaml");

        Assert.Empty(page.Descendants(Maui + "Button"));
        Assert.Empty(page.Descendants(Maui + "Entry"));
        Assert.Contains(
            page.Descendants(),
            element =>
                element.Name.LocalName == "TransactionRailMarkView");

        Assert.Equal(
            "True",
            AttributeValue(
                mark.Root!,
                "AutomationProperties.IsInAccessibleTree"));
        Assert.Equal(
            "โลโก้ TOKLONG",
            AttributeValue(
                mark.Root!,
                "SemanticProperties.Description"));

        var decorativeChildren = mark
            .Descendants()
            .Where(element =>
                element.Name.LocalName is "Image" or "Border" or "Label");
        Assert.All(
            decorativeChildren,
            element => Assert.Equal(
                "False",
                AttributeValue(
                    element,
                    "AutomationProperties.IsInAccessibleTree")));
    }

    [Fact]
    public void StartupLogo_IsNotPartOfShellHistory()
    {
        var shell = Load("Ui", "AppShell.xaml");

        Assert.DoesNotContain(
            shell.Descendants(),
            element =>
                AttributeValue(element, "Route") == "startup");
        Assert.Equal(
            "welcome",
            AttributeValue(
                shell.Descendants()
                    .First(element =>
                        element.Name.LocalName == "ShellContent"),
                "Route"));
    }

    [Fact]
    public void StartupServices_AreRegisteredAndShellIsInstalledAfterIntro()
    {
        var program = File.ReadAllText(
            Path.Combine(
                AppContext.BaseDirectory,
                "Ui",
                "MauiProgram.cs"));
        var app = File.ReadAllText(
            Path.Combine(
                AppContext.BaseDirectory,
                "Ui",
                "App.xaml.cs"));

        Assert.Contains(
            "AddSingleton<IStartupMotionPreference, StartupMotionPreference>()",
            program);
        Assert.Contains("AddSingleton<StartupCoordinator>()", program);
        Assert.Contains("AddSingleton<StartupLogoPage>()", program);
        Assert.Contains("new Window(startupPage)", app);
        Assert.Contains("window.Page = shell", app);
        Assert.DoesNotContain(
            "IAuthenticationService authentication",
            app);
    }

    [Fact]
    public void EveryUserFacingFormControl_UsesTheSharedControlStyle()
    {
        var pageDirectory = Path.Combine(AppContext.BaseDirectory, "Ui", "Pages");
        var pages = Directory.GetFiles(pageDirectory, "*.xaml");

        Assert.NotEmpty(pages);

        foreach (var page in pages)
        {
            var document = XDocument.Load(page);

            AssertStyle(document, page, "Entry", "RefinedEntry", "RefinedAmountEntry");
            AssertStyle(document, page, "ThaiMobilePhoneEntry", "RefinedEntry");
            AssertStyle(document, page, "Picker", "RefinedPicker");
            AssertStyle(document, page, "Editor", "RefinedEditor");
        }
    }

    private static void AssertStyle(
        XDocument document,
        string page,
        string controlName,
        params string[] allowedStyles)
    {
        foreach (var control in document.Descendants().Where(element => element.Name.LocalName == controlName))
        {
            var style = AttributeValue(control, "Style");
            var isAllowed = allowedStyles.Any(
                allowed => style == $"{{StaticResource {allowed}}}");

            Assert.True(
                isAllowed,
                $"{Path.GetFileName(page)} contains {controlName} without an approved shared style.");
            Assert.Null(control.Attribute("FontSize"));
        }
    }

    private static XDocument Load(params string[] pathSegments)
    {
        var path = pathSegments.Prepend(AppContext.BaseDirectory).ToArray();
        return XDocument.Load(Path.Combine(path));
    }

    private static string ResourceValue(XDocument document, string key)
    {
        var resource = document
            .Descendants()
            .Single(element => AttributeValue(element, "Key") == key);

        return resource.Value.Trim();
    }

    private static string StyleSetterValue(XDocument document, string styleKey, string property)
    {
        var style = document
            .Descendants(Maui + "Style")
            .Single(element => AttributeValue(element, "Key") == styleKey);
        var setter = style
            .Elements(Maui + "Setter")
            .Single(element => AttributeValue(element, "Property") == property);

        return AttributeValue(setter, "Value")
            ?? throw new InvalidOperationException($"Style '{styleKey}' has no value for '{property}'.");
    }

    private static string? AttributeValue(XElement element, string localName)
    {
        return element
            .Attributes()
            .SingleOrDefault(attribute => attribute.Name.LocalName == localName)
            ?.Value;
    }

    private static int RequiredIntAttribute(
        XElement element,
        string localName) =>
        int.Parse(
            AttributeValue(element, localName)
            ?? throw new InvalidOperationException(
                $"Element '{element.Name.LocalName}' has no '{localName}' attribute"));
}
